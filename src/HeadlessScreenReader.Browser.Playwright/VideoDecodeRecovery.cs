using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record VideoDecodeAttempt(int Number, string Stage, string SourceSha256, int SourceBytes,
    DateTimeOffset StartedAtUtc, double ElapsedMilliseconds, string Outcome, string? ErrorCode,
    bool OriginalUnchanged, bool? ProcessExitConfirmed, int? DecodedFrames)
{
    public string Scope => "StoredOriginalVideoDecode";
    public bool WebsiteInputRetried => false;
}
public sealed record RecordingDecodeAttempt(string File, VideoDecodeAttempt Decode);

/// <summary>Retries only a settled decoder timeout against a private immutable original, never browser input.</summary>
internal static class VideoDecodeRecovery
{
    private const int MaximumAttempts = 2;

    internal static async Task<ImmutableArray<byte[]>> DecodeAsync(byte[] original,
        Func<byte[], Task<ImmutableArray<byte[]>>> settledAttempt, Action<VideoDecodeAttempt>? observe = null)
    {
        if (original.Length is 0 or > 16 * 1024 * 1024) throw new BrowserOperationException("CAPTURE_MEDIA_SIZE_INVALID");
        var snapshot = original.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(snapshot));
        for (var number = 1; number <= MaximumAttempts; number++)
        {
            var started = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew();
            ImmutableArray<byte[]> frames = default; Exception? failure = null;
            try { frames = await settledAttempt(snapshot).ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            var unchanged = Convert.ToHexStringLower(SHA256.HashData(snapshot)) == hash;
            var code = failure switch
            {
                null => null,
                OperationCanceledException => "CAPTURE_DECODE_CANCELLED",
                BrowserOperationException { Code: "CAPTURE_DECODE_TIMEOUT" } => "CAPTURE_DECODE_TIMEOUT",
                BrowserOperationException { Code: "CAPTURE_DECODE_FAILED" } => "CAPTURE_DECODE_FAILED",
                BrowserOperationException { Code: "CAPTURE_FRAME_INVALID" } => "CAPTURE_FRAME_INVALID",
                BrowserOperationException { Code: "CAPTURE_FRAME_LIMIT" } => "CAPTURE_FRAME_LIMIT",
                BrowserOperationException { Code: "CAPTURE_DIMENSIONS_INVALID" } => "CAPTURE_DIMENSIONS_INVALID",
                _ => "CAPTURE_DECODE_UNCONFIRMED"
            };
            // DecodeCoreAsync returns/throws timeout only after joining its exact decoder process and streams.
            bool? exited = failure is null || code == "CAPTURE_DECODE_TIMEOUT" ? true : null;
            if (!unchanged) { failure = new BrowserOperationException("CAPTURE_ORIGINAL_CHANGED"); code = "CAPTURE_ORIGINAL_CHANGED"; }
            observe?.Invoke(new(number, "OriginalVideoFrames", hash, snapshot.Length, started, elapsed.Elapsed.TotalMilliseconds,
                failure is null ? "Completed" : code == "CAPTURE_DECODE_TIMEOUT" ? "TimedOut" : "Failed", code,
                unchanged, exited, failure is null ? frames.Length : null));
            if (failure is null) return frames;
            if (unchanged && code == "CAPTURE_DECODE_TIMEOUT" && number < MaximumAttempts) continue;
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        throw new BrowserOperationException("CAPTURE_DECODE_UNCONFIRMED");
    }
}
