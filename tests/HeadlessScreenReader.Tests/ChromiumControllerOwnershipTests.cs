using System.Security.Cryptography;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumControllerOwnershipTests
{
    [Fact]
    public async Task SequentialControllers_UseNativeFocusAndFreshEpochWithoutMutatingHistoricalFiles()
    {
        var store = Store();
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var control = new ControlledScanSession(browser, store);
        try
        {
            var first = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var initial = await control.ExecuteAsync(first, 1, ReaderCommand.ReadCurrent);
            var tab = await control.ExecuteAsync(first, 1, ReaderCommand.Tab);
            Assert.Equal("Open checkout preview", tab.Evidence!.Snapshot!.FocusedNode!.Name);
            Assert.Equal(1, tab.Completion.CompletedInputCalls);
            var path = Path.Combine(store.Folder, tab.ResultWrite!.ArtifactId);
            var original = SHA256.HashData(await File.ReadAllBytesAsync(path));
            await control.ReleaseAsync(first);
            Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
            var second = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            Assert.Equal(2, second.Generation); Assert.Equal(2, control.Status.Owned.Capture.Epoch);
            await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(first, 2, ReaderCommand.Activate));
            var activate = await control.ExecuteAsync(second, 2, ReaderCommand.Activate,
                expectation: new("heading", "Checkout preview", MustBeFocused: true));
            Assert.Equal(VerificationStatus.Satisfied, activate.Evidence!.Step.Verification);
            Assert.Equal(1, activate.Completion.CompletedInputCalls);
            Assert.Equal(2, activate.Completion.Intent.ControllerGeneration);
            Assert.Equal(initial.Evidence!.Snapshot!.DocumentId, activate.Evidence.Snapshot!.DocumentId);
            Assert.Contains(activate.Evidence.Events, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
            Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(path)));
            var content = await Contents(store);
            Assert.DoesNotContain(first.Token.ToString(), content); Assert.DoesNotContain(second.Token.ToString(), content);
        }
        finally { await control.DisposeAsync(); }
        Assert.Equal(ControllerPhase.Closed, control.Status.Phase);
        Assert.Equal(CapturePhase.Closed, browser.CaptureStatus.Phase);
        Assert.Empty(Directory.EnumerateFiles(store.Folder, "*.pending"));
    }

    [Fact]
    public async Task ListboxCharacters_AreBrokeredAndStaleLeaseCannotSendAnotherPrefix()
    {
        var store = Store();
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Typeahead);
        await using var control = new ControlledScanSession(browser, store);
        var first = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await control.ExecuteAsync(first, 1, ReaderCommand.Tab);
        var search = await control.ExecuteAsync(first, 1, ReaderCommand.Typeahead, "pe",
            expectation: new("option", "Peaches", MustBeFocused: true, Selected: false));
        Assert.Equal(VerificationStatus.Satisfied, search.Evidence!.Step.Verification);
        Assert.Equal(2, search.Completion.InputRequests); Assert.Equal(2, search.Completion.CompletedInputCalls);
        Assert.Equal("Peaches", search.Evidence.Snapshot!.EffectiveFocusedNode!.Name);
        await control.ReleaseAsync(first);
        var second = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var count = Directory.EnumerateFiles(store.Folder).Count();
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(first, 2, ReaderCommand.Typeahead, "pe"));
        Assert.Equal(count, Directory.EnumerateFiles(store.Folder).Count());
        var read = await control.ExecuteAsync(second, 2, ReaderCommand.ReadCurrent);
        Assert.Equal("Peaches", read.Evidence!.Snapshot!.EffectiveFocusedNode!.Name);
        Assert.Equal(0, read.Completion.InputRequests);
    }

    [Fact]
    public async Task RealIntentDelay_CancelledWaitCannotReleaseAuthorityOrSendOldInput()
    {
        var files = Store(); var store = new DelayedIntentStore(files);
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var control = new ControlledScanSession(browser, store);
        try
        {
            var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            using var cancellation = new CancellationTokenSource();
            var command = control.ExecuteAsync(lease, 1, ReaderCommand.Tab, cancellationToken: cancellation.Token);
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
            var release = control.ReleaseAsync(lease);
            Assert.False(release.IsCompleted); Assert.Equal(ControllerPhase.Revoking, control.Status.Phase);
            await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
            store.Release.SetResult(); await release.WaitAsync(TimeSpan.FromSeconds(5));
            var next = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var read = await control.ExecuteAsync(next, 2, ReaderCommand.ReadCurrent);
            Assert.Equal("RootWebArea", read.Evidence!.Snapshot!.FocusedNode!.Role);
            Assert.Equal(0, read.Completion.InputRequests);
            var tab = await control.ExecuteAsync(next, 2, ReaderCommand.Tab);
            Assert.Equal(1, tab.Completion.CompletedInputCalls);
            Assert.Equal("Open checkout preview", tab.Evidence!.Snapshot!.FocusedNode!.Name);
        }
        finally { await control.DisposeAsync(); }
        Assert.Equal(0, control.Status.ActiveCommands); Assert.Equal(0, control.Status.Owned.WritesInFlight);
        Assert.Empty(Directory.EnumerateFiles(files.Folder, "*.pending"));
    }

    private static OwnedEvidenceFileStore Store() => new(Path.Combine("artifacts", "controller-tests"));
    private static async Task<string> Contents(OwnedEvidenceFileStore store)
    {
        var contents = new List<string>();
        foreach (var file in Directory.EnumerateFiles(store.Folder)) contents.Add(await File.ReadAllTextAsync(file));
        return string.Join("\n", contents);
    }
    private sealed class DelayedIntentStore(OwnedEvidenceFileStore files) : IOwnedEvidenceStore
    {
        private bool _delayed;
        public string ScanId => files.ScanId;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<OwnedWriteReceipt> WriteIntentAsync(OwnedCommandIntent intent)
        {
            if (!_delayed) { _delayed = true; Started.SetResult(); await Release.Task; }
            return await files.WriteIntentAsync(intent);
        }
        public Task<OwnedWriteReceipt> WriteResultAsync(OwnedCommandEvidence evidence) => files.WriteResultAsync(evidence);
        public Task<OwnedWriteReceipt> WriteCompletionAsync(OwnedCommandCompletion completion) => files.WriteCompletionAsync(completion);
        public Task<OwnedWriteReceipt> WriteBaselineAsync(OwnedScanBaseline baseline) => files.WriteBaselineAsync(baseline);
        public Task<OwnedWriteReceipt> WriteLifecycleAsync(OwnedScanLifecycle lifecycle) => files.WriteLifecycleAsync(lifecycle);
    }
}
