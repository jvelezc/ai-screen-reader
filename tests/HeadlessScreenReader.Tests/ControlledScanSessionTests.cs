using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class ControlledScanSessionTests
{
    [Fact]
    public async Task ExclusiveLease_RejectsContendersAndPersistsGenerationWithoutBearerToken()
    {
        var store = new RecordingStore(); var browser = new OwnedBrowser();
        await using var control = new ControlledScanSession(browser, store);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(1, lease.Generation);
        Assert.Equal("CONTROL_BUSY", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => control.AcquireAsync(TimeSpan.FromMinutes(1)))).Code);
        var result = await control.ExecuteAsync(lease, 1, ReaderCommand.Tab);
        Assert.Equal(1, result.Completion.CompletedInputCalls);
        Assert.Equal(1, result.Completion.Intent.ControllerGeneration);
        Assert.Equal(1, browser.Keys);
        Assert.DoesNotContain(lease.Token.ToString(), JsonSerializer.Serialize(store.Records));
        Assert.DoesNotContain(lease.Token.ToString(), JsonSerializer.Serialize(control.Status));
        Assert.Equal(0, control.Status.ActiveCommands);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("scan")]
    [InlineData("generation")]
    public async Task ForeignAuthority_IsRejectedBeforeIntentCaptureOrInput(string field)
    {
        var store = new RecordingStore(); var browser = new OwnedBrowser();
        await using var control = new ControlledScanSession(browser, store);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var foreign = field switch
        {
            "token" => lease with { Token = Guid.NewGuid() },
            "scan" => lease with { ScanId = Guid.NewGuid().ToString("N") },
            _ => lease with { Generation = 2 }
        };
        Assert.Equal("CONTROL_LEASE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => control.ExecuteAsync(foreign, 1, ReaderCommand.Tab))).Code);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ReleaseAsync(foreign));
        Assert.Empty(store.Records); Assert.Equal(0, browser.Captures); Assert.Equal(0, browser.Keys);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(120001)]
    public async Task InvalidLeaseLifetime_DoesNotConsumeAGeneration(int milliseconds)
    {
        await using var control = new ControlledScanSession(new OwnedBrowser(), new RecordingStore());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => control.AcquireAsync(TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Equal(ControllerPhase.Unowned, control.Status.Phase);
        Assert.Equal(0, control.Status.Generation);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(120000)]
    public async Task ExactLifetimeBoundaries_ExpireAtDeadlineAndRejectForgedFutureExpiry(int milliseconds)
    {
        var time = new FakeTimeProvider(); var browser = new OwnedBrowser();
        await using var control = new ControlledScanSession(browser, new RecordingStore(), time);
        var lease = await control.AcquireAsync(TimeSpan.FromMilliseconds(milliseconds));
        time.Advance(TimeSpan.FromMilliseconds(milliseconds - 1));
        await control.ExecuteAsync(lease, 1, ReaderCommand.ReadCurrent);
        Assert.Equal(ControllerPhase.Active, control.Status.Phase);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await Until(() => control.Status.Phase == ControllerPhase.Unowned);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(
            lease with { ExpiresAt = time.GetUtcNow().AddYears(1) }, 1, ReaderCommand.Tab));
        Assert.Equal(0, browser.Keys);
        Assert.Equal(CapturePhase.Excluded, control.Status.Owned.Capture.Phase);
        Assert.Contains(control.Status.Events, e => e.Code == "CONTROL_EXPIRED");
    }

    [Fact]
    public async Task ReleaseWaitCancellation_DoesNotGrantReplacementBeforeActualWriteAndCommandDrain()
    {
        var store = new RecordingStore { DelayStage = "result" };
        await using var control = new ControlledScanSession(new OwnedBrowser(), store);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        using var commandWait = new CancellationTokenSource();
        var command = control.ExecuteAsync(lease, 1, ReaderCommand.Tab, cancellationToken: commandWait.Token);
        await store.Started.Task;
        commandWait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        using var releaseWait = new CancellationTokenSource();
        var release = control.ReleaseAsync(lease, releaseWait.Token);
        releaseWait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => release);
        Assert.Equal(ControllerPhase.Revoking, control.Status.Phase);
        Assert.Equal(1, control.Status.ActiveCommands);
        Assert.Equal(1, control.Status.Owned.WritesInFlight);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
        store.Release.SetResult();
        await Until(() => control.Status.Phase == ControllerPhase.Unowned);
        Assert.Equal(0, control.Status.ActiveCommands);
        Assert.Equal(0, control.Status.Owned.WritesInFlight);
        var next = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(2, next.Generation); Assert.NotEqual(lease.Token, next.Token);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(lease, 2, ReaderCommand.Tab));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(next, 1, ReaderCommand.Tab));
        var result = await control.ExecuteAsync(next, 2, ReaderCommand.ReadCurrent);
        Assert.Equal(2, result.Completion.Intent.ControllerGeneration);
        Assert.Contains(result.Evidence!.Events, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
    }

    [Fact]
    public async Task Expiry_DrainsLateCaptureAndRejectsQueuedInputBeforeNewGeneration()
    {
        var time = new FakeTimeProvider();
        var browser = new OwnedBrowser { CaptureWork = new() };
        var store = new RecordingStore();
        await using var control = new ControlledScanSession(browser, store, time);
        var lease = await control.AcquireAsync(TimeSpan.FromSeconds(1));
        var read = control.ExecuteAsync(lease, 1, ReaderCommand.ReadCurrent);
        await browser.CaptureStarted.Task;
        var tab = control.ExecuteAsync(lease, 1, ReaderCommand.Tab);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ControllerPhase.Revoking, control.Status.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromSeconds(1)));
        browser.CaptureWork.SetResult(browser.Snapshot());
        await Assert.ThrowsAsync<BrowserOperationException>(() => read);
        await Assert.ThrowsAsync<BrowserOperationException>(() => tab);
        await Until(() => control.Status.Phase == ControllerPhase.Unowned);
        Assert.Equal(0, browser.Keys); Assert.Empty(store.Records.OfType<OwnedCommandEvidence>());
    }

    [Fact]
    public async Task ExpiryDuringNativeCall_RetainsActualWorkUntilAdapterStopAcknowledgement()
    {
        var time = new FakeTimeProvider(); var browser = new DeferredInputBrowser();
        await using var control = new ControlledScanSession(browser, new RecordingStore(), time);
        var lease = await control.AcquireAsync(TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        var command = control.ExecuteAsync(lease, 1, ReaderCommand.Tab, cancellationToken: cancellation.Token);
        await browser.InputStarted.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ControllerPhase.Revoking, control.Status.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromSeconds(1)));
        browser.InputSettled.SetResult();
        await Until(() => control.Status.Phase == ControllerPhase.Unowned);
        Assert.Equal(1, browser.Completed); Assert.Equal(0, control.Status.ActiveCommands);
        Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
    }

    [Fact]
    public async Task UnconfirmedDrain_FaultsClosedAndLateWriteCannotGrantReplacement()
    {
        var time = new FakeTimeProvider(); var store = new RecordingStore { DelayStage = "result" };
        var control = new ControlledScanSession(new OwnedBrowser(), store, time);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var command = control.ExecuteAsync(lease, 1, ReaderCommand.ReadCurrent);
        await store.Started.Task;
        var release = control.ReleaseAsync(lease);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("CONTROL_DRAIN_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => release)).Code);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        store.Release.SetResult();
        await Assert.ThrowsAsync<BrowserOperationException>(() => command);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        await control.DisposeAsync();
        Assert.Equal(ControllerPhase.Closed, control.Status.Phase);
        Assert.Equal("CONTROL_DRAIN_UNCONFIRMED", control.Status.FaultCode);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("Permitted")]
    public async Task ReplacementLease_WaitsForFreshPersistedBaselineBeforeAdmission(string stage)
    {
        var store = new RecordingStore { DelayStage = stage };
        await using var control = new ControlledScanSession(new OwnedBrowser(), store);
        var first = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await control.ReleaseAsync(first);
        var acquire = control.AcquireAsync(TimeSpan.FromMinutes(1));
        await store.Started.Task;
        Assert.Equal(ControllerPhase.Acquiring, control.Status.Phase);
        Assert.False(acquire.IsCompleted);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(first, 2, ReaderCommand.Tab));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
        store.Release.SetResult(); var second = await acquire;
        Assert.Equal(2, second.Generation); Assert.Equal(2, control.Status.Owned.Capture.Epoch);
    }

    [Fact]
    public async Task DuplicateOwnerAndGrantLimit_CannotCreateCompetingNativeAuthority()
    {
        var browser = new OwnedBrowser(); var store = new RecordingStore();
        await using var control = new ControlledScanSession(browser, store, maximumGrants: 1);
        Assert.Equal("CONTROL_BROWSER_ALREADY_OWNED", Assert.Throws<BrowserOperationException>(
            () => new ControlledScanSession(browser, new RecordingStore())).Code);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await control.ReleaseAsync(lease);
        Assert.Equal("CONTROL_GRANT_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => control.AcquireAsync(TimeSpan.FromMinutes(1)))).Code);
        Assert.Equal(ControllerPhase.Unowned, control.Status.Phase); Assert.Equal(0, browser.Keys);
    }

    [Fact]
    public async Task Close_DrainsActualCommandRejectsAllAuthorityAndIsIdempotent()
    {
        var store = new RecordingStore { DelayStage = "intent" }; var browser = new OwnedBrowser();
        var control = new ControlledScanSession(browser, store);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var command = control.ExecuteAsync(lease, 1, ReaderCommand.Tab); await store.Started.Task;
        var close = control.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(lease, 1, ReaderCommand.Tab));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
        store.Release.SetResult();
        await Assert.ThrowsAsync<BrowserOperationException>(() => command); await close;
        await control.DisposeAsync();
        Assert.Equal(0, browser.Keys); Assert.Equal(CapturePhase.Closed, browser.CaptureStatus.Phase);
        Assert.Equal(ControllerPhase.Closed, control.Status.Phase); Assert.Equal(0, control.Status.ActiveCommands);
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(1, timeout.Token);
    }
    [Fact]
    public async Task DelayedExpiryTimer_CannotBypassMonotonicCheckAtFinalNativeDispatch()
    {
        var time = new SuppressedTimers(); var store = new RecordingStore { DelayStage = "intent" };
        var browser = new OwnedBrowser();
        await using var control = new ControlledScanSession(browser, store, time);
        var lease = await control.AcquireAsync(TimeSpan.FromSeconds(1));
        var command = control.ExecuteAsync(lease, 1, ReaderCommand.Tab);
        await store.Started.Task;
        time.Advance(TimeSpan.FromSeconds(1));
        store.Release.SetResult();
        await Assert.ThrowsAsync<BrowserOperationException>(() => command);
        await Until(() => control.Status.Phase == ControllerPhase.Unowned);
        var completion = Assert.Single(store.Records.OfType<OwnedCommandCompletion>());
        Assert.Equal(1, completion.InputRequests); Assert.Equal(0, completion.CompletedInputCalls);
        Assert.False(completion.InputUncertain); Assert.Equal(0, browser.Keys);
        Assert.Empty(store.Records.OfType<OwnedCommandEvidence>());
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("Permitted")]
    public async Task FailedReplacementBaseline_NeverIssuesAuthorityAndFaultsClosed(string stage)
    {
        var store = new RecordingStore { FailStage = stage };
        await using var control = new ControlledScanSession(new OwnedBrowser(), store);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await control.ReleaseAsync(lease);
        Assert.Equal("CONTROL_ACQUIRE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => control.AcquireAsync(TimeSpan.FromMinutes(1)))).Code);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        Assert.Equal(1, control.Status.Generation);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(lease, 2, ReaderCommand.Tab));
    }

    [Fact]
    public async Task StoreFailure_SurfacesControllerFaultAndKeepsAuthorityClosed()
    {
        var store = new RecordingStore { FailStage = "result" };
        await using var control = new ControlledScanSession(new OwnedBrowser(), store);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(lease, 1, ReaderCommand.ReadCurrent));
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        Assert.Equal("CONTROL_OWNED_FAULTED", control.Status.FaultCode);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(lease, 1, ReaderCommand.Tab));
        Assert.DoesNotContain("private-error-canary", JsonSerializer.Serialize(control.Status));
    }

    [Fact]
    public async Task CloseDuringReplacementBaseline_CannotReturnALeaseOrDispatchInput()
    {
        var store = new RecordingStore { DelayStage = "baseline" }; var browser = new OwnedBrowser();
        var control = new ControlledScanSession(browser, store);
        await control.ReleaseAsync(await control.AcquireAsync(TimeSpan.FromMinutes(1)));
        var acquire = control.AcquireAsync(TimeSpan.FromMinutes(1)); await store.Started.Task;
        var close = control.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        store.Release.SetResult();
        await Assert.ThrowsAsync<BrowserOperationException>(() => acquire);
        await close;
        Assert.Equal(ControllerPhase.Closed, control.Status.Phase); Assert.Equal(1, control.Status.Generation);
        Assert.Equal(0, browser.Keys); Assert.Equal(0, control.Status.Owned.WritesInFlight);
    }

    private sealed class SuppressedTimers : TimeProvider
    {
        private readonly FakeTimeProvider _time = new();
        public TimeSpan UtcOffset { get; set; }
        public void Advance(TimeSpan duration) => _time.Advance(duration);
        public override DateTimeOffset GetUtcNow() => _time.GetUtcNow() + UtcOffset;
        public override long GetTimestamp() => _time.GetTimestamp();
        public override long TimestampFrequency => _time.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new SilentTimer();
        private sealed class SilentTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    [Fact]
    public async Task WallClockChanges_DoNotExtendOrPrematurelyExpireMonotonicAuthority()
    {
        var time = new SuppressedTimers();
        await using var control = new ControlledScanSession(new OwnedBrowser(), new RecordingStore(), time);
        var lease = await control.AcquireAsync(TimeSpan.FromSeconds(1));
        time.UtcOffset = TimeSpan.FromDays(1);
        Assert.Equal(ControllerPhase.Active, control.Status.Phase);
        await control.ExecuteAsync(lease, 1, ReaderCommand.ReadCurrent);
        time.UtcOffset = TimeSpan.FromDays(-1); time.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(lease, 1, ReaderCommand.Tab));
        await Until(() => control.Status.Phase == ControllerPhase.Unowned);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void InvalidGrantBounds_AreRejectedBeforeOwningAnAdapter(int maximum)
    {
        var browser = new OwnedBrowser();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ControlledScanSession(browser, new RecordingStore(), maximumGrants: maximum));
        Assert.Equal(CapturePhase.Permitted, browser.CaptureStatus.Phase);
    }

    [Fact]
    public async Task FailedConstructor_DoesNotPoisonOwnershipRegistry()
    {
        var browser = new OwnedBrowser();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ControlledScanSession(browser, new RecordingStore(), maximumCommands: 0));
        await using var control = new ControlledScanSession(browser, new RecordingStore());
        Assert.Equal(1, (await control.AcquireAsync(TimeSpan.FromSeconds(1))).Generation);
    }

    [Fact]
    public async Task CancelledBeforeAdmission_PublishesNoIntentAndSendsNoInput()
    {
        var store = new RecordingStore(); var browser = new OwnedBrowser();
        await using var control = new ControlledScanSession(browser, store);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.ExecuteAsync(lease, 1, ReaderCommand.Tab, cancellationToken: cancellation.Token));
        Assert.Empty(store.Records); Assert.Equal(0, browser.Keys); Assert.Equal(0, control.Status.ActiveCommands);
        Assert.Equal(ControllerPhase.Active, control.Status.Phase);
    }

    [Fact]
    public async Task NativeFailure_PreservesInputUncertaintyAndNeverInventsDestinationSuccess()
    {
        var browser = new DeferredInputBrowser { ThrowAfterInput = true };
        await using var control = new ControlledScanSession(browser, new RecordingStore());
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var command = control.ExecuteAsync(lease, 1, ReaderCommand.Tab, expectation: new("heading", "unseen goal"));
        await browser.InputStarted.Task; browser.InputSettled.SetResult();
        var receipt = await command;
        Assert.Equal(ReaderOutcome.BrowserError, receipt.Evidence!.Step.Outcome);
        Assert.NotEqual(VerificationStatus.Satisfied, receipt.Evidence.Step.Verification);
        Assert.True(receipt.Completion.InputUncertain); Assert.True(receipt.Completion.InputMayHaveOccurred);
        Assert.Equal(0, receipt.Completion.CompletedInputCalls);
        Assert.Equal(OwnedCompletionState.ResultStored, receipt.Completion.State);
    }

    [Fact]
    public async Task DeniedReaderKey_StaysPolicyDenialWithoutNativeInputOrControllerFailure()
    {
        var browser = new OwnedBrowser();
        await using var control = new ControlledScanSession(browser, new RecordingStore());
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var receipt = await control.ExecuteAsync(lease, 1, ReaderCommand.PressKey, "outside-policy");
        Assert.Equal(ReaderOutcome.Denied, receipt.Evidence!.Step.Outcome);
        Assert.Equal(0, receipt.Completion.InputRequests); Assert.False(receipt.Completion.InputMayHaveOccurred);
        Assert.Equal(0, browser.Keys); Assert.Equal(ControllerPhase.Active, control.Status.Phase);
    }

    [Fact]
    public async Task MaximumGrantBoundary_RemainsBoundedAndNeverReusesGeneration()
    {
        await using var control = new ControlledScanSession(new OwnedBrowser(), new RecordingStore());
        for (var generation = 1; generation <= 32; generation++)
        {
            var lease = await control.AcquireAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(generation, lease.Generation);
            Assert.Equal(generation, control.Status.Owned.Capture.Epoch);
            await control.ReleaseAsync(lease);
        }
        Assert.Equal("CONTROL_GRANT_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => control.AcquireAsync(TimeSpan.FromSeconds(1)))).Code);
        Assert.Equal(32, control.Status.Generation); Assert.True(control.Status.Events.Length <= 256);
        Assert.Equal(CapturePhase.Excluded, control.Status.Owned.Capture.Phase);
    }

    [Fact]
    public async Task CommandWaitTimeout_DoesNotReleaseActualWriteOrControllerDrain()
    {
        var time = new FakeTimeProvider(); var store = new RecordingStore { DelayStage = "intent" };
        await using var control = new ControlledScanSession(new OwnedBrowser(), store, time);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(2));
        var command = control.ExecuteAsync(lease, 1, ReaderCommand.Tab); await store.Started.Task;
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("CONTROL_COMMAND_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => command)).Code);
        Assert.Equal(1, control.Status.ActiveCommands); Assert.Equal(1, control.Status.Owned.WritesInFlight);
        var release = control.ReleaseAsync(lease);
        Assert.False(release.IsCompleted);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
        store.Release.SetResult(); await release;
        Assert.Equal(ControllerPhase.Unowned, control.Status.Phase); Assert.Equal(0, control.Status.ActiveCommands);
        Assert.Equal(0, Assert.Single(store.Records.OfType<OwnedCommandCompletion>()).InputRequests);
    }

    [Fact]
    public async Task UnconfirmedClose_StaysFaultedAfterLateWriteInsteadOfClaimingClosed()
    {
        var time = new FakeTimeProvider(); var store = new RecordingStore { DelayStage = "intent" };
        var control = new ControlledScanSession(new OwnedBrowser(), store, time);
        var lease = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var command = control.ExecuteAsync(lease, 1, ReaderCommand.Tab); await store.Started.Task;
        var close = control.DisposeAsync().AsTask(); time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("CONTROL_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => close)).Code);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        Assert.Equal(1, control.Status.Owned.WritesInFlight);
        store.Release.SetResult(); await Assert.ThrowsAsync<BrowserOperationException>(() => command);
        await Until(() => control.Status.Owned.WritesInFlight == 0);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        Assert.Equal("CONTROL_CLOSE_UNCONFIRMED", control.Status.FaultCode);
    }
    private sealed class DeferredInputBrowser : IScreenReaderBrowser, ICaptureControl, IAsyncDisposable
    {
        private readonly OwnedBrowser _inner = new();
        public bool ThrowAfterInput { get; init; }
        public TaskCompletionSource InputStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InputSettled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Completed { get; private set; }
        public CaptureStatus CaptureStatus => _inner.CaptureStatus;
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => _inner.CaptureAsync(cancellationToken);
        public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            InputStarted.SetResult(); await InputSettled.Task; Completed++;
            if (ThrowAfterInput) throw new BrowserOperationException("NATIVE_INPUT_FAILED");
        }
        public async Task<CaptureExclusionLease> ExcludeCaptureAsync()
        {
            var lease = await _inner.ExcludeCaptureAsync();
            if (InputStarted.Task.IsCompleted) await InputSettled.Task;
            return lease;
        }
        public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease) => _inner.ResumeCaptureAsync(lease);
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
