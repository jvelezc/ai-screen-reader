using System.Net;
using System.Net.Http.Headers;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Signed private S3 objects with bounded digest-verified readback. This is not S3 Object Lock.</summary>
public sealed class S3TraversalObjectStore : ITraversalObjectStore, IDisposable
{
    private readonly S3EvidenceConfiguration _configuration;
    private readonly HttpClient _http;
    public S3TraversalObjectStore(S3EvidenceConfiguration configuration) : this(configuration, null) { }
    internal S3TraversalObjectStore(S3EvidenceConfiguration configuration, HttpMessageHandler? transport)
    { _configuration = configuration; _http = configuration.CreateClient(transport); }
    private Uri Address(TraversalArchiveIdentity identity, string? part) =>
        new(_configuration.Endpoint, _configuration.Bucket + "/" + TraversalArchive.ObjectKey(identity, part));

    public async Task<byte[]?> ReadAsync(TraversalArchiveIdentity identity, string? partSha256, CancellationToken token)
    {
        var address = Address(identity, partSha256); token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode != HttpStatusCode.OK) throw new TraversalArchiveException(TraversalArchiveFailure.Unavailable);
            if (response.Content.Headers.ContentLength is <= 0 or > TraversalArchive.ObjectBytes)
                throw new TraversalArchiveException(TraversalArchiveFailure.Invalid);
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var result = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (read == 0) break;
                if (result.Length + read > TraversalArchive.ObjectBytes) throw new TraversalArchiveException(TraversalArchiveFailure.Invalid);
                result.Write(buffer, 0, read);
            }
            var bytes = result.ToArray();
            if (response.Content.Headers.ContentLength is { } length && length != bytes.Length)
                throw new TraversalArchiveException(TraversalArchiveFailure.Invalid);
            TraversalArchive.ValidateObject(identity, partSha256, bytes); return bytes;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TraversalArchiveException) { throw; }
        catch (Exception) { throw new TraversalArchiveException(TraversalArchiveFailure.Unavailable); }
    }
    public async Task WriteAsync(TraversalArchiveIdentity identity, string? partSha256, byte[] bytes, CancellationToken token)
    {
        bytes = bytes.ToArray(); TraversalArchive.ValidateObject(identity, partSha256, bytes);
        token.ThrowIfCancellationRequested();
        // Reuse only already hash-matching objects. Existing conflicting bytes are not repaired by overwrite.
        if (await ReadAsync(identity, partSha256, token).ConfigureAwait(false) is not null) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, Address(identity, partSha256));
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(partSha256 is null ? "application/json" : "application/octet-stream");
            request.Headers.TryAddWithoutValidation("x-amz-content-sha256", partSha256 ?? identity.IndexSha256);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (response.StatusCode != HttpStatusCode.OK) throw new TraversalArchiveException(TraversalArchiveFailure.WriteUnconfirmed);
            var actual = await ReadAsync(identity, partSha256, timeout.Token).ConfigureAwait(false);
            if (actual is null || !bytes.AsSpan().SequenceEqual(actual)) throw new TraversalArchiveException(TraversalArchiveFailure.WriteUnconfirmed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TraversalArchiveException) { throw; }
        catch (Exception) { throw new TraversalArchiveException(TraversalArchiveFailure.WriteUnconfirmed); }
    }
    public void Dispose() => _http.Dispose();
}
