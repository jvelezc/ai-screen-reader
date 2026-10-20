using System.Security.Cryptography;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumSyntheticMediaTests
{
    [Fact]
    public async Task ActualExcludedTyping_IsAbsentFromOwnedFilesAndEveryOriginalAndStagingMediaFrame()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "synthetic-media-tests", Guid.NewGuid().ToString("N")));
        var files = new OwnedEvidenceFileStore(Path.Combine(root, "owned"));
        await using var probe = await ChromiumFixtureBrowser.LaunchSyntheticHandoffProbeAsync(Path.Combine(root, "media"));
        var control = new ControlledScanSession(probe.Browser, files);
        var canary = probe.Browser.SyntheticCanaryForAudit;
        var before = await probe.Browser.CaptureFixtureProcessIdentityAsync();
        try
        {
            var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var start = await control.ExecuteAsync(ai, 1, ReaderCommand.Tab);
            await control.ReleaseAsync(ai);
            var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
            await control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary);
            Assert.Equal(CapturePhase.Excluded, probe.Browser.CaptureStatus.Phase);
            Assert.Equal(canary, await probe.Browser.DiagnosticPage.Locator("input").InputValueAsync());
            Assert.Equal(canary, await probe.Browser.DiagnosticPage.Locator("h1").InnerTextAsync());
            Assert.True(await probe.ObserveInMemoryPositiveControlAsync());
            await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(simulation, 1, ReaderCommand.ReadCurrent));
            var clear = await control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.ClearCanary);
            Assert.True(clear.Completion.Input.CanaryCleared); await control.ReleaseAsync(simulation);
            Assert.False(probe.Browser.SyntheticStatus.AuthorityActive); Assert.Equal(0, probe.Browser.SyntheticStatus.ActiveOperations);
            var resumed = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var finish = await control.ExecuteAsync(resumed, 2, ReaderCommand.ReadCurrent);
            Assert.True(finish.Completion.Intent.PriorSimulatedAssistance);
            Assert.Equal(start.Evidence!.Snapshot!.DocumentId, finish.Evidence!.Snapshot!.DocumentId);
            Assert.Equal(before, await probe.Browser.CaptureFixtureProcessIdentityAsync());
        }
        finally { await control.DisposeAsync(); }
        var audit = await probe.InspectClosedEvidenceAsync(root);
        Assert.True(audit.Verified, audit.ErrorCode); Assert.True(audit.PositiveControlDetected); Assert.True(audit.CanaryTextAbsent);
        Assert.Equal(2, audit.Segments.Length); Assert.All(audit.Segments, segment => Assert.True(segment.Frames.Length >= 3));
        Assert.Contains(audit.Artifacts, artifact => artifact.File == "segment-1.webm");
        Assert.Contains(audit.Artifacts, artifact => artifact.File == "segment-2.webm");
        Assert.Contains(audit.Artifacts, artifact => artifact.File.StartsWith("staging/", StringComparison.Ordinal));
        foreach (var artifact in audit.Artifacts)
        {
            Assert.Equal(0, artifact.Pixels.CanaryPixels); Assert.True(artifact.Pixels.Frames > 0);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "media", artifact.File));
            Assert.Equal(bytes.Length, artifact.Bytes); Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), artifact.Sha256);
        }
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.False(bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(canary)) >= 0);
        }
        Assert.Empty(Directory.EnumerateFiles(root, "*.pending", SearchOption.AllDirectories));
        Assert.True(control.Status.Owned.SimulatedAssistanceIntroduced);
    }

    [Fact]
    public async Task MissingActualCanaryControl_CannotTurnEmptySafeLookingSegmentsIntoAcceptance()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "synthetic-media-tests", Guid.NewGuid().ToString("N")));
        var store = new OwnedEvidenceFileStore(Path.Combine(root, "owned"));
        await using var probe = await ChromiumFixtureBrowser.LaunchSyntheticHandoffProbeAsync(Path.Combine(root, "media"));
        var control = new ControlledScanSession(probe.Browser, store);
        try
        {
            await Assert.ThrowsAsync<BrowserOperationException>(() => probe.ObserveInMemoryPositiveControlAsync());
            var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            await control.ExecuteAsync(ai, 1, ReaderCommand.Tab); await control.ReleaseAsync(ai);
            var resumed = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            await control.ExecuteAsync(resumed, 2, ReaderCommand.ReadCurrent);
        }
        finally { await control.DisposeAsync(); }
        var audit = await probe.InspectClosedEvidenceAsync(root);
        Assert.False(audit.Verified); Assert.False(audit.PositiveControlDetected);
        Assert.Equal("SYNTHETIC_CANARY_LEAK_OR_MISSING_CONTROL", audit.ErrorCode);
        Assert.Equal(2, audit.Segments.Length); Assert.True(audit.CanaryTextAbsent);
        Assert.False(control.Status.Owned.SimulatedAssistanceIntroduced);
    }
}
