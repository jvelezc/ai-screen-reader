using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeadlessScreenReader.Worker;

/// <summary>Closed operational facts, never storage URLs, signed headers, bodies or exception text.</summary>
internal sealed record S3ReadDiagnostic(string Schema, string? SupportReference, Guid? OrganizationId, Guid? JobId,
    string ObjectKeySha256, string? OriginalSha256, string Operation, int Attempt, string Outcome, string Cause,
    int? HttpStatus, long? DeclaredBytes, long ReceivedBytes, string? ProviderRequestId, string? TransportReason,
    DateTimeOffset Utc, double ElapsedMilliseconds)
{
    public bool WebsiteInputRetried => false;
}

/// <summary>One settled GET recovery inside the client's existing timeout. Writes are never repeated.</summary>
internal sealed class S3ReadResilienceHandler(HttpMessageHandler transport, Action<S3ReadDiagnostic> diagnostic)
    : DelegatingHandler(transport)
{
    internal const int MaximumBodyBytes = 26 * 1024 * 1024;
    internal const string SupportReferenceTag = "hsr.support_reference";

    internal static void WriteDiagnostic(S3ReadDiagnostic value)
    {
        try { Console.Error.WriteLine(JsonSerializer.Serialize(value, ReaderWorkerWire.Json)); }
        catch (Exception) { /* Diagnostics never change storage or caller authority. */ }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Method != HttpMethod.Get || request.Content is not null)
            return await base.SendAsync(request, token).ConfigureAwait(false);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            HttpResponseMessage? response = null;
            long received = 0;
            var stage = "Response";
            var retry = false;
            using var copy = Copy(request);
            try
            {
                response = await base.SendAsync(copy, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    retry = Transient(response.StatusCode);
                    Record(request, response, attempt, retry && attempt == 1 ? "RetryingRead" : "ReturnedHttpFailure", "HttpStatus", received, null, started);
                    if (!retry || attempt == 2) return response;
                }
                else
                {
                    stage = "Body";
                    var length = response.Content.Headers.ContentLength;
                    if (length is null or < 0 or > MaximumBodyBytes)
                    {
                        // Existing stores have different bounded metadata guards. Preserve their
                        // Invalid/Unavailable classification rather than replacing it with I/O failure.
                        Record(request, response, attempt, "DeferredToCaller", length is null ? "LengthMissing" : "BodyLimitExceeded", received, null, started);
                        return response;
                    }
                    await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    using var buffer = new MemoryStream((int)length.Value);
                    var chunk = new byte[65536];
                    while (true)
                    {
                        var count = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
                        if (count == 0) break;
                        received += count;
                        if (received > length.Value)
                        {
                            Record(request, response, attempt, "Failed", "BodyLimitExceeded", received, null, started);
                            throw new InvalidDataException("EVIDENCE_READ_RESPONSE_INVALID");
                        }
                        buffer.Write(chunk, 0, count);
                    }
                    if (received != length.Value) throw new EndOfStreamException("EVIDENCE_READ_BODY_INCOMPLETE");
                    var original = response.Content;
                    var content = new ByteArrayContent(buffer.ToArray());
                    foreach (var header in original.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    response.Content = content;
                    original.Dispose();
                    if (attempt == 2) Record(request, response, attempt, "RecoveredRead", "VerifiedLength", received, null, started);
                    return response;
                }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
            {
                if (error is InvalidDataException) { response?.Dispose(); throw; }
                var cancelled = token.IsCancellationRequested;
                var cause = cancelled ? "CallerCancelled" : error is OperationCanceledException ? "ReadTimeout"
                    : stage == "Body" ? "BodyReadInterrupted" : "TransportFailure";
                var transportReason = error is HttpRequestException http ? http.HttpRequestError.ToString()
                    : error is HttpIOException io ? io.HttpRequestError.ToString() : null;
                Record(request, response, attempt, !cancelled && attempt == 1 ? "RetryingRead" : "Failed", cause, received, transportReason, started);
                response?.Dispose(); response = null;
                if (cancelled || attempt == 2) throw;
                retry = true;
            }
            catch { response?.Dispose(); throw; }
            // The previous response and stream have settled before a fresh, identical read.
            response?.Dispose();
            if (retry) await Task.Delay(TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false);
        }
        throw new IOException("EVIDENCE_READ_UNCONFIRMED");
    }

    private static bool Transient(HttpStatusCode status) => (int)status is 408 or 429 or 500 or 502 or 503 or 504;
    private static HttpRequestMessage Copy(HttpRequestMessage original)
    {
        var copy = new HttpRequestMessage(original.Method, original.RequestUri)
        { Version = original.Version, VersionPolicy = original.VersionPolicy };
        foreach (var header in original.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }

    private void Record(HttpRequestMessage request, HttpResponseMessage? response, int attempt, string outcome, string cause,
        long received, string? transportReason, long started)
    {
        string? reference = null;
        for (var activity = Activity.Current; activity is not null; activity = activity.Parent)
            if (activity.GetTagItem(SupportReferenceTag) is string candidate && Hex(candidate, 32)) { reference = candidate; break; }
        var path = request.RequestUri!.AbsolutePath;
        var pieces = path.Split('/');
        Guid? organization = null, job = null;
        if (pieces.Length >= 4 && pieces[^4] == "public-tasks"
            && Guid.TryParseExact(pieces[^3], "N", out var org) && org != Guid.Empty
            && Guid.TryParseExact(pieces[^2], "N", out var id) && id != Guid.Empty) { organization = org; job = id; }
        string? provider = null;
        if (response?.Headers.TryGetValues("x-amz-request-id", out var values) == true)
        {
            var candidates = values.Take(2).ToArray();
            if (candidates.Length == 1 && candidates[0] is { Length: > 0 and <= 128 } value
                && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) provider = value;
        }
        var diagnosticValue = new S3ReadDiagnostic("hsr-evidence-read-v1", reference, organization, job,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path))), Hex(pieces[^1], 64) ? pieces[^1] : null,
            request.Headers.Range is null ? "GetObject" : "GetRange", attempt, outcome, cause,
            response is null ? null : (int)response.StatusCode, response?.Content.Headers.ContentLength, received, provider,
            transportReason, DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        try { diagnostic(diagnosticValue); }
        catch (Exception) { /* A failed observer cannot retry input or mask the storage result. */ }
    }
    private static bool Hex(string value, int length) => value.Length == length && value.All(char.IsAsciiHexDigitLower);
}
