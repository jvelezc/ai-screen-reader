using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class OwnedScanSessionTests
{
    [Fact]
    public async Task IntentIsAcknowledgedBeforeDispatch_AndCurrentEvidenceHasSeparateInputFacts()
    {
        var browser = new OwnedBrowser();
        var store = new RecordingStore { DelayStage = "intent" };
        await using var owner = new OwnedScanSession(browser, store);
        var command = owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab);
        await store.Started.Task;
        Assert.Equal(0, browser.Captures);
        Assert.Equal(0, browser.Keys);
        Assert.Equal(1, owner.Status.WritesInFlight);
        store.Release.SetResult();
        var result = await command;
        Assert.Equal(OwnedCompletionState.ResultStored, result.Completion.State);
        Assert.Equal(1, result.Completion.InputRequests);
        Assert.Equal(1, result.Completion.CompletedInputCalls);
        Assert.False(result.Completion.InputUncertain);
        Assert.True(result.Completion.InputMayHaveOccurred);
        Assert.Equal(1, result.Evidence!.Snapshot!.CaptureEpoch);
        Assert.NotNull(result.Evidence.CaptureWindow);
        Assert.Null(result.Evidence.Snapshot.DocumentUrl);
        Assert.Equal(new[] { typeof(OwnedCommandIntent), typeof(OwnedCommandEvidence), typeof(OwnedCommandCompletion) },
            store.Records.Select(r => r.GetType()));
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("result")]
    [InlineData("completion")]
    public async Task CancelledWaiter_DoesNotReleasePendingOwnedWriteOrAcknowledgeGap(string stage)
    {
        var browser = new OwnedBrowser();
        var store = new RecordingStore { DelayStage = stage };
        await using var owner = new OwnedScanSession(browser, store);
        using var cancellation = new CancellationTokenSource();
        var command = owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab, cancellationToken: cancellation.Token);
        await store.Started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        var exclusion = owner.ExcludeCaptureAsync();
        Assert.Equal(CapturePhase.Draining, owner.Status.Capture.Phase);
        Assert.False(exclusion.IsCompleted);
        Assert.True(owner.Status.ActiveOperations > 0);
        Assert.True(owner.Status.WritesInFlight > 0);
        store.Release.SetResult();
        var lease = await exclusion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CapturePhase.Excluded, owner.Status.Capture.Phase);
        Assert.Equal(0, owner.Status.ActiveOperations);
        Assert.Equal(0, owner.Status.WritesInFlight);
        var completion = Assert.Single(store.Records.OfType<OwnedCommandCompletion>());
        Assert.Equal(stage == "intent" ? 0 : 1, completion.CompletedInputCalls);
        Assert.DoesNotContain(lease.Token.ToString(), JsonSerializer.Serialize(store.Records));
    }

    [Fact]
    public async Task LateCaptureAndQueuedCommand_AreDrainedWithoutPersistingPayloadOrSendingQueuedInput()
    {
        var browser = new OwnedBrowser { CaptureWork = new() };
        var store = new RecordingStore();
        await using var owner = new OwnedScanSession(browser, store);
        var current = owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent);
        await browser.CaptureStarted.Task;
        var queued = owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab);
        var exclusion = owner.ExcludeCaptureAsync();
        Assert.False(exclusion.IsCompleted);
        browser.Name = "owned-late-private-canary";
        browser.CaptureWork.SetResult(browser.Snapshot());
        await Assert.ThrowsAsync<BrowserOperationException>(() => current);
        await Assert.ThrowsAsync<BrowserOperationException>(() => queued);
        await exclusion;
        Assert.Equal(1, browser.Captures);
        Assert.Equal(0, browser.Keys);
        Assert.Empty(store.Records.OfType<OwnedCommandEvidence>());
        Assert.Single(store.Records.OfType<OwnedCommandIntent>());
        Assert.Single(store.Records.OfType<OwnedCommandCompletion>());
        Assert.DoesNotContain("owned-late-private-canary", JsonSerializer.Serialize(store.Records));
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("result")]
    [InlineData("completion")]
    public async Task StalledWrite_FaultsExclusionAndLateAcknowledgementCannotReopen(string stage)
    {
        var time = new FakeTimeProvider();
        var store = new RecordingStore { DelayStage = stage };
        var owner = new OwnedScanSession(new OwnedBrowser(), store, time);
        var command = owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent);
        await store.Started.Task;
        var exclusion = owner.ExcludeCaptureAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("OWNED_EXCLUSION_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => exclusion)).Code);
        Assert.Equal(CapturePhase.Faulted, owner.Status.Capture.Phase);
        Assert.True(owner.Status.WritesInFlight > 0);
        store.Release.SetResult();
        await Assert.ThrowsAsync<BrowserOperationException>(() => command);
        Assert.Equal(CapturePhase.Faulted, owner.Status.Capture.Phase);
        Assert.Equal(0, owner.Status.WritesInFlight);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab));
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExcludeCaptureAsync());
        await owner.DisposeAsync();
        Assert.Equal("OWNED_EXCLUSION_UNCONFIRMED", owner.Status.FaultCode);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("Permitted")]
    public async Task Resume_KeepsAdmissionClosedUntilBaselineAndPermitWritesAreAcknowledged(string stage)
    {
        var store = new RecordingStore { DelayStage = stage };
        await using var owner = new OwnedScanSession(new OwnedBrowser(), store);
        await owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent);
        var lease = await owner.ExcludeCaptureAsync();
        var resume = owner.ResumeCaptureAsync(lease);
        await store.Started.Task;
        Assert.Equal(CapturePhase.Resuming, owner.Status.Capture.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(2, ReaderCommand.Tab));
        Assert.False(resume.IsCompleted);
        store.Release.SetResult();
        var baseline = await resume;
        Assert.Equal(2, baseline.CaptureEpoch);
        Assert.Equal(CapturePhase.Permitted, owner.Status.Capture.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab));
        var current = await owner.ExecuteAndPersistAsync(2, ReaderCommand.ReadCurrent);
        Assert.Single(current.Evidence!.Events, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
        Assert.Equal(2, Assert.Single(store.Records.OfType<OwnedScanBaseline>()).Epoch);
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("result")]
    [InlineData("completion")]
    public async Task WriteFailures_AreSanitizedAndKeepTheOwnerClosed(string stage)
    {
        var store = new RecordingStore { FailStage = stage };
        var owner = new OwnedScanSession(new OwnedBrowser(), store);
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent));
        Assert.Null(error.InnerException);
        Assert.Equal(CapturePhase.Faulted, owner.Status.Capture.Phase);
        Assert.NotNull(owner.Status.FaultCode);
        Assert.DoesNotContain("private-error-canary", JsonSerializer.Serialize(new { store.Records, owner.Status }));
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExcludeCaptureAsync());
        await owner.DisposeAsync();
    }

    [Theory]
    [InlineData("browser")]
    [InlineData("cancelled")]
    public async Task StoreExceptions_CannotMasqueradeAsReaderExclusionOrCallerCancellation(string kind)
    {
        var store = new RecordingStore { FailStage = "intent", FailureKind = kind };
        await using var owner = new OwnedScanSession(new OwnedBrowser(), store);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent));
        Assert.Equal(CapturePhase.Faulted, owner.Status.Capture.Phase);
        Assert.Equal("OWNED_WRITE_UNCONFIRMED", owner.Status.FaultCode);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExcludeCaptureAsync());
    }

    [Fact]
    public async Task PostInputCancellation_PreservesUncertaintyWhenActualOperationAlreadySettled()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore();
        var browser = new OwnedBrowser { AfterKey = cancellation.Cancel };
        await using var owner = new OwnedScanSession(browser, store);
        var request = owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab, cancellationToken: cancellation.Token);
        var receipt = await request;
        Assert.True(receipt.Evidence!.Step.InputMayHaveOccurred);
        Assert.Equal(VerificationStatus.Interrupted, receipt.Evidence.Step.Verification);
        await owner.ExcludeCaptureAsync();
        var completion = Assert.Single(store.Records.OfType<OwnedCommandCompletion>());
        Assert.Equal(1, completion.InputRequests);
        Assert.Equal(0, completion.CompletedInputCalls);
        Assert.True(completion.InputUncertain);
        Assert.True(completion.InputMayHaveOccurred);
        Assert.Equal(1, browser.Keys);
    }

    [Fact]
    public async Task ForeignStaleAndConsumedLeases_RejectBeforeAdapterResume()
    {
        var firstBrowser = new OwnedBrowser();
        await using var first = new OwnedScanSession(firstBrowser, new RecordingStore());
        await using var second = new OwnedScanSession(new OwnedBrowser(), new RecordingStore());
        var lease = await first.ExcludeCaptureAsync();
        var foreign = await second.ExcludeCaptureAsync();
        foreach (var invalid in new[] { foreign, lease with { Epoch = 2 }, lease with { Token = Guid.Empty } })
            Assert.Equal("OWNED_LEASE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => first.ResumeCaptureAsync(invalid))).Code);
        Assert.Equal(0, firstBrowser.Resumes);
        await first.ResumeCaptureAsync(lease);
        await Assert.ThrowsAsync<BrowserOperationException>(() => first.ResumeCaptureAsync(lease));
        Assert.Equal(1, firstBrowser.Resumes);
    }

    [Fact]
    public async Task DuplicateOwnerStaleEpochAndBudget_CannotDispatchOrPublishExtraIntents()
    {
        var browser = new OwnedBrowser();
        var store = new RecordingStore();
        var owner = new OwnedScanSession(browser, store, maximumCommands: 1);
        Assert.Equal("OWNED_BROWSER_ALREADY_OWNED", Assert.Throws<BrowserOperationException>(
            () => new OwnedScanSession(browser, new RecordingStore())).Code);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(2, ReaderCommand.Tab));
        Assert.Empty(store.Records);
        await owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab));
        Assert.Single(store.Records.OfType<OwnedCommandIntent>());
        Assert.Equal(0, browser.Keys);
        await owner.DisposeAsync();
        Assert.Equal(CapturePhase.Closed, owner.Status.Capture.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExcludeCaptureAsync());
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task ExcludedJournalAcknowledgement_IsRequiredBeforeLeaseAndWaitCancellationDoesNotImplyIt()
    {
        var store = new RecordingStore { DelayStage = "Excluded" };
        await using var owner = new OwnedScanSession(new OwnedBrowser(), store);
        var exclusion = owner.ExcludeCaptureAsync();
        await store.Started.Task;
        using var cancellation = new CancellationTokenSource();
        var waiter = exclusion.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.Equal(CapturePhase.Draining, owner.Status.Capture.Phase);
        Assert.Equal(1, owner.Status.WritesInFlight);
        Assert.False(exclusion.IsCompleted);
        store.Release.SetResult();
        await exclusion;
        Assert.Equal(CapturePhase.Excluded, owner.Status.Capture.Phase);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("Permitted")]
    public async Task FailedResumeWrites_KeepTheOwnerClosedAndDoNotGrantNewEpochInput(string stage)
    {
        var browser = new OwnedBrowser();
        var store = new RecordingStore { FailStage = stage };
        await using var owner = new OwnedScanSession(browser, store);
        var lease = await owner.ExcludeCaptureAsync();
        Assert.Equal("OWNED_RESUME_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => owner.ResumeCaptureAsync(lease))).Code);
        Assert.Equal(CapturePhase.Faulted, owner.Status.Capture.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(2, ReaderCommand.Tab));
        Assert.Equal(0, browser.Keys);
        Assert.DoesNotContain("private-error-canary", JsonSerializer.Serialize(store.Records));
    }

    [Fact]
    public async Task TimedOutResumeWriter_RemainsTrackedThroughCloseUntilActualAcknowledgement()
    {
        var time = new FakeTimeProvider();
        var store = new RecordingStore { DelayStage = "baseline" };
        var owner = new OwnedScanSession(new OwnedBrowser(), store, time);
        var lease = await owner.ExcludeCaptureAsync();
        var resume = owner.ResumeCaptureAsync(lease);
        await store.Started.Task;
        time.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<BrowserOperationException>(() => resume);
        Assert.Equal(CapturePhase.Faulted, owner.Status.Capture.Phase);
        Assert.Equal(1, owner.Status.WritesInFlight);
        var close = owner.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        store.Release.SetResult();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CapturePhase.Closed, owner.Status.Capture.Phase);
        Assert.Equal(0, owner.Status.WritesInFlight);
        Assert.Equal("OWNED_RESUME_UNCONFIRMED", owner.Status.FaultCode);
    }

    [Fact]
    public async Task ActualCaptureWindow_UsesOwnerClockAndCallerArgumentsNeverEnterEvidence()
    {
        var time = new FakeTimeProvider();
        var browser = new OwnedBrowser { CaptureWork = new() };
        var store = new RecordingStore();
        await using var owner = new OwnedScanSession(browser, store, time);
        var operation = owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent);
        time.Advance(TimeSpan.FromMilliseconds(17));
        browser.CaptureWork.SetResult(browser.Snapshot());
        var receipt = await operation;
        Assert.Equal(new EvidenceInterval(0, 17), receipt.Evidence!.CaptureWindow);
        Assert.Equal(new EvidenceInterval(0, 0), receipt.Evidence.Clock.ReaderOrigin);
        await owner.ExecuteAndPersistAsync(1, ReaderCommand.PressKey, "caller-private-token");
        Assert.DoesNotContain("caller-private-token", JsonSerializer.Serialize(store.Records));
        Assert.DoesNotContain("private-query", JsonSerializer.Serialize(store.Records));
        Assert.Equal(0, browser.Keys);
    }

    [Fact]
    public async Task EpochLimit_BoundsTransitionsWithoutFabricatingAnExclusionLease()
    {
        await using var owner = new OwnedScanSession(new OwnedBrowser(), new RecordingStore());
        for (var i = 1; i < 64; i++) await owner.ResumeCaptureAsync(await owner.ExcludeCaptureAsync());
        Assert.Equal(64, owner.Status.Capture.Epoch);
        Assert.Equal("OWNED_EPOCH_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExcludeCaptureAsync())).Code);
        Assert.Equal(CapturePhase.Permitted, owner.Status.Capture.Phase);
        Assert.True(owner.Status.Capture.Events.Length <= 256);
        await owner.ExecuteAndPersistAsync(64, ReaderCommand.ReadCurrent);
    }

    [Fact]
    public async Task AdapterReturningAStaleEpoch_CannotPersistRawEvidenceOrSendInput()
    {
        var browser = new OwnedBrowser { StaleCapture = true };
        var store = new RecordingStore();
        await using var owner = new OwnedScanSession(browser, store);
        await Assert.ThrowsAsync<BrowserOperationException>(() => owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab));
        Assert.Equal("OWNED_CAPTURE_EPOCH_MISMATCH", owner.Status.FaultCode);
        Assert.Empty(store.Records.OfType<OwnedCommandEvidence>());
        Assert.Equal(0, browser.Keys);
    }

    internal sealed class RecordingStore : IOwnedEvidenceStore, IOwnedAssistanceStore
    {
        private readonly object _sync = new();
        private readonly List<object> _records = [];
        private bool _delayed;
        public string ScanId { get; } = Guid.NewGuid().ToString("N");
        public string? DelayStage { get; init; }
        public string? FailStage { get; init; }
        public string FailureKind { get; init; } = "io";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object[] Records { get { lock (_sync) return [.. _records]; } }
        public Task<OwnedWriteReceipt> WriteIntentAsync(OwnedCommandIntent intent) => Save("intent", intent);
        public Task<OwnedWriteReceipt> WriteResultAsync(OwnedCommandEvidence evidence) => Save("result", evidence);
        public Task<OwnedWriteReceipt> WriteCompletionAsync(OwnedCommandCompletion completion) => Save("completion", completion);
        public Task<OwnedWriteReceipt> WriteBaselineAsync(OwnedScanBaseline baseline) => Save("baseline", baseline);
        public Task<OwnedWriteReceipt> WriteLifecycleAsync(OwnedScanLifecycle lifecycle) => Save(lifecycle.Event.Phase.ToString(), lifecycle);
        public Task<OwnedWriteReceipt> WriteAssistanceIntentAsync(OwnedAssistanceIntent intent) => Save("assist-intent", intent);
        public Task<OwnedWriteReceipt> WriteAssistanceCompletionAsync(OwnedAssistanceCompletion completion) => Save("assist-completion", completion);
        private async Task<OwnedWriteReceipt> Save(string stage, object value)
        {
            if (stage == DelayStage && !_delayed) { _delayed = true; Started.TrySetResult(); await Release.Task; }
            if (stage == FailStage)
                throw FailureKind switch
                {
                    "browser" => new BrowserOperationException("private-error-canary"),
                    "cancelled" => new OperationCanceledException("private-error-canary"),
                    _ => new IOException("private-error-canary")
                };
            lock (_sync) _records.Add(value);
            return new(stage, 1, new string('a', 64));
        }
    }

    internal sealed class OwnedBrowser : IScreenReaderBrowser, ICaptureControl, IAsyncDisposable
    {
        public string Name { get; set; } = "Safe control";
        public bool StaleCapture { get; init; }
        public int Captures { get; private set; }
        public int Keys { get; private set; }
        public int Resumes { get; private set; }
        public Action? AfterKey { get; init; }
        public TaskCompletionSource<AccessibilitySnapshot>? CaptureWork { get; init; }
        public TaskCompletionSource CaptureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CaptureStatus CaptureStatus { get; private set; } = new(CapturePhase.Permitted, 1, [], false);
        public AccessibilitySnapshot Snapshot() => new("owned-document",
            [new("button", "button", Name, IsFocused: Keys > 0, DocumentId: "owned-document", FrameId: "main")], [])
        { CaptureEpoch = StaleCapture ? 0 : CaptureStatus.Epoch, DocumentUrl = "https://fixture.invalid/?private-query" };
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Captures++; CaptureStarted.TrySetResult(); cancellationToken.ThrowIfCancellationRequested();
            return CaptureWork?.Task ?? Task.FromResult(Snapshot());
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (CaptureStatus.Phase != CapturePhase.Permitted) throw new BrowserInputDeniedException("CAPTURE_EXCLUDED");
            Keys++; AfterKey?.Invoke(); cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask;
        }
        public Task<CaptureExclusionLease> ExcludeCaptureAsync()
        {
            CaptureStatus = CaptureStatus with { Phase = CapturePhase.Excluded };
            return Task.FromResult(new CaptureExclusionLease(Guid.NewGuid(), CaptureStatus.Epoch));
        }
        public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease)
        {
            Resumes++; CaptureStatus = CaptureStatus with { Phase = CapturePhase.Permitted, Epoch = CaptureStatus.Epoch + 1 };
            return Task.FromResult(Snapshot());
        }
        public ValueTask DisposeAsync() { CaptureStatus = CaptureStatus with { Phase = CapturePhase.Closed }; return ValueTask.CompletedTask; }
    }
}
