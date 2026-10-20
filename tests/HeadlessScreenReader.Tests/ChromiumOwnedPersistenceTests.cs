using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumOwnedPersistenceTests
{
    [Fact]
    public async Task AcknowledgedGap_ExcludesVisibleAndPasswordCanariesFromRealOwnedFilesAndResumesSameContext()
    {
        var store = Store();
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var owner = new OwnedScanSession(browser, store);
        try
        {
            var initial = await owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent);
            await owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab);
            await browser.DiagnosticPage.EvaluateAsync("() => window.ownedContextMarker='same-owned-context'");
            var lease = await owner.ExcludeCaptureAsync();
            Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
            Assert.Equal(0, owner.Status.ActiveOperations);
            Assert.Equal(0, owner.Status.WritesInFlight);
            var files = Directory.EnumerateFiles(store.Folder).Count();
            // Independent in-memory positive control. These values are never passed to the evidence writer.
            await browser.DiagnosticPage.EvaluateAsync("""
                () => {
                  const h=document.createElement('h2');h.id='owned-private';h.textContent='owned-excluded-visible-canary';document.body.prepend(h);
                  const p=document.createElement('input');p.id='owned-password';p.type='password';p.value='owned-excluded-password-canary';document.body.append(p);p.focus();
                }
                """);
            Assert.True(await browser.DiagnosticPage.Locator("#owned-private").IsVisibleAsync());
            Assert.Equal("owned-excluded-password-canary", await browser.DiagnosticPage.Locator("#owned-password").InputValueAsync());
            foreach (var command in Enum.GetValues<ReaderCommand>())
                await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, command, "pe"));
            Assert.Equal(files, Directory.EnumerateFiles(store.Folder).Count());
            await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressKeyAsync("Tab"));
            Assert.Equal("owned-password", await browser.DiagnosticPage.EvaluateAsync<string>("() => document.activeElement.id"));
            await browser.DiagnosticPage.EvaluateAsync("() => { document.querySelector('#owned-private').remove();document.querySelector('#owned-password').remove(); }");
            var baseline = await owner.ResumeCaptureAsync(lease);
            Assert.Equal(initial.Evidence!.Snapshot!.DocumentId, baseline.DocumentId);
            Assert.Equal(2, baseline.CaptureEpoch);
            Assert.Equal("same-owned-context", await browser.DiagnosticPage.EvaluateAsync<string>("() => window.ownedContextMarker"));
            var current = await owner.ExecuteAndPersistAsync(2, ReaderCommand.ReadCurrent);
            Assert.Single(current.Evidence!.Events, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
            Assert.DoesNotContain(current.Evidence.Events, e => e.Kind is ReaderEventKind.FocusChanged
                or ReaderEventKind.WidgetStateChanged or ReaderEventKind.LiveRegionChanged);
            foreach (var write in new[] { initial.IntentWrite, initial.ResultWrite!, initial.CompletionWrite })
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(store.Folder, write.ArtifactId));
                Assert.Equal(write.Bytes, bytes.LongLength);
                Assert.Equal(write.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            }
            await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab));
            Assert.DoesNotContain(lease.Token.ToString(), await Contents(store));
        }
        finally { await owner.DisposeAsync(); }
        var all = await Contents(store);
        Assert.DoesNotContain("owned-excluded-visible-canary", all);
        Assert.DoesNotContain("owned-excluded-password-canary", all);
        Assert.Empty(Directory.EnumerateFiles(store.Folder, "*.pending"));
        Assert.Equal(CapturePhase.Closed, owner.Status.Capture.Phase);
        Assert.Equal(CapturePhase.Closed, browser.CaptureStatus.Phase);
    }

    [Fact]
    public async Task DelayedRealFileWrite_CannotCrossAcknowledgedGapOrBeReleasedByCancelledWaiter()
    {
        var files = Store();
        var store = new DelayedResultStore(files);
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var owner = new OwnedScanSession(browser, store);
        try
        {
            using var cancellation = new CancellationTokenSource();
            var command = owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent, cancellationToken: cancellation.Token);
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
            var exclusion = owner.ExcludeCaptureAsync();
            Assert.False(exclusion.IsCompleted);
            Assert.Equal(CapturePhase.Draining, owner.Status.Capture.Phase);
            Assert.True(owner.Status.WritesInFlight > 0);
            Assert.Empty(Directory.EnumerateFiles(files.Folder, "*.result.json"));
            store.Release.SetResult();
            var lease = await exclusion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(Directory.EnumerateFiles(files.Folder, "*.result.json"));
            Assert.Equal(0, owner.Status.WritesInFlight);
            Assert.Equal(0, owner.Status.ActiveOperations);
            await browser.DiagnosticPage.EvaluateAsync("""
                () => { const n=document.createElement('h2');n.id='delayed-private';n.textContent='delayed-write-private-canary';document.body.append(n); }
                """);
            Assert.Equal("delayed-write-private-canary", await browser.DiagnosticPage.Locator("#delayed-private").InnerTextAsync());
            await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent));
            Assert.DoesNotContain("delayed-write-private-canary", await Contents(files));
            await browser.DiagnosticPage.EvaluateAsync("() => document.querySelector('#delayed-private').remove()");
            await owner.ResumeCaptureAsync(lease);
            await owner.ExecuteAndPersistAsync(2, ReaderCommand.ReadCurrent);
        }
        finally { await owner.DisposeAsync(); }
        Assert.Empty(Directory.EnumerateFiles(files.Folder, "*.pending"));
        Assert.DoesNotContain("delayed-write-private-canary", await Contents(files));
    }

    [Fact]
    public async Task ClosingDuringExcludedCanary_AddsOnlyMetadataAndClosesTheRealContext()
    {
        var store = Store();
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var owner = new OwnedScanSession(browser, store);
        await owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent);
        await owner.ExcludeCaptureAsync();
        var results = Directory.EnumerateFiles(store.Folder, "*.result.json").Count();
        await browser.DiagnosticPage.EvaluateAsync("""
            () => { const h=document.createElement('h2');h.id='close-private';h.textContent='owned-close-private-canary';document.body.append(h); }
            """);
        Assert.True(await browser.DiagnosticPage.Locator("#close-private").IsVisibleAsync());
        await owner.DisposeAsync();
        Assert.Equal(results, Directory.EnumerateFiles(store.Folder, "*.result.json").Count());
        Assert.DoesNotContain("owned-close-private-canary", await Contents(store));
        Assert.Equal(CapturePhase.Closed, browser.CaptureStatus.Phase);
        Assert.Equal(CapturePhase.Closed, owner.Status.Capture.Phase);
        Assert.Equal(0, owner.Status.WritesInFlight);
    }

    [Fact]
    public async Task ExistingPendingResult_IsPreservedAndFaultsTheRealOwnerWithoutGapAuthority()
    {
        var store = Store();
        var pending = Path.Combine(store.Folder, "command-000001.result.json.pending");
        await File.WriteAllTextAsync(pending, "prior-owned-pending-data");
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var owner = new OwnedScanSession(browser, store);
        try
        {
            await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent));
            Assert.Equal(CapturePhase.Faulted, owner.Status.Capture.Phase);
            Assert.Equal("OWNED_WRITE_UNCONFIRMED", owner.Status.FaultCode);
            await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExcludeCaptureAsync());
            Assert.Equal("prior-owned-pending-data", await File.ReadAllTextAsync(pending));
            Assert.False(File.Exists(Path.Combine(store.Folder, "command-000001.result.json")));
        }
        finally { await owner.DisposeAsync(); }
        Assert.Equal("prior-owned-pending-data", await File.ReadAllTextAsync(pending));
    }

    private static OwnedEvidenceFileStore Store() => new(Path.GetFullPath(Path.Combine("artifacts", "owned-file-tests")));
    private static async Task<string> Contents(OwnedEvidenceFileStore store)
    {
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(store.Folder)) result.Add(await File.ReadAllTextAsync(file));
        return string.Join("\n", result);
    }
    private sealed class DelayedResultStore(OwnedEvidenceFileStore store) : IOwnedEvidenceStore
    {
        private bool _delayed;
        public string ScanId => store.ScanId;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OwnedWriteReceipt> WriteIntentAsync(OwnedCommandIntent intent) => store.WriteIntentAsync(intent);
        public async Task<OwnedWriteReceipt> WriteResultAsync(OwnedCommandEvidence evidence)
        {
            if (!_delayed) { _delayed = true; Started.TrySetResult(); await Release.Task; }
            return await store.WriteResultAsync(evidence);
        }
        public Task<OwnedWriteReceipt> WriteCompletionAsync(OwnedCommandCompletion completion) => store.WriteCompletionAsync(completion);
        public Task<OwnedWriteReceipt> WriteBaselineAsync(OwnedScanBaseline baseline) => store.WriteBaselineAsync(baseline);
        public Task<OwnedWriteReceipt> WriteLifecycleAsync(OwnedScanLifecycle lifecycle) => store.WriteLifecycleAsync(lifecycle);
    }
}
