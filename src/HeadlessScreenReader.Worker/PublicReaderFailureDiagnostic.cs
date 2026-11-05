using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Allowlisted operational facts only. Never contains page text, URLs, credentials or exception messages.</summary>
public sealed record PublicReaderFailureDiagnostic(string Schema, string Stage, string Cause, Guid SessionId,
    int Sequence, int? ObservedNodes, int? HttpStatusCode, int? PayloadBytes, int? MaximumBytes, double ElapsedMilliseconds)
{
    public const string CurrentSchema = "hsr-public-reader-failure-v1";
    private const string LegacyFailure = "PUBLIC_READER_WORKER_UNCONFIRMED";

    public static PublicReaderFailureDiagnostic Create(string stage, Exception error, Guid session, int sequence,
        int? observedNodes, double elapsedMilliseconds)
    {
        var cause = error is BrowserOperationException browser && AllowedCause(browser.Code) ? browser.Code
            : error is OperationCanceledException ? "CANCELLED"
            : error is TimeoutException ? "TIMEOUT"
            : error is JsonException ? "JSON_INVALID"
            : error is IOException ? "TRANSPORT_FAILED"
            : "UNCONFIRMED";
        int? status = error is BrowserOperationException && error.Data["HttpStatusCode"] is int value && value is >= 400 and <= 599 ? value : null;
        int? payload = error is BrowserOperationException && error.Data["PayloadBytes"] is int size && size is >= 0 and <= 64 * 1024 * 1024 ? size : null;
        int? maximum = error is BrowserOperationException && error.Data["MaximumBytes"] is int limit && limit is > 0 and <= 64 * 1024 * 1024 ? limit : null;
        return new(CurrentSchema, AllowedStage(stage) ? stage : "Unknown", cause, session,
            Math.Clamp(sequence, 0, ReaderWorkerRequest.MaximumCaptures + 1),
            observedNodes is >= 0 and <= 1_000_000 ? observedNodes : null, status, payload, maximum,
            double.IsFinite(elapsedMilliseconds) ? Math.Clamp(elapsedMilliseconds, 0, 120_000) : 0);
    }

    public async Task WriteAsync(TextWriter diagnostics)
    {
        if (!SafeFields()) return;
        try { await diagnostics.WriteLineAsync(JsonSerializer.Serialize(this, ReaderWorkerWire.Json)).ConfigureAwait(false); }
        catch (Exception) { /* Operational logging never grants authority or changes the original failure. */ }
    }

    public static PublicReaderFailureDiagnostic? Read(byte[] bytes, Guid session)
    {
        if (bytes.Length is 0 or > 16384) return null;
        try
        {
            var lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')).ToArray();
            if (lines.Length != 2 || lines[1] != LegacyFailure || Encoding.UTF8.GetByteCount(lines[0]) > 1024) return null;
            var value = JsonSerializer.Deserialize<PublicReaderFailureDiagnostic>(lines[0], ReaderWorkerWire.Json);
            return value is not null && value.SafeFields() && value.SessionId == session
                && value.SessionId != Guid.Empty && value.Sequence >= 1
                ? value : null;
        }
        catch (Exception) { return null; }
    }

    internal static BrowserOperationException Attach(string code, PublicReaderFailureDiagnostic? diagnostic)
    {
        var error = new BrowserOperationException(code);
        if (diagnostic?.SafeFields() == true) error.Data[typeof(PublicReaderFailureDiagnostic)] = diagnostic;
        return error;
    }

    internal static PublicReaderFailureDiagnostic? From(Exception error, Guid session) =>
        error.Data[typeof(PublicReaderFailureDiagnostic)] is PublicReaderFailureDiagnostic value
            && value.SafeFields() && value.SessionId == session && session != Guid.Empty ? value : null;

    private bool SafeFields() => Schema == CurrentSchema && AllowedStage(Stage) && AllowedCause(Cause)
        && Sequence is >= 0 and <= ReaderWorkerRequest.MaximumCaptures + 1
        && (ObservedNodes is null or >= 0 and <= 1_000_000)
        && (HttpStatusCode is null or >= 400 and <= 599)
        && (PayloadBytes is null or >= 0 and <= 64 * 1024 * 1024)
        && (MaximumBytes is null or > 0 and <= 64 * 1024 * 1024)
        && double.IsFinite(ElapsedMilliseconds) && ElapsedMilliseconds is >= 0 and <= 120_000;

    private static bool AllowedStage(string stage) => stage is "Request" or "Isolation" or "Navigation" or "Capture"
        or "Response" or "Observation" or "Shutdown" or "Unknown";

    private static bool AllowedCause(string cause) => cause is "TARGET_HTTP_ERROR" or "TARGET_URL_DENIED"
        or "TARGET_REDIRECT_DENIED" or "PUBLIC_READER_WORKER_RESPONSE_INVALID" or "READER_WORKER_FRAME_INVALID"
        or "PUBLIC_READER_WORKER_REQUEST_INVALID" or "PUBLIC_READER_ISOLATION_UNCONFIRMED"
        or "PUBLIC_READER_WORKER_CLOSE_UNCONFIRMED" or "CANCELLED" or "TIMEOUT" or "JSON_INVALID"
        or "TRANSPORT_FAILED" or "UNCONFIRMED";
}
