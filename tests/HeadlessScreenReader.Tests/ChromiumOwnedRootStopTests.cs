using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ChromiumOwnedRootStopTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task OwnedRootStop_StartsAtTwoSecondsAndAckDoesNotReplaceExitProof(int contextTime)
    {
        var time = new FakeTimeProvider(); var context = Signal(); var browserStarted = Signal(); var process = Signal(); var stop = Signal(); var stops = 0;
        Task? actual = null; ChromiumShutdown? shutdown = null;
        shutdown = new(() => context.Task, () => { browserStarted.SetResult(); return Task.CompletedTask; }, () => { }, time,
            waitProcessExit: () => process.Task, stopOwnedProcess: () =>
            { Assert.Same(actual, shutdown!.CloseAsync()); Interlocked.Increment(ref stops); stop.SetResult(); return Task.CompletedTask; });
        actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(contextTime)); context.SetResult(); await browserStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(1999 - contextTime)); Assert.Equal(0, stops);
        time.Advance(TimeSpan.FromMilliseconds(1)); await stop.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await NativeShutdownTestSync.UntilAsync(() => shutdown.Status.OwnedProcessStopCallSettled);
        Assert.True(shutdown.Status.OwnedProcessStopRequested); Assert.True(shutdown.Status.OwnedProcessStopAcknowledged);
        Assert.False(shutdown.Status.TransportDisposed); Assert.False(shutdown.Status.ProcessExitConfirmed); Assert.False(actual.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1999)); process.SetResult(); await actual;
        Assert.Equal(1, stops); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(3999d, shutdown.Status.ProcessExitConfirmationMilliseconds);
        Assert.False(shutdown.Status.TransportEscalatedBeforeProcessExit); Assert.Equal(0, shutdown.Status.ActiveCalls);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1999)]
    public async Task OwnedRootStop_TimelyGracefulExitNeverDispatchesFallback(int exitAt)
    {
        var time = new FakeTimeProvider(); var process = Signal(); var stops = 0;
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () => { }, time,
            waitProcessExit: () => process.Task, stopOwnedProcess: () => { stops++; return Task.CompletedTask; });
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(exitAt)); process.SetResult(); await actual;
        Assert.Equal(0, stops); Assert.False(shutdown.Status.OwnedProcessStopRequested); Assert.Null(shutdown.Status.OwnedProcessStopDurationMilliseconds);
        Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.True(shutdown.Status.ProcessExitConfirmed);
    }
    [Fact]
    public async Task OwnedRootStop_RequiresAnIndependentWatchBeforeAnyDispatch()
    {
        var stops = 0;
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () => { },
            stopOwnedProcess: () => { stops++; return Task.CompletedTask; });
        await shutdown.CloseAsync(); Assert.Equal(0, stops); Assert.False(shutdown.Status.OwnedProcessStopRequested);
        Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase);
    }
    [Fact]
    public async Task OwnedRootStop_FailureIsStickyEvenWhenTransportLaterConfirmsRootExit()
    {
        var time = new FakeTimeProvider(); var process = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () => process.SetResult(), time,
            waitProcessExit: () => process.Task, stopOwnedProcess: () => Task.FromException(new IOException("private-root-stop-error")));
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal("CHROMIUM_OWNED_ROOT_STOP_UNCONFIRMED", error.Code); Assert.DoesNotContain("private-root-stop-error", error.ToString());
        Assert.True(shutdown.Status.OwnedProcessStopCallSettled); Assert.False(shutdown.Status.OwnedProcessStopAcknowledged);
        Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.True(shutdown.Status.TransportEscalatedBeforeProcessExit);
        Assert.Equal(0, shutdown.Status.ActiveCalls); Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase);
    }
    [Fact]
    public async Task OwnedRootStop_NativeExitCannotWaiveAnActuallyRejectedSdkClose()
    {
        var time = new FakeTimeProvider(); var process = Signal(); var browser = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () => { }, time, waitProcessExit: () => process.Task,
            stopOwnedProcess: () => { process.SetResult(); browser.SetException(new IOException("private-sdk-reject")); return Task.CompletedTask; });
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", error.Code); Assert.DoesNotContain("private-sdk-reject", error.ToString());
        Assert.True(shutdown.Status.OwnedProcessStopAcknowledged); Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.True(shutdown.Status.BrowserCallRejected);
        Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase); Assert.False(shutdown.Status.BrowserDeadlineExceeded);
    }
    [Fact]
    public async Task OwnedRootStop_TimelyExitAndCancelledWaitCannotDrainItsStillPendingActualCallback()
    {
        var time = new FakeTimeProvider(); var process = Signal(); var started = Signal(); var stop = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () => { }, time, waitProcessExit: () => process.Task,
            stopOwnedProcess: () => { started.SetResult(); return stop.Task; });
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2)); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(500)); process.SetResult();
        await NativeShutdownTestSync.UntilAsync(() => shutdown.Status is { Phase: BrowserShutdownPhase.DrainingCalls, TransportCallSettled: true });
        using var caller = new CancellationTokenSource(); var waiting = actual.WaitAsync(caller.Token); caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(actual.IsCompleted); Assert.Equal(1, shutdown.Status.ActiveCalls); Assert.False(shutdown.Status.OwnedProcessStopCallSettled);
        time.Advance(TimeSpan.FromMilliseconds(2500)); stop.SetResult(); await actual;
        Assert.Equal(2500d, shutdown.Status.ProcessExitConfirmationMilliseconds); Assert.Equal(3000d, shutdown.Status.OwnedProcessStopDurationMilliseconds);
        Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
    }
    [Theory]
    [InlineData(1999, false)]
    [InlineData(2000, true)]
    [InlineData(2500, true)]
    public async Task OwnedRootStop_ConfirmationStillMustPrecedeTheOriginalFourSecondDeadline(int cost, bool fails)
    {
        var time = new FakeTimeProvider(); var process = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () => { }, time, waitProcessExit: () => process.Task,
            stopOwnedProcess: () => { time.Advance(TimeSpan.FromMilliseconds(cost)); process.SetResult(); return Task.CompletedTask; });
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2));
        if (fails) Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => actual)).Code);
        else await actual;
        Assert.Equal(fails, shutdown.Status.BrowserDeadlineExceeded); Assert.Equal(2000d + cost, shutdown.Status.ProcessExitConfirmationMilliseconds);
        Assert.True(shutdown.Status.OwnedProcessStopCallSettled); Assert.True(shutdown.Status.OwnedProcessStopAcknowledged);
        Assert.Equal(fails ? BrowserShutdownPhase.Faulted : BrowserShutdownPhase.Closed, shutdown.Status.Phase);
    }
}
