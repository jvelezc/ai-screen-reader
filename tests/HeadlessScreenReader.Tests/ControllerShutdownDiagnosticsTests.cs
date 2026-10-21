using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class ControllerShutdownDiagnosticsTests
{
    [Fact]
    public async Task TimelyRootExit_CannotRecoverTheControllerDeadlineWhileActualOwnedStopIsPending()
    {
        var time = new FakeTimeProvider(); var context = Signal(); context.SetResult(); var transport = Signal(); var started = Signal(); var stop = Signal(); var process = Signal();
        var browser = new ClosingBrowser(time, context, transport, () => process.Task,
            stopOwnedProcess: () => { started.SetResult(); return stop.Task; });
        var control = new ControlledScanSession(browser, new RecordingStore(), time); var close = control.DisposeAsync().AsTask();
        try
        {
            time.Advance(TimeSpan.FromSeconds(2)); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromMilliseconds(500)); process.SetResult();
            await NativeShutdownTestSync.UntilAsync(() => browser.Native.Status is { Phase: BrowserShutdownPhase.DrainingCalls, TransportCallSettled: true });
            time.Advance(TimeSpan.FromMilliseconds(2499)); Assert.False(close.IsCompleted);
            Assert.True(control.Status.Shutdown!.ProcessExitConfirmed); Assert.Equal(1, control.Status.Shutdown.ActiveCalls);
            Assert.False(control.Status.Shutdown.OwnedProcessStopCallSettled);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal("CONTROL_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => close)).Code);
            Assert.Equal(ControllerPhase.Faulted, control.Status.Phase); Assert.False(browser.Native.Status.BrowserDeadlineExceeded);
        }
        finally { stop.TrySetResult(); }
        await browser.Native.CloseAsync(); Assert.True(control.Status.Shutdown!.OwnedProcessStopAcknowledged);
        Assert.Equal(BrowserShutdownPhase.Closed, control.Status.Shutdown.Phase); Assert.Equal(0, control.Status.Shutdown.ActiveCalls);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
    }
    [Fact]
    public async Task TimelyRootExit_CannotRecoverTheFiveSecondControllerDeadlineWhileActualTransportStopIsPending()
    {
        var time = new FakeTimeProvider(); var context = Signal(); context.SetResult(); var started = Signal(); var process = Signal();
        using var release = new ManualResetEventSlim();
        var browser = new ClosingBrowser(time, context, started, () => process.Task, () =>
        { started.SetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); });
        var control = new ControlledScanSession(browser, new RecordingStore(), time);
        var close = control.DisposeAsync().AsTask();
        try
        {
            time.Advance(TimeSpan.FromSeconds(2)); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromMilliseconds(500)); process.SetResult();
            await NativeShutdownTestSync.UntilAsync(() => browser.Native.Status.Phase == BrowserShutdownPhase.DrainingCalls);
            time.Advance(TimeSpan.FromMilliseconds(2499)); Assert.False(close.IsCompleted);
            Assert.Equal(2500d, control.Status.Shutdown!.ProcessExitConfirmationMilliseconds);
            Assert.True(control.Status.Shutdown.ProcessExitConfirmed); Assert.False(control.Status.Shutdown.TransportCallSettled);
            Assert.Equal(1, control.Status.Shutdown.ActiveCalls);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal("CONTROL_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => close)).Code);
            Assert.Equal(ControllerPhase.Faulted, control.Status.Phase); Assert.False(browser.Native.Status.BrowserDeadlineExceeded);
        }
        finally { release.Set(); }
        await browser.Native.CloseAsync();
        Assert.True(control.Status.Shutdown!.TransportCallSettled); Assert.Equal(0, control.Status.Shutdown.ActiveCalls);
        Assert.Equal(BrowserShutdownPhase.Closed, control.Status.Shutdown.Phase); Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
    }
    [Fact]
    public async Task ControllerClose_KeepsFiveSecondDeadlineAndExposesActualPendingClosureWithoutLateRecovery()
    {
        var time = new FakeTimeProvider(); var context = Signal(); var transport = Signal();
        var browser = new ClosingBrowser(time, context, transport);
        var control = new ControlledScanSession(browser, new RecordingStore(), time);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent); await control.ReleaseAsync(ai);
        var close = control.DisposeAsync().AsTask(); time.Advance(TimeSpan.FromSeconds(1)); await transport.Task;
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (control.Status.Shutdown is not { Phase: BrowserShutdownPhase.DrainingCalls, TransportCallSettled: true })
            await Task.Delay(1, bound.Token);
        Assert.False(close.IsCompleted); Assert.Equal(1, control.Status.Shutdown!.ActiveCalls);
        Assert.Equal(BrowserShutdownPhase.DrainingCalls, control.Status.Shutdown.Phase);
        time.Advance(TimeSpan.FromMilliseconds(3999)); Assert.False(close.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("CONTROL_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => close)).Code);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase); Assert.Equal(1, control.Status.Shutdown.ActiveCalls);
        context.SetResult(); await browser.Native.CloseAsync();
        Assert.Equal(0, control.Status.Shutdown.ActiveCalls); Assert.Equal(BrowserShutdownPhase.Closed, control.Status.Shutdown.Phase);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class ClosingBrowser : IScreenReaderBrowser, ICaptureControl, IAsyncDisposable, IBrowserShutdownDiagnostics
    {
        private readonly OwnedBrowser _inner = new();
        public ChromiumShutdown Native { get; }
        public ClosingBrowser(TimeProvider time, TaskCompletionSource context, TaskCompletionSource transport,
            Func<Task>? processExit = null, Action? stopTransport = null, Func<Task>? stopOwnedProcess = null) =>
            Native = new(() => context.Task, () => _inner.DisposeAsync().AsTask(), stopTransport ?? (() => transport.SetResult()), time,
                waitProcessExit: processExit, stopOwnedProcess: stopOwnedProcess);
        public BrowserShutdownStatus ShutdownStatus => Native.Status;
        public CaptureStatus CaptureStatus => _inner.CaptureStatus;
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => _inner.CaptureAsync(cancellationToken);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => _inner.PressKeyAsync(key, cancellationToken);
        public Task<CaptureExclusionLease> ExcludeCaptureAsync() => _inner.ExcludeCaptureAsync();
        public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease) => _inner.ResumeCaptureAsync(lease);
        public ValueTask DisposeAsync() => new(Native.CloseAsync());
    }
}
