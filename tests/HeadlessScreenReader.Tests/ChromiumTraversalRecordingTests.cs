using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumTraversalRecordingTests
{
    [Theory]
    [InlineData(0, TraversalReachability.Reached, TraversalBarrier.NoneObserved, 2)]
    [InlineData(1, TraversalReachability.NotReached, TraversalBarrier.Observed, 0)]
    [InlineData(2, TraversalReachability.Unknown, TraversalBarrier.NoneObserved, 3)]
    [InlineData(3, TraversalReachability.Unknown, TraversalBarrier.NoneObserved, 4)]
    public async Task FourOriginalRecordings_LinkRealCaptureAndInputIntervalsWithoutChangingRouteClassification(
        int route, TraversalReachability reachability, TraversalBarrier barrier, int keys)
    {
        var folder = NewFolder();
        var result = await TraversalRecordingDemo.RunRouteAsync(Path.Combine(folder, "route-1-media"), TraversalDemo.Plans[route]);
        Assert.True(result.Recording.ErrorCode is null, result.Recording.ErrorCode + ": " + JsonSerializer.Serialize(result.Recording.Shutdown));
        Assert.Equal(BrowserShutdownPhase.Closed, result.Recording.Shutdown!.Phase);
        Assert.True(result.Recording.Shutdown.ProcessExitConfirmed);
        Assert.Equal(0, result.Recording.Shutdown.ActiveCalls);
        Assert.InRange(result.Recording.Shutdown.NativeElapsedMilliseconds!.Value, 0, double.MaxValue);
        Assert.Equal("Complete", result.Recording.Stage);
        var receipt = Assert.IsType<TraversalReceipt>(result.Receipt);
        Assert.Equal(reachability, receipt.Reachability);
        Assert.Equal(barrier, receipt.Barrier);
        Assert.Equal(keys, receipt.CompletedKeystrokes);
        Assert.Equal(keys, receipt.AttemptedKeystrokes);
        Assert.True(receipt.HasRecording);
        Assert.Equal(CapturePhase.Closed, result.Recording.CaptureLifecycle!.Phase);
        Assert.Equal(BundledFixture.TraversalRecording, result.Environment!.Content!.Fixture);
        Assert.Equal(14, result.Recording.ClockSamples.Length);
        Assert.True(result.Recording.RunnerOriginHostMilliseconds > 0);
        Assert.True(result.Recording.Alignment!.Calibrated);
        Assert.All(result.Recording.Alignment.Cues, cue =>
        {
            Assert.Equal(RecordingCueCoverage.Covered, cue.Coverage);
            Assert.NotNull(cue.FirstFrame);
            Assert.True(cue.StartSeconds >= 0);
            Assert.True(cue.EndSeconds > cue.StartSeconds);
        });
        Assert.Equal(receipt.Observations.Length + receipt.Attempts.Length + receipt.Timeline.Length,
            result.Recording.Alignment.Cues.Length);
        var media = Assert.IsType<RecordingProbeMedia>(result.Recording.Media);
        var recomputed = TraversalRecording.Align(receipt, RecordingSynchronization.Calibrate(result.Recording.ClockSamples),
            result.Recording.RunnerOriginHostMilliseconds!.Value, receipt.Observations[0].Snapshot.DocumentId,
            media.Artifacts.Single(a => a.File == "original.webm").Sha256, media.Source, media.Video);
        Assert.Equal(JsonSerializer.Serialize(result.Recording.Alignment), JsonSerializer.Serialize(recomputed));
        Assert.NotEmpty(media.Video);
        Assert.All(media.Video, f => Assert.NotNull(f.Marker));
        Assert.All(media.Artifacts, artifact =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(folder, "route-1-media", artifact.File));
            Assert.Equal(bytes.LongLength, artifact.Bytes);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), artifact.Sha256);
            Assert.True(artifact.DecodedFrames > 0);
        });
        Assert.DoesNotContain(receipt.Observations.SelectMany(o => o.Snapshot.Nodes), n => n.Role is "textbox" or "code");
        if (route == 2)
        {
            Assert.Contains(receipt.Observations[^1].Snapshot.Nodes, n => n.Role == "dialog" && n.Name == "Address required");
            Assert.DoesNotContain(receipt.Observations[^1].Snapshot.Nodes, n => n.Name == "Checkout preview");
        }
        if (route == 3)
        {
            Assert.Equal(VerificationStatus.TimedOut, receipt.Attempts[^1].Step!.Verification);
            Assert.Equal(receipt.Observations[^2].Fingerprint, receipt.Observations[^1].Fingerprint);
        }
        if (route != 0) Assert.DoesNotContain(receipt.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
        await TraversalArtifactWriter.WriteAsync(folder, 1, receipt, result.Environment, result.Recording);
        var report = await File.ReadAllTextAsync(Path.Combine(folder, "route-1.md"));
        Assert.Contains("pixels do not verify", report);
        Assert.Contains("#t=", report);
        Assert.DoesNotContain("<script", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", report, StringComparison.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(report, @"\]\(([^)]+)\)"))
        {
            var target = match.Groups[1].Value;
            if (target.StartsWith('#')) Assert.Contains($"id=\"{target[1..]}\"", report);
            else Assert.True(File.Exists(Path.Combine(folder, target.Split('#')[0])), target);
        }
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "route-1.json")));
        Assert.Equal(receipt.RunId, json.RootElement.GetProperty("Recording").GetProperty("Alignment").GetProperty("RunId").GetString());
        var original = await File.ReadAllTextAsync(Path.Combine(folder, "route-1.json"));
        await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(folder, 1, receipt, result.Environment, result.Recording));
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(folder, "route-1.json")));
    }

    [Fact]
    public async Task OriginalMediaChangedBeforePublishing_RejectsAttachmentsWithoutPublishingUnrelatedTrace()
    {
        var folder = NewFolder();
        var result = await TraversalRecordingDemo.RunRouteAsync(Path.Combine(folder, "route-1-media"), TraversalDemo.Plans[0]);
        Assert.Null(result.Recording.ErrorCode);
        var original = Path.Combine(folder, "route-1-media", "original.webm");
        var originalHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(original)));
        await File.WriteAllTextAsync(original, "tampered media");
        var failure = await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(folder, 1,
            result.Receipt!, result.Environment, result.Recording));
        Assert.Equal("TRAVERSAL_RECORDING_ARTIFACT_CHANGED", failure.Message);
        Assert.False(File.Exists(Path.Combine(folder, "route-1.json")));
        Assert.False(File.Exists(Path.Combine(folder, "route-1.md")));
        Assert.False(File.Exists(Path.Combine(folder, "route-1.svg")));
        Assert.Equal(originalHash, result.Recording.Alignment!.VideoSha256);
        Assert.Equal(TraversalReachability.Reached, result.Receipt!.Reachability);
    }

    [Fact]
    public async Task CancellationBeforeLaunch_RetainsFailureAndCreatesNoInputOrRecordingClaim()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var folder = NewFolder();
        var exit = await TraversalRecordingDemo.RunAsync(folder, cancellation.Token);
        Assert.Equal(130, exit);
        var batch = Assert.Single(Directory.GetDirectories(folder));
        Assert.True(File.Exists(Path.Combine(batch, "index.md")));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(batch, "route-1-failure.json")));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Receipt").ValueKind);
        var recording = json.RootElement.GetProperty("Recording");
        Assert.Equal("Launch", recording.GetProperty("Stage").GetString());
        Assert.Equal("TRAVERSAL_RECORDING_CANCELLED", recording.GetProperty("ErrorCode").GetString());
        Assert.Equal(JsonValueKind.Null, recording.GetProperty("Alignment").ValueKind);
        Assert.Equal(JsonValueKind.Null, recording.GetProperty("Media").ValueKind);
        Assert.Empty(Directory.GetFiles(batch, "*.webm", SearchOption.AllDirectories));
    }

    [Fact]
    public void SafeShopFixture_ContainsNoEditableFieldsOrLegacyPrivateCanaries()
    {
        var safe = BundledFixtureCatalog.ReadHtml(BundledFixture.TraversalRecording);
        Assert.DoesNotContain("<input", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<textarea", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contenteditable", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("traversal-account-canary", safe);
        Assert.DoesNotContain("traversal-secret-canary", safe);
        Assert.Contains("aria-hidden=\"true\"", safe);
        Assert.Contains("traversal-secret-canary", BundledFixtureCatalog.ReadHtml(BundledFixture.Traversal));
    }

    private static string NewFolder() => Path.GetFullPath(Path.Combine("artifacts", "traversal-recording-tests", Guid.NewGuid().ToString("N")));
}
