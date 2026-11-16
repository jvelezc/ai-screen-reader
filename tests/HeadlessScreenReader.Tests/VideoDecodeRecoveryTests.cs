using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class VideoDecodeRecoveryTests
{
    [Fact]
    public void HistoricalMediaWithoutRecoveryDiagnosticsKeepsItsExactPayload()
    {
        const string original = """
            {"Source":[],"Video":[],"Artifacts":[],"PreciseFrames":[],"SourceClock":"ChromiumCdpJsonDouble","VideoFramesPerSecond":25,"SourcePresentationOrder":[],"SourceInPresentationOrder":[]}
            """;
        var media = JsonSerializer.Deserialize<RecordingProbeMedia>(original, ReaderWorkerWire.Json)!;
        Assert.Null(media.DecodeAttempts);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(original), JsonSerializer.SerializeToElement(media, ReaderWorkerWire.Json)));
    }

    [Fact]
    public async Task SettledVideoTimeoutRetriesOnlyTheSamePrivateOriginalAndRetainsBothOutcomes()
    {
        byte[] original = [1, 2, 3, 4]; var expected = original.ToArray();
        var attempts = new List<VideoDecodeAttempt>(); var calls = 0;
        var result = await VideoDecodeRecovery.DecodeAsync(original, bytes =>
        {
            Assert.NotSame(original, bytes);
            Assert.Equal(expected, bytes);
            calls++;
            if (calls == 1) { original[0] = 99; throw new BrowserOperationException("CAPTURE_DECODE_TIMEOUT"); }
            return Task.FromResult<ImmutableArray<byte[]>>([[7, 8]]);
        }, attempts.Add);
        Assert.Equal(2, calls);
        Assert.Equal(new byte[] { 7, 8 }, Assert.Single(result));
        Assert.Equal(new[] { "TimedOut", "Completed" }, attempts.Select(attempt => attempt.Outcome));
        Assert.Equal(new[] { 1, 2 }, attempts.Select(attempt => attempt.Number));
        Assert.All(attempts, attempt =>
        {
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(expected)), attempt.SourceSha256);
            Assert.True(attempt.OriginalUnchanged);
            Assert.True(attempt.ProcessExitConfirmed);
        });
        Assert.Equal("CAPTURE_DECODE_TIMEOUT", attempts[0].ErrorCode);
        Assert.Null(attempts[0].DecodedFrames);
        Assert.Equal(1, attempts[1].DecodedFrames);
    }

    [Theory]
    [InlineData("CAPTURE_DECODE_FAILED")]
    [InlineData("CAPTURE_FRAME_INVALID")]
    [InlineData("CAPTURE_FRAME_LIMIT")]
    public async Task InvalidVideoDoesNotGainADecoderRetry(string code)
    {
        var error = new BrowserOperationException(code); var calls = 0;
        var attempts = new List<VideoDecodeAttempt>();
        var actual = await Assert.ThrowsAsync<BrowserOperationException>(() => VideoDecodeRecovery.DecodeAsync([1], _ =>
        { calls++; throw error; }, attempts.Add));
        Assert.Same(error, actual); Assert.Equal(1, calls);
        Assert.Equal(code, Assert.Single(attempts).ErrorCode);
        Assert.Null(attempts[0].ProcessExitConfirmed);
    }

    [Fact]
    public async Task ASecondSettledVideoTimeoutIsTerminalAndNeverBecomesARecording()
    {
        var attempts = new List<VideoDecodeAttempt>(); var calls = 0;
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => VideoDecodeRecovery.DecodeAsync([1], _ =>
        { calls++; throw new BrowserOperationException("CAPTURE_DECODE_TIMEOUT"); }, attempts.Add));
        Assert.Equal("CAPTURE_DECODE_TIMEOUT", error.Code); Assert.Equal(2, calls);
        Assert.Equal(2, attempts.Count); Assert.All(attempts, attempt => Assert.Null(attempt.DecodedFrames));
    }

    [Fact]
    public async Task CancelledDecodeDoesNotStartAnotherInvocation()
    {
        var calls = 0; var attempts = new List<VideoDecodeAttempt>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoDecodeRecovery.DecodeAsync([1], _ =>
        { calls++; throw new OperationCanceledException(); }, attempts.Add));
        Assert.Equal(1, calls); Assert.Equal("CAPTURE_DECODE_CANCELLED", Assert.Single(attempts).ErrorCode);
        Assert.Null(attempts[0].ProcessExitConfirmed);
    }

    [Fact]
    public async Task AChangedDecoderSnapshotCannotBeRetriedOrAccepted()
    {
        var calls = 0; var attempts = new List<VideoDecodeAttempt>();
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => VideoDecodeRecovery.DecodeAsync([1], bytes =>
        { calls++; bytes[0] = 99; throw new BrowserOperationException("CAPTURE_DECODE_TIMEOUT"); }, attempts.Add));
        Assert.Equal("CAPTURE_ORIGINAL_CHANGED", error.Code); Assert.Equal(1, calls);
        Assert.False(Assert.Single(attempts).OriginalUnchanged);
        Assert.Null(attempts[0].DecodedFrames);
    }
}
