using System.Security.Cryptography;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumRecordingSynchronizationTests
{
    [Fact]
    public async Task CancelledBeforeLaunch_RetainsFailedReceiptWithoutInventingMediaOrInput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var receipt = await RecordingSynchronizationDemo.RunAsync(Path.Combine("artifacts", "recording-sync-tests"), cancellation.Token);
        Assert.Equal("RECORDING_PROBE_CANCELLED", receipt.ErrorCode);
        Assert.Equal("Launch", receipt.Stage);
        Assert.Null(receipt.Synchronization);
        Assert.Null(receipt.Media);
        Assert.Empty(receipt.Actions);
        Assert.Empty(receipt.Timeline);
        Assert.True(File.Exists(Path.Combine(receipt.Folder, "receipt.json")));
        Assert.Contains("No original recording finalized", File.ReadAllText(Path.Combine(receipt.Folder, "index.md")));
    }

    [Fact]
    public async Task TwentyActualReaderActions_AlignOriginalVideoWithinTargetIncludingUncertainty()
    {
        var receipt = await RecordingSynchronizationDemo.RunAsync(Path.Combine("artifacts", "recording-sync-tests"));
        Assert.Null(receipt.ErrorCode);
        Assert.Equal("Complete", receipt.Stage);
        Assert.Equal(SynchronizationVerification.Verified, receipt.Synchronization!.Verification);
        Assert.Equal(250, receipt.Synchronization.TargetMilliseconds);
        Assert.Equal(20, receipt.Synchronization.Markers.Length);
        Assert.All(receipt.Synchronization.Markers, m =>
        {
            Assert.NotNull(m.VideoFrameIndex);
            Assert.InRange(m.ConservativeAbsoluteErrorMilliseconds!.Value, 0, 250);
        });
        Assert.Equal(14, receipt.ClockSamples.Length);
        Assert.Equal(20, receipt.Actions.Length);
        Assert.All(receipt.Actions, a => Assert.True(a.ReaderGoalVerified));
        Assert.All(receipt.Steps, s => Assert.Equal(VerificationStatus.Satisfied, s.Verification));
        Assert.Equal(20, receipt.Timeline.Count(e => e.Kind == ReaderEventKind.InputAttempted && e.Detail == "ArrowRight"));
        Assert.Equal(20, receipt.Timeline.Count(e => e.Kind == ReaderEventKind.InputCompleted && e.Detail == "ArrowRight"));
        Assert.Equal(21, receipt.Timeline.Count(e => e.Kind == ReaderEventKind.InputCompleted));
        Assert.Equal(CapturePhase.Closed, receipt.CaptureLifecycle!.Phase);
        Assert.Contains(receipt.Media!.Artifacts, a => a.File == "original.webm");
        Assert.Equal("ChromiumCdpJsonDouble", receipt.Media.SourceClock);
        Assert.All(receipt.Media.Video, f => Assert.NotNull(f.Marker));
        Assert.True(receipt.Media.PreciseFrames.Select(f => f.PresentationUnixMilliseconds).Distinct().Count() > 20);
        Assert.All(receipt.Media.Artifacts, a =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(receipt.Folder, a.File));
            Assert.Equal(bytes.Length, a.Bytes);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), a.Sha256);
        });
        Assert.True(File.Exists(Path.Combine(receipt.Folder, "receipt.json")));
        Assert.Contains("Conservative absolute error", File.ReadAllText(Path.Combine(receipt.Folder, "index.md")));
    }
}
