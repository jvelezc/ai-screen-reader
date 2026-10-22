using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ChromiumTransportEscalationTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [Theory]
    [InlineData(0, false)]
    [InlineData(250, false)]
    [InlineData(1000, false)]
    [InlineData(0, true)]
    [InlineData(1000, true)]
    public async Task OwnedTransport_StopsAtTwoSecondsOfTotalNativeTimeAndStillRequiresActualExit(int contextTime, bool sdkAcknowledged)
    {
        var time = new GraceTimerTimeProvider(); var context = Signal(); var browser = Signal(); var process = Signal(); var stopped = Signal(); var started = Signal(); var stops = 0;
        var shutdown = new ChromiumShutdown(() => context.Task, () => { started.SetResult(); return sdkAcknowledged ? Task.CompletedTask : browser.Task; },
            () => { stops++; browser.TrySetResult(); stopped.SetResult(); }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(contextTime)); context.SetResult();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The callback starts before WaitAsync registers its relative timer. Advancing
        // fake time in that gap would move the deadline; wait for the actual timer.
        await time.GraceTimerArmed.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(1999 - contextTime)); Assert.False(stopped.Task.IsCompleted); Assert.False(actual.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1)); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); await NativeShutdownTestSync.UntilAsync(() => shutdown.Status.TransportCallSettled);
        Assert.True(shutdown.Status.TransportDisposed); Assert.True(shutdown.Status.TransportEscalatedBeforeProcessExit);
        Assert.False(shutdown.Status.ProcessExitConfirmed); Assert.False(shutdown.Status.BrowserDeadlineExceeded); Assert.False(actual.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1999)); Assert.False(actual.IsCompleted); process.SetResult(); await actual;
        Assert.Equal(1, stops); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.True(shutdown.Status.ProcessCallSettled); Assert.True(shutdown.Status.BrowserCallSettled);
        Assert.Equal(sdkAcknowledged, shutdown.Status.BrowserCloseAcknowledgedBeforeTransport);
        Assert.False(shutdown.Status.TransportEscalatedAfterProcessExit); Assert.Equal(3999d, shutdown.Status.NativeElapsedMilliseconds);
    }
    [Theory]
    [InlineData(1999, false)]
    [InlineData(2000, true)]
    [InlineData(2500, true)]
    public async Task OwnedTransport_StopCostConsumesTheOriginalFourSecondBudget(int stopCost, bool fails)
    {
        var time = new FakeTimeProvider(); var browser = Signal(); var process = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () =>
        { time.Advance(TimeSpan.FromMilliseconds(stopCost)); process.SetResult(); browser.SetResult(); }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2));
        if (fails) Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => actual)).Code);
        else await actual;
        Assert.Equal(fails ? BrowserShutdownPhase.Faulted : BrowserShutdownPhase.Closed, shutdown.Status.Phase);
        Assert.Equal(fails, shutdown.Status.BrowserDeadlineExceeded); Assert.Equal(2000d + stopCost, shutdown.Status.NativeElapsedMilliseconds);
        Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.Equal(2000d + stopCost, shutdown.Status.ProcessExitConfirmationMilliseconds);
    }
    [Fact]
    public async Task OwnedTransport_CancelledCallerCannotAcknowledgeTheStillPendingProcessWatch()
    {
        var time = new FakeTimeProvider(); var process = Signal(); var stopped = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () => stopped.SetResult(), time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2)); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); await NativeShutdownTestSync.UntilAsync(() => shutdown.Status.TransportCallSettled);
        using var caller = new CancellationTokenSource(); var wait = actual.WaitAsync(caller.Token); caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.False(actual.IsCompleted); Assert.False(shutdown.Status.ProcessExitConfirmed); Assert.Equal(1, shutdown.Status.ActiveCalls);
        process.SetResult(); await actual; Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase);
    }
    [Fact]
    public async Task OwnedTransport_SdkRejectionDuringEarlyStopRemainsAFailureAfterActualExit()
    {
        var time = new FakeTimeProvider(); var browser = Signal(); var process = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () =>
        { browser.SetException(new IOException("private-sdk-interrupted")); process.SetResult(); }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", error.Code); Assert.DoesNotContain("private-sdk-interrupted", error.ToString());
        Assert.True(shutdown.Status.BrowserCallRejected); Assert.True(shutdown.Status.ProcessExitConfirmed);
        Assert.False(shutdown.Status.BrowserCloseAcknowledged); Assert.False(shutdown.Status.BrowserDeadlineExceeded);
        Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
    }
    [Fact]
    public async Task OwnedTransport_StopFailureStaysFaultedAfterProcessExitAndDoesNotLeakNativeText()
    {
        var time = new FakeTimeProvider(); var browser = Signal(); var process = Signal(); var stops = 0;
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () =>
        { stops++; process.SetResult(); browser.SetResult(); throw new IOException("private-transport-error"); }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal("CHROMIUM_TRANSPORT_CLOSE_UNCONFIRMED", error.Code); Assert.DoesNotContain("private-transport-error", error.ToString());
        Assert.Equal(1, stops); Assert.False(shutdown.Status.TransportDisposed); Assert.True(shutdown.Status.ProcessExitConfirmed);
        Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
    }
    [Fact]
    public async Task OwnedTransport_ReentrantAndRepeatedCloseJoinOneActualStop()
    {
        var time = new FakeTimeProvider(); var browser = Signal(); var process = Signal(); var stops = 0; Task? reentered = null; ChromiumShutdown? shutdown = null;
        shutdown = new(() => Task.CompletedTask, () => browser.Task, () =>
        { stops++; reentered = shutdown!.CloseAsync(); browser.SetResult(); process.SetResult(); }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2)); await actual;
        Assert.Same(actual, reentered); Assert.Same(actual, shutdown.CloseAsync()); Assert.Equal(1, stops);
        Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
    }
    [Fact]
    public async Task OwnedTransport_ProcessExitDoesNotReleaseAnUnsettledContextCallback()
    {
        var time = new GraceTimerTimeProvider(); var context = Signal(); var browser = Signal(); var process = Signal(); var started = Signal(); var stopped = Signal();
        var shutdown = new ChromiumShutdown(() => context.Task, () => { started.SetResult(); return browser.Task; },
            () => { browser.SetResult(); process.SetResult(); stopped.SetResult(); }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(1)); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await time.GraceTimerArmed.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(1)); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); await NativeShutdownTestSync.UntilAsync(() => shutdown.Status.TransportCallSettled);
        Assert.False(actual.IsCompleted); Assert.False(shutdown.Status.ContextCallSettled);
        context.SetException(new IOException("private-context-interrupted")); await actual;
        Assert.True(shutdown.Status.ContextEscalated); Assert.True(shutdown.Status.ContextCallSettled); Assert.False(shutdown.Status.ContextCloseAcknowledged);
        Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
    }
    [Fact]
    public async Task OwnedTransport_ConfirmedExitCannotRecoverAFailedCollector()
    {
        var time = new FakeTimeProvider(); var browser = Signal(); var process = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () => { browser.SetResult(); process.SetResult(); }, time,
            () => Task.FromException(new IOException("private-collector-error")), () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", error.Code); Assert.DoesNotContain("private-collector-error", error.ToString());
        Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.False(shutdown.Status.CollectorStopAcknowledged);
        Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
    }

    private sealed class GraceTimerTimeProvider : TimeProvider
    {
        private readonly FakeTimeProvider _clock = new();
        private readonly TaskCompletionSource _armed = Signal();
        private readonly DateTimeOffset _deadline;
        public GraceTimerTimeProvider() => _deadline = _clock.GetUtcNow() + TimeSpan.FromSeconds(2);
        public Task GraceTimerArmed => _armed.Task;
        public override long TimestampFrequency => _clock.TimestampFrequency;
        public override long GetTimestamp() => _clock.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();
        public void Advance(TimeSpan amount) => _clock.Advance(amount);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _clock.CreateTimer(callback, state, dueTime, period);
            if (_clock.GetUtcNow() + dueTime == _deadline) _armed.TrySetResult();
            return timer;
        }
    }
}
