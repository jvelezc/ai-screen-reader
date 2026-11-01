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
        Assert.Null(result.CollectorFailureCode);
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
        Assert.True(result.Recording.Alignment!.Calibrated, JsonSerializer.Serialize(new
        {
            Route = route,
            result.Recording.Alignment,
            result.Recording.ClockSamples,
            result.Recording.RunnerOriginHostMilliseconds,
            SourceFrameCount = result.Recording.Media!.Source.Length,
            VideoFrameCount = result.Recording.Media.Video.Length,
            FirstVideoMarker = result.Recording.Media.Video.FirstOrDefault()?.Marker,
            SourceMarkers = result.Recording.Media.Source.Select(frame => new { frame.PresentationUnixMilliseconds, frame.Marker }),
            VideoTransitions = result.Recording.Media.Video.Where((frame, index) => index == 0 || frame.Marker != result.Recording.Media.Video[index - 1].Marker)
                .Select(frame => new { frame.Index, frame.PresentationMilliseconds, frame.Marker })
        }));
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
            media.Artifacts.Single(a => a.File == "original.webm").Sha256, media.SourceInPresentationOrder, media.Video);
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
        var package = await TraversalEvidencePackage.CreateAsync(folder, [result], DateTimeOffset.UtcNow);
        var verified = await TraversalEvidencePackage.VerifyAsync(folder, package.ManifestSha256);
        Assert.True(verified.IntegrityMatches);
        Assert.Equal(package.Files, verified.Files);
        using var archive = System.IO.Compression.ZipFile.OpenRead(package.Archive);
        var originalEntry = Assert.Single(archive.Entries, entry => entry.FullName == "route-1-media/original.webm");
        await using var videoStream = originalEntry.Open();
        Assert.Equal(result.Recording.Alignment.VideoSha256, Convert.ToHexStringLower(await SHA256.HashDataAsync(videoStream)));
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
    public async Task TerminalObservedCheckpointPrecedesTheFinalOriginalTransfer()
    {
        var folder = Path.Combine(NewFolder(), "media");
        var plan = TraversalDemo.Plans[0];
        var terminalIntent = false; var terminalObserved = false;
        var prematureTransfer = false; var acquisitionContinuedDuringTransfer = false;
        var finalCopies = 0; var before = -1; var after = -1;
        var copied = new HashSet<int>();
        var result = await TraversalRecordingDemo.RunRouteAsync(folder, plan, checkpoint: async (checkpoint, token) =>
        {
            if (checkpoint.CommandIndex != plan.Instructions.Length) return;
            if (checkpoint.Phase == TraversalCheckpointPhase.Intent)
            {
                terminalIntent = true;
                // Actual renderer time models a pending durable intent acknowledgement.
                await Task.Delay(250, token);
            }
            if (checkpoint.Phase == TraversalCheckpointPhase.Observed) terminalObserved = true;
        }, sources: async (originals, token) =>
        {
            if (terminalIntent)
            {
                prematureTransfer |= !terminalObserved;
                finalCopies++;
                before = Directory.GetFiles(folder, "source-*.jpg").Length;
                // The final transfer may wait on storage after acquisition has been joined.
                await Task.Delay(500, token);
                after = Directory.GetFiles(folder, "source-*.jpg").Length;
                acquisitionContinuedDuringTransfer |= before != after;
            }
            foreach (var source in originals)
            {
                Assert.True(copied.Add(source.Frame.Sequence), "An acquired original was transferred twice.");
                Assert.Equal(source.Frame.Sha256, Convert.ToHexStringLower(SHA256.HashData(source.Content)));
            }
        });
        Assert.False(prematureTransfer, "The final source transfer ran before the terminal observed checkpoint.");
        Assert.False(acquisitionContinuedDuringTransfer, "Acquisition continued during the final source transfer.");
        Assert.True(terminalIntent); Assert.True(terminalObserved); Assert.Equal(1, finalCopies);
        Assert.InRange(before, 3, 250); Assert.Equal(before, after);
        Assert.Null(result.Recording.ErrorCode); Assert.Equal("Complete", result.Recording.Stage);
        var media = Assert.IsType<RecordingProbeMedia>(result.Recording.Media);
        Assert.Equal(media.PreciseFrames.Length, copied.Count);
        Assert.Equal(TraversalReachability.Reached, result.Receipt!.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, result.Receipt.Barrier);
        Assert.Equal(2, result.Receipt.AttemptedKeystrokes); Assert.Equal(2, result.Receipt.CompletedKeystrokes);
        Assert.Equal(CapturePhase.Closed, result.Recording.CaptureLifecycle!.Phase);
        Assert.Equal(BrowserShutdownPhase.Closed, result.Recording.Shutdown!.Phase);
        Assert.Equal(0, result.Recording.Shutdown.ActiveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalCheckpointFailureCannotStartTheFinalTransferOrClaimARecording(bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        var folder = Path.Combine(NewFolder(), "media");
        var plan = TraversalDemo.Plans[0];
        var terminalIntent = false; var terminalCopies = 0;
        TraversalAttempt? lastAttempt = null;
        var result = await TraversalRecordingDemo.RunRouteAsync(folder, plan, cancellation.Token,
            checkpoint: async (checkpoint, token) =>
            {
                if (checkpoint.CommandIndex != plan.Instructions.Length) return;
                if (checkpoint.Phase == TraversalCheckpointPhase.Intent)
                {
                    terminalIntent = true;
                    await Task.Delay(250, token);
                }
                if (checkpoint.Phase != TraversalCheckpointPhase.Observed) return;
                lastAttempt = checkpoint.Attempt;
                if (cancelled) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                throw new IOException("synthetic-private-terminal-checkpoint");
            }, sources: (_, _) =>
            {
                if (terminalIntent) terminalCopies++;
                return Task.CompletedTask;
            });
        var attempt = Assert.IsType<TraversalAttempt>(lastAttempt);
        Assert.Equal(1, attempt.Step!.BrowserKeystrokes);
        Assert.Equal(VerificationStatus.Satisfied, attempt.Step.Verification);
        Assert.Equal(0, terminalCopies);
        Assert.Null(result.Recording.Media);
        Assert.False(result.Receipt?.HasRecording ?? false);
        Assert.Equal(cancelled ? "TRAVERSAL_RECORDING_CANCELLED" : "TRAVERSAL_RECORDING_FAILED", result.Recording.ErrorCode);
        Assert.Equal(cancelled ? "CalibrateAfter" : "Traverse", result.Recording.Stage);
        Assert.DoesNotContain("synthetic-private-terminal-checkpoint", JsonSerializer.Serialize(result));
        Assert.Equal(BrowserShutdownPhase.Closed, result.Recording.Shutdown!.Phase);
        Assert.Equal(0, result.Recording.Shutdown.ActiveCalls);
    }

    [Fact]
    public async Task IntermediateObservedCheckpointStillReceivesItsCompletedOriginalsBeforeAcknowledgement()
    {
        var folder = Path.Combine(NewFolder(), "media");
        var waitingForObserved = false; var transfers = 0; var transferredBeforeObserved = -1;
        var result = await TraversalRecordingDemo.RunRouteAsync(folder, TraversalDemo.Plans[2],
            checkpoint: async (checkpoint, token) =>
            {
                if (checkpoint.CommandIndex != 1) return;
                if (checkpoint.Phase == TraversalCheckpointPhase.Intent)
                {
                    waitingForObserved = true;
                    await Task.Delay(250, token);
                }
                if (checkpoint.Phase == TraversalCheckpointPhase.Observed)
                {
                    transferredBeforeObserved = transfers;
                    waitingForObserved = false;
                }
            }, sources: (_, _) =>
            {
                if (waitingForObserved) transfers++;
                return Task.CompletedTask;
            });
        Assert.InRange(transferredBeforeObserved, 1, 250);
        Assert.Null(result.Recording.ErrorCode); Assert.Equal("Complete", result.Recording.Stage);
        Assert.Equal(TraversalReachability.Unknown, result.Receipt!.Reachability);
        Assert.Equal(3, result.Receipt.AttemptedKeystrokes); Assert.Equal(3, result.Receipt.CompletedKeystrokes);
        Assert.Equal(CapturePhase.Closed, result.Recording.CaptureLifecycle!.Phase);
        Assert.Equal(BrowserShutdownPhase.Closed, result.Recording.Shutdown!.Phase);
    }

    [Fact]
    public async Task FinalOriginalTransferCannotExtendActualSourceAcquisitionAfterTheRouteEnds()
    {
        var folder = Path.Combine(NewFolder(), "media");
        var plan = TraversalDemo.Plans[1];
        var ended = false; var finalCopies = 0; var before = -1; var after = -1;
        var transferred = new Dictionary<int, CompletedCaptureSource>();
        var result = await TraversalRecordingDemo.RunRouteAsync(folder, plan, checkpoint: (checkpoint, _) =>
        {
            if (checkpoint.Phase == TraversalCheckpointPhase.Observed && checkpoint.CommandIndex == plan.Instructions.Length) ended = true;
            return Task.CompletedTask;
        }, sources: async (originals, token) =>
        {
            foreach (var source in originals) transferred.Add(source.Frame.Sequence, source);
            if (!ended) return;
            finalCopies++;
            before = Directory.GetFiles(folder, "source-*.jpg").Length;
            // Actual renderer time models a pending storage acknowledgement after all route commands.
            await Task.Delay(500, token);
            after = Directory.GetFiles(folder, "source-*.jpg").Length;
        });
        Assert.Equal(1, finalCopies); Assert.InRange(before, 3, 250); Assert.Equal(before, after);
        Assert.Null(result.Recording.ErrorCode); Assert.Equal("Complete", result.Recording.Stage);
        var media = Assert.IsType<RecordingProbeMedia>(result.Recording.Media);
        Assert.Equal(media.PreciseFrames.Length, transferred.Count);
        foreach (var frame in media.PreciseFrames)
        {
            var source = transferred[frame.Sequence];
            Assert.Equal(frame, source.Frame);
            Assert.Equal(frame.Sha256, Convert.ToHexStringLower(SHA256.HashData(source.Content)));
            Assert.Equal(source.Content, await File.ReadAllBytesAsync(Path.Combine(folder, frame.File)));
        }
        Assert.Equal(TraversalReachability.NotReached, result.Receipt!.Reachability);
        Assert.Equal(TraversalBarrier.Observed, result.Receipt.Barrier);
        Assert.Equal(0, result.Receipt.AttemptedKeystrokes); Assert.Equal(0, result.Receipt.CompletedKeystrokes);
        Assert.Equal(CapturePhase.Closed, result.Recording.CaptureLifecycle!.Phase);
        Assert.Equal(BrowserShutdownPhase.Closed, result.Recording.Shutdown!.Phase);
        Assert.Equal(0, result.Recording.Shutdown.ActiveCalls);
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
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(batch, TraversalEvidencePackage.ManifestFile)));
        Assert.Equal(4, manifest.RootElement.GetProperty("PlannedRoutes").GetInt32());
        var route = Assert.Single(manifest.RootElement.GetProperty("Routes").EnumerateArray());
        Assert.Equal("Unknown", route.GetProperty("Reachability").GetString());
        Assert.False(route.GetProperty("HasRecording").GetBoolean());
        Assert.False(route.GetProperty("TimingCalibrated").GetBoolean());
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
