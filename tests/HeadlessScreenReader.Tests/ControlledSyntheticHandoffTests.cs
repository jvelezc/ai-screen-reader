using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class ControlledSyntheticHandoffTests
{
    [Fact]
    public async Task Simulation_RequiresAcknowledgedGapAndMarksLaterAutomationWithoutHumanAcceptance()
    {
        var browser = new SyntheticBrowser(); var store = new RecordingStore();
        await using var control = new ControlledScanSession(browser, store);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1)));
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await control.ExecuteAsync(ai, 1, ReaderCommand.Tab);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1)));
        await control.ReleaseAsync(ai);
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(2, simulation.Generation); Assert.Equal(ControllerRole.SimulatedAssistance, simulation.Role);
        Assert.Equal(CapturePhase.Excluded, control.Status.Owned.Capture.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(simulation, 1, ReaderCommand.ReadCurrent));
        var typed = await control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary);
        Assert.Equal(1, typed.Completion.Input.CompletedInputCalls); Assert.False(typed.Completion.HumanAcceptancePerformed);
        Assert.Equal(ControllerRole.SimulatedAssistance, typed.Completion.Intent.Origin);
        var cleared = await control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.ClearCanary);
        Assert.Equal(2, cleared.Completion.Input.CompletedInputCalls); Assert.True(cleared.Completion.Input.CanaryCleared);
        await control.ReleaseAsync(simulation);
        var next = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var after = await control.ExecuteAsync(next, 2, ReaderCommand.ReadCurrent);
        Assert.Equal(3, next.Generation); Assert.Equal(ControllerRole.Automation, next.Role);
        Assert.True(after.Completion.Intent.PriorSimulatedAssistance);
        Assert.True(control.Status.Owned.SimulatedAssistanceIntroduced);
        Assert.Contains(control.Status.Events, e => e.Role == ControllerRole.SimulatedAssistance && e.Code == "CONTROL_SIMULATION_GRANTED");
        var json = JsonSerializer.Serialize(store.Records);
        foreach (var token in browser.Tokens.Append(simulation.Token).Append(ai.Token).Append(next.Token)) Assert.DoesNotContain(token.ToString(), json);
    }

    [Fact]
    public async Task UnclearedCanary_VetoesAutomationWithoutResumingAndAllowsAnExplicitNewClearLease()
    {
        var browser = new SyntheticBrowser();
        await using var control = new ControlledScanSession(browser, new RecordingStore());
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        await control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary);
        await control.ReleaseAsync(simulation);
        Assert.Equal("CONTROL_SYNTHETIC_CLEAR_REQUIRED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => control.AcquireAsync(TimeSpan.FromMinutes(1)))).Code);
        Assert.Equal(0, browser.Resumes); Assert.Equal(ControllerPhase.Unowned, control.Status.Phase);
        var clearer = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        await control.ExecuteSimulatedAssistanceAsync(clearer, SyntheticInputAction.ClearCanary); await control.ReleaseAsync(clearer);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(4, ai.Generation); Assert.Equal(1, browser.Resumes); Assert.Equal(2, control.Status.Owned.Capture.Epoch);
    }

    [Theory]
    [InlineData("assist-intent")]
    [InlineData("assist-completion")]
    public async Task CancelledWait_CannotReleasePendingSimulationMetadataOrGrantAnotherController(string stage)
    {
        var browser = new SyntheticBrowser(); var store = new RecordingStore { DelayStage = stage };
        await using var control = new ControlledScanSession(browser, store);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        using var wait = new CancellationTokenSource();
        var command = control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary, wait.Token);
        await store.Started.Task; wait.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        var release = control.ReleaseAsync(simulation);
        Assert.False(release.IsCompleted); Assert.Equal(ControllerPhase.Revoking, control.Status.Phase);
        Assert.Equal(1, control.Status.ActiveCommands); Assert.Equal(1, control.Status.Owned.WritesInFlight);
        Assert.Equal(stage == "assist-intent" ? 0 : 1, browser.Calls);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
        store.Release.SetResult(); await release.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ControllerPhase.Unowned, control.Status.Phase); Assert.Equal(0, control.Status.Owned.WritesInFlight);
        var completion = Assert.Single(store.Records.OfType<OwnedAssistanceCompletion>());
        Assert.Equal(stage == "assist-intent" ? 0 : 1, completion.Input.CompletedInputCalls);
        Assert.Equal(stage == "assist-completion", browser.Canary);
    }

    [Fact]
    public async Task Expiry_DrainsActualSyntheticCallBeforeReplacingAuthority()
    {
        var time = new FakeTimeProvider(); var browser = new SyntheticBrowser { Work = new() };
        await using var control = new ControlledScanSession(browser, new RecordingStore(), time);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromSeconds(1));
        using var wait = new CancellationTokenSource();
        var command = control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary, wait.Token);
        await browser.Started.Task; wait.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ControllerPhase.Revoking, control.Status.Phase); Assert.Equal(1, browser.SyntheticStatus.ActiveOperations);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1)));
        browser.Work.SetResult(); await Until(() => control.Status.Phase == ControllerPhase.Unowned);
        Assert.Equal(0, browser.SyntheticStatus.ActiveOperations); Assert.Equal(1, browser.Calls);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData("assist-intent")]
    [InlineData("assist-completion")]
    public async Task SimulationWriteFailure_FaultsClosedStopsAuthorityAndRetainsOnlySanitizedMetadata(string stage)
    {
        var store = new RecordingStore { FailStage = stage, FailureKind = "cancelled" }; var browser = new SyntheticBrowser();
        await using var control = new ControlledScanSession(browser, store);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var lease = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteSimulatedAssistanceAsync(lease, SyntheticInputAction.EnterCanary));
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        Assert.False(browser.SyntheticStatus.AuthorityActive);
        Assert.Equal(stage == "assist-intent" ? 0 : 1, browser.Calls);
        Assert.DoesNotContain("private-error-canary", JsonSerializer.Serialize(store.Records));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData("code")]
    [InlineData("action")]
    [InlineData("negative")]
    [InlineData("too-many")]
    [InlineData("completed")]
    [InlineData("null")]
    [InlineData("no-calls")]
    [InlineData("success-state")]
    public async Task InvalidNativeResult_FaultsAndCannotPersistProviderPayloadOrClaimHumanAcceptance(string caseId)
    {
        var invalid = caseId switch
        {
            "action" => new SyntheticInputResult(SyntheticInputAction.ClearCanary, 1, 1, false, false, null),
            "negative" => new(SyntheticInputAction.EnterCanary, -1, 0, false, false, null),
            "too-many" => new(SyntheticInputAction.EnterCanary, 2, 1, false, false, null),
            "completed" => new(SyntheticInputAction.EnterCanary, 1, 2, false, false, null),
            "no-calls" => new(SyntheticInputAction.EnterCanary, 0, 0, false, false, null),
            "success-state" => new(SyntheticInputAction.EnterCanary, 1, 1, false, true, null),
            _ => new(SyntheticInputAction.EnterCanary, 1, 1, false, false, "private-result-canary")
        };
        var browser = new SyntheticBrowser { Override = invalid, ReturnNull = caseId == "null" };
        var store = new RecordingStore();
        await using var control = new ControlledScanSession(browser, store);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var lease = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteSimulatedAssistanceAsync(lease, SyntheticInputAction.EnterCanary));
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        Assert.DoesNotContain("private-result-canary", JsonSerializer.Serialize(store.Records));
        Assert.False(Assert.Single(store.Records.OfType<OwnedAssistanceCompletion>()).HumanAcceptancePerformed);
    }

    [Fact]
    public async Task ForgedRoleAndStaleLease_RejectBeforePublishingSimulationIntent()
    {
        var store = new RecordingStore();
        await using var control = new ControlledScanSession(new SyntheticBrowser(), store);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteSimulatedAssistanceAsync(ai, SyntheticInputAction.EnterCanary));
        await control.ReleaseAsync(ai);
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteSimulatedAssistanceAsync(
            simulation with { Role = ControllerRole.Automation }, SyntheticInputAction.EnterCanary));
        await control.ReleaseAsync(simulation);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary));
        Assert.Empty(store.Records.OfType<OwnedAssistanceIntent>());
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("store")]
    [InlineData("adapter")]
    public async Task MissingCapability_CannotGrantSimulationOrOpenCapture(string capability)
    {
        var records = new RecordingStore();
        IOwnedEvidenceStore store = capability == "store" ? new ReaderOnlyStore(records) : records;
        IScreenReaderBrowser browser = capability == "adapter" ? new OwnedBrowser() : new SyntheticBrowser { Supported = capability != "scope" };
        await using var control = new ControlledScanSession(browser, store);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1)));
        Assert.Equal(ControllerPhase.Unowned, control.Status.Phase); Assert.Equal(CapturePhase.Excluded, control.Status.Owned.Capture.Phase);
        Assert.Empty(records.Records.OfType<OwnedAssistanceIntent>());
    }

    [Theory]
    [InlineData("assist-intent")]
    [InlineData("assist-completion")]
    public async Task UnconfirmedMetadataDrain_StaysFaultedAfterLateAcknowledgement(string stage)
    {
        var time = new FakeTimeProvider(); var store = new RecordingStore { DelayStage = stage }; var browser = new SyntheticBrowser();
        var control = new ControlledScanSession(browser, store, time);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        var command = control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary); await store.Started.Task;
        var release = control.ReleaseAsync(simulation); time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("CONTROL_DRAIN_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => release)).Code);
        Assert.False(browser.SyntheticStatus.AuthorityActive); Assert.Equal(1, control.Status.Owned.WritesInFlight);
        store.Release.SetResult(); var result = await command;
        Assert.Equal(stage == "assist-intent" ? 0 : 1, result.Completion.Input.CompletedInputCalls);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
        await control.DisposeAsync();
    }

    [Fact]
    public async Task UnconfirmedActualInputDrain_CannotReopenAfterNativeCompletion()
    {
        var time = new FakeTimeProvider(); var browser = new SyntheticBrowser { Work = new() };
        var control = new ControlledScanSession(browser, new RecordingStore(), time);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        var command = control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary); await browser.Started.Task;
        var release = control.ReleaseAsync(simulation); time.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<BrowserOperationException>(() => release);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase); Assert.Equal(1, browser.SyntheticStatus.ActiveOperations);
        browser.Work.SetResult(); Assert.Equal(1, (await command).Completion.Input.CompletedInputCalls);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1)));
        await control.DisposeAsync();
    }

    [Fact]
    public async Task SimulationWaitTimeout_RetainsActualIntentWriteAndSendsNoLateInput()
    {
        var time = new FakeTimeProvider(); var store = new RecordingStore { DelayStage = "assist-intent" }; var browser = new SyntheticBrowser();
        await using var control = new ControlledScanSession(browser, store, time);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(2));
        var command = control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary); await store.Started.Task;
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("CONTROL_ASSISTANCE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => command)).Code);
        Assert.Equal(1, control.Status.ActiveCommands); Assert.Equal(1, control.Status.Owned.WritesInFlight);
        var release = control.ReleaseAsync(simulation); Assert.False(release.IsCompleted);
        store.Release.SetResult(); await release;
        Assert.Equal(0, browser.Calls);
        Assert.Equal("SYNTHETIC_INPUT_CANCELLED", Assert.Single(store.Records.OfType<OwnedAssistanceCompletion>()).Input.ErrorCode);
    }

    [Theory]
    [InlineData("assist-intent")]
    [InlineData("assist-completion")]
    public async Task CloseDuringSimulationWrite_RevokesAuthorityAndWaitsForActualAcknowledgement(string stage)
    {
        var store = new RecordingStore { DelayStage = stage }; var browser = new SyntheticBrowser();
        var control = new ControlledScanSession(browser, store);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        var command = control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary); await store.Started.Task;
        var close = control.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
        Assert.False(browser.SyntheticStatus.AuthorityActive);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.ClearCanary));
        store.Release.SetResult(); await command; await close;
        Assert.Equal(stage == "assist-intent" ? 0 : 1, browser.Calls);
        Assert.Equal(ControllerPhase.Closed, control.Status.Phase); Assert.Equal(0, control.Status.Owned.WritesInFlight);
    }

    [Fact]
    public async Task SimulationAndReaderCommands_ShareTheOwnedCommandBudget()
    {
        var store = new RecordingStore(); var browser = new SyntheticBrowser();
        await using var control = new ControlledScanSession(browser, store, maximumCommands: 1);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1)); await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        await control.ReleaseAsync(ai); var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal("OWNED_COMMAND_BUDGET_EXHAUSTED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary))).Code);
        Assert.Equal(0, browser.Calls); Assert.Empty(store.Records.OfType<OwnedAssistanceIntent>());
        Assert.False(control.Status.Owned.SimulatedAssistanceIntroduced);
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(1, timeout.Token);
    }

    internal sealed class SyntheticBrowser : IScreenReaderBrowser, ICaptureControl, ISyntheticExcludedInput, IAsyncDisposable
    {
        private readonly OwnedBrowser _inner = new();
        private CaptureExclusionLease? _gap;
        private SyntheticInputLease? _lease, _stopped;
        private Task? _stop;
        private int _active;
        private long _generation;
        private TaskCompletionSource _drained = Completed();
        public bool Canary { get; private set; }
        public bool Supported { get; init; } = true;
        public int Calls { get; private set; }
        public int Resumes => _inner.Resumes;
        public List<Guid> Tokens { get; } = [];
        public TaskCompletionSource? Work { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SyntheticInputResult? Override { get; init; }
        public bool ReturnNull { get; init; }
        public CaptureStatus CaptureStatus => _inner.CaptureStatus;
        public SyntheticInputStatus SyntheticStatus => new(Supported, Canary, _lease is not null, _active);
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => _inner.CaptureAsync(cancellationToken);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => _inner.PressKeyAsync(key, cancellationToken);
        public async Task<CaptureExclusionLease> ExcludeCaptureAsync() => _gap = await _inner.ExcludeCaptureAsync();
        public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease)
        {
            if (Canary || _lease is not null || _active != 0) throw new BrowserOperationException("SYNTHETIC_CLEAR_REQUIRED");
            return _inner.ResumeCaptureAsync(lease);
        }
        public SyntheticInputLease GrantSyntheticInput(CaptureExclusionLease captureLease, long generation, TimeSpan? lifetime = null)
        {
            if (captureLease != _gap || CaptureStatus.Phase != CapturePhase.Excluded || _lease is not null || _active != 0 || generation <= _generation)
                throw new BrowserOperationException("SYNTHETIC_GRANT_DENIED");
            _generation = generation; _stopped = null; _stop = null;
            _lease = new(Guid.NewGuid(), captureLease.Epoch, generation); Tokens.Add(_lease.Token); return _lease;
        }
        public async Task<SyntheticInputResult> ExecuteSyntheticInputAsync(SyntheticInputLease lease, SyntheticInputAction action, CancellationToken cancellationToken = default)
        {
            if (lease != _lease) throw new BrowserInputDeniedException("SYNTHETIC_INPUT_FENCED");
            cancellationToken.ThrowIfCancellationRequested();
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Started.TrySetResult();
            try
            {
                if (Work is not null) await Work.Task;
                var count = action == SyntheticInputAction.EnterCanary ? 1 : 2;
                Calls += count; Canary = action == SyntheticInputAction.EnterCanary;
                return ReturnNull ? null! : Override ?? new(action, count, count, false, !Canary, null);
            }
            finally { if (--_active == 0) _drained.TrySetResult(); }
        }
        public Task RevokeSyntheticInputAsync(SyntheticInputLease lease)
        {
            if (lease == _stopped && _stop is not null) return _stop;
            if (lease != _lease) throw new BrowserInputDeniedException("SYNTHETIC_INPUT_FENCED");
            _stopped = lease; _lease = null; return _stop = _drained.Task;
        }
        public ValueTask DisposeAsync() { _lease = null; return _inner.DisposeAsync(); }
        private static TaskCompletionSource Completed()
        { var value = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); value.SetResult(); return value; }
    }
    private sealed class ReaderOnlyStore(RecordingStore store) : IOwnedEvidenceStore
    {
        public string ScanId => store.ScanId;
        public Task<OwnedWriteReceipt> WriteIntentAsync(OwnedCommandIntent intent) => store.WriteIntentAsync(intent);
        public Task<OwnedWriteReceipt> WriteResultAsync(OwnedCommandEvidence evidence) => store.WriteResultAsync(evidence);
        public Task<OwnedWriteReceipt> WriteCompletionAsync(OwnedCommandCompletion completion) => store.WriteCompletionAsync(completion);
        public Task<OwnedWriteReceipt> WriteBaselineAsync(OwnedScanBaseline baseline) => store.WriteBaselineAsync(baseline);
        public Task<OwnedWriteReceipt> WriteLifecycleAsync(OwnedScanLifecycle lifecycle) => store.WriteLifecycleAsync(lifecycle);
    }
}
