using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public interface IObservationArchive
{
    Task FinalizeAsync(FixtureJob job, byte[] bytes, string digest, CancellationToken token);
    Task<byte[]?> ReadAsync(FixtureJob job, string digest, CancellationToken token);
}

public enum ObservationArchiveFailure { Unavailable, Invalid, WriteUnconfirmed }
public sealed class ObservationArchiveException(ObservationArchiveFailure failure) : Exception(failure switch
{
    ObservationArchiveFailure.Invalid => "EVIDENCE_S3_CONTENT_INVALID",
    ObservationArchiveFailure.WriteUnconfirmed => "EVIDENCE_S3_WRITE_UNCONFIRMED",
    _ => "EVIDENCE_S3_UNAVAILABLE"
})
{
    public ObservationArchiveFailure Failure { get; } = failure;
}

/// <summary>Content-addressed S3 evidence with actual bounded readback, without delete or listing authority.</summary>
public sealed class S3ObservationArchive : IObservationArchive, IDisposable
{
    private readonly HttpClient _http;
    private readonly S3EvidenceConfiguration _configuration;
    public S3ObservationArchive(S3EvidenceConfiguration configuration) : this(configuration, null) { }
    internal S3ObservationArchive(S3EvidenceConfiguration configuration, HttpMessageHandler? transport)
    { _configuration = configuration; _http = configuration.CreateClient(transport); }

    public static string ObjectKey(FixtureJob job, string digest)
    {
        job.Validate();
        if (digest.Length != 64 || digest.Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new ArgumentException("EVIDENCE_S3_DIGEST_INVALID");
        return $"evidence-v1/{job.OrganizationId:N}/{job.ScanId:N}/{job.JobId:N}/{job.Generation}/{digest}/observation.json";
    }
    private Uri Address(FixtureJob job, string digest) => new(_configuration.Endpoint, _configuration.Bucket + "/" + ObjectKey(job, digest));

    public async Task FinalizeAsync(FixtureJob job, byte[] bytes, string digest, CancellationToken token)
    {
        bytes = bytes.ToArray();
        ObservationStore.Validate(bytes, job);
        var address = Address(job, digest);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != digest)
            throw new ObservationArchiveException(ObservationArchiveFailure.Invalid);
        // Existing conflicting evidence is never repaired by overwriting it.
        if (await ReadAsync(job, digest, token).ConfigureAwait(false) is not null) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, address);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.TryAddWithoutValidation("x-amz-content-sha256", digest);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (response.StatusCode != HttpStatusCode.OK)
                throw new ObservationArchiveException(ObservationArchiveFailure.WriteUnconfirmed);
            var readback = await ReadAsync(job, digest, timeout.Token).ConfigureAwait(false);
            if (readback is null || !bytes.AsSpan().SequenceEqual(readback))
                throw new ObservationArchiveException(ObservationArchiveFailure.WriteUnconfirmed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (ObservationArchiveException) { throw; }
        catch (Exception) { throw new ObservationArchiveException(ObservationArchiveFailure.WriteUnconfirmed); }
    }

    public async Task<byte[]?> ReadAsync(FixtureJob job, string digest, CancellationToken token)
    {
        var address = Address(job, digest);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode != HttpStatusCode.OK)
                throw new ObservationArchiveException(ObservationArchiveFailure.Unavailable);
            if (response.Content.Headers.ContentLength is <= 0 or > FixtureObservation.MaximumBytes)
                throw new ObservationArchiveException(ObservationArchiveFailure.Invalid);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (read == 0) break;
                if (bytes.Length + read > FixtureObservation.MaximumBytes)
                    throw new ObservationArchiveException(ObservationArchiveFailure.Invalid);
                bytes.Write(buffer, 0, read);
            }
            var result = bytes.ToArray();
            if (Convert.ToHexStringLower(SHA256.HashData(result)) != digest)
                throw new ObservationArchiveException(ObservationArchiveFailure.Invalid);
            try { ObservationStore.Validate(result, job); }
            catch (Exception) { throw new ObservationArchiveException(ObservationArchiveFailure.Invalid); }
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (ObservationArchiveException) { throw; }
        catch (Exception) { throw new ObservationArchiveException(ObservationArchiveFailure.Unavailable); }
    }
    public void Dispose() => _http.Dispose();
}
