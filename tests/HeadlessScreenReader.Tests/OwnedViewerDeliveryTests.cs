using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class OwnedViewerDeliveryTests
{
    [Theory]
    [InlineData("scan")]
    [InlineData("session")]
    public async Task ViewerService_RejectsCompositionBoundToAnotherOwnerOrSession(string partition)
    {
        var store = new RecordingStore(); await using var control = new ControlledScanSession(new OwnedBrowser(), store);
        var scope = new ViewerScope(partition == "scan" ? Guid.NewGuid().ToString("N") : store.ScanId,
            partition == "session" ? Guid.NewGuid().ToString("N") : control.SessionId);
        await using var broker = new PrivateViewerBroker(scope, Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => new HeadlessScreenReader.Cli.PrivateViewerService(broker, control));
        Assert.Empty(store.Records);
    }
    [Fact]
    public async Task ViewerUsesOnlyAcknowledgedRetainedObservationAndSendsNoInputOrNewCapture()
    {
        var browser = new OwnedBrowser(); var store = new RecordingStore();
        await using var control = new ControlledScanSession(browser, store);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.DeliverRetainedObservationAsync(1, _ => Task.CompletedTask));
        var command = await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var captures = browser.Captures; var writes = store.Records.Length;
        RetainedViewerObservation? viewed = null;
        await control.DeliverRetainedObservationAsync(1, value => { viewed = value; return Task.CompletedTask; });
        Assert.Equal(command.Evidence!.Snapshot, viewed!.Snapshot); Assert.Equal(1, viewed.Ordinal);
        Assert.Equal("LastPersistedObservation", viewed.ObservationKind); Assert.Null(viewed.Snapshot.DocumentUrl);
        Assert.Equal(captures, browser.Captures); Assert.Equal(0, browser.Keys); Assert.Equal(writes, store.Records.Length);
        await control.ReleaseAsync(ai);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.DeliverRetainedObservationAsync(1, _ => Task.CompletedTask));
        await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.DeliverRetainedObservationAsync(1, _ => Task.CompletedTask));
        await control.DeliverRetainedObservationAsync(2, value =>
        { Assert.Equal(0, value.Ordinal); Assert.Equal(2, value.Snapshot.CaptureEpoch); return Task.CompletedTask; });
    }

    [Theory]
    [InlineData("result")]
    [InlineData("completion")]
    public async Task UnacknowledgedCommand_CannotBecomeAViewerObservation(string stage)
    {
        var store = new RecordingStore { DelayStage = stage }; var browser = new OwnedBrowser();
        await using var control = new ControlledScanSession(browser, store);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var command = control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent); await store.Started.Task;
        var sends = 0; var view = control.DeliverRetainedObservationAsync(1, _ => { sends++; return Task.CompletedTask; });
        Assert.False(view.IsCompleted); Assert.Equal(0, sends);
        var stop = control.ReleaseAsync(ai); Assert.False(stop.IsCompleted);
        store.Release.SetResult(); await Assert.ThrowsAsync<BrowserOperationException>(() => command);
        await Assert.ThrowsAsync<BrowserOperationException>(() => view); await stop;
        Assert.Equal(0, sends); Assert.Equal(CapturePhase.Excluded, control.Status.Owned.Capture.Phase);
    }

    [Fact]
    public async Task CaptureGap_WaitsActualViewerDeliveryAfterCallerStopsWaitingAndRejectsQueuedViews()
    {
        var browser = new OwnedBrowser(); await using var control = new ControlledScanSession(browser, new RecordingStore());
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1)); await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var started = Signal(); var release = Signal(); var queuedSends = 0;
        var actual = control.DeliverRetainedObservationAsync(1, _ => { started.SetResult(); return release.Task; });
        await started.Task;
        var queued = control.DeliverRetainedObservationAsync(1, _ => { queuedSends++; return Task.CompletedTask; });
        using var wait = new CancellationTokenSource(); var caller = actual.WaitAsync(wait.Token); wait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => caller);
        var stop = control.ReleaseAsync(ai); Assert.False(stop.IsCompleted);
        Assert.Equal(CapturePhase.Draining, control.Status.Owned.Capture.Phase);
        Assert.Equal(2, control.Status.Owned.ActiveOperations);
        release.SetResult(); await actual; await Assert.ThrowsAsync<BrowserOperationException>(() => queued); await stop;
        Assert.Equal(0, queuedSends); Assert.Equal(0, control.Status.Owned.ActiveOperations);
    }

    [Fact]
    public async Task UnconfirmedViewerDrain_StaysClosedAfterLateTransportCompletion()
    {
        var time = new FakeTimeProvider(); var browser = new OwnedBrowser();
        var control = new ControlledScanSession(browser, new RecordingStore(), time);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1)); await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var release = Signal(); var actual = control.DeliverRetainedObservationAsync(1, _ => release.Task);
        var stop = control.ReleaseAsync(ai); time.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<BrowserOperationException>(() => stop);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase); Assert.Equal(1, control.Status.Owned.ActiveOperations);
        release.SetResult(); await actual;
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.DeliverRetainedObservationAsync(1, _ => Task.CompletedTask));
        Assert.Equal(0, control.Status.Owned.ActiveOperations); await control.DisposeAsync();
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
