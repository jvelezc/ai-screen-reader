using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ChromiumConcurrentTransportTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimelyRootExit_RemainsDistinctFromTheStillPendingActualTransportStop(bool stopFails)
    {
        var time = new FakeTimeProvider(); var process = Signal(); var started = Signal();
        using var release = new ManualResetEventSlim();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () =>
        {
            started.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            if (stopFails) throw new IOException("private-transport-tail");
        }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync();
        try
        {
            time.Advance(TimeSpan.FromSeconds(2)); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromMilliseconds(500)); process.SetResult();
            await NativeShutdownTestSync.UntilAsync(() => shutdown.Status.Phase == BrowserShutdownPhase.DrainingCalls);
            Assert.Equal(2500d, shutdown.Status.ProcessExitConfirmationMilliseconds);
            Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.False(shutdown.Status.TransportDisposed);
            Assert.False(shutdown.Status.TransportCallSettled); Assert.Equal(1, shutdown.Status.ActiveCalls); Assert.False(actual.IsCompleted);
            time.Advance(TimeSpan.FromMilliseconds(2500));
            Assert.False(shutdown.Status.BrowserDeadlineExceeded); Assert.False(actual.IsCompleted);
        }
        finally { release.Set(); }
        if (stopFails)
        {
            var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
            Assert.Equal("CHROMIUM_TRANSPORT_CLOSE_UNCONFIRMED", error.Code); Assert.DoesNotContain("private-transport-tail", error.ToString());
        }
        else await actual;
        Assert.Equal(stopFails ? BrowserShutdownPhase.Faulted : BrowserShutdownPhase.Closed, shutdown.Status.Phase);
        Assert.Equal(!stopFails, shutdown.Status.TransportDisposed); Assert.True(shutdown.Status.TransportCallSettled);
        Assert.Equal(3000d, shutdown.Status.TransportStopDurationMilliseconds); Assert.Equal(5000d, shutdown.Status.NativeElapsedMilliseconds);
        Assert.Equal(2500d, shutdown.Status.ProcessExitConfirmationMilliseconds); Assert.False(shutdown.Status.BrowserDeadlineExceeded);
        Assert.Equal(0, shutdown.Status.ActiveCalls);
    }
    [Fact]
    public async Task TimelyRootExitAndSettledTransport_CannotReleaseTheActualSdkCallbackOrCancelledWaiter()
    {
        var time = new FakeTimeProvider(); var process = Signal(); var browser = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () => { }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(2));
        await NativeShutdownTestSync.UntilAsync(() => shutdown.Status.TransportCallSettled);
        time.Advance(TimeSpan.FromMilliseconds(500)); process.SetResult();
        await NativeShutdownTestSync.UntilAsync(() => shutdown.Status.Phase == BrowserShutdownPhase.DrainingCalls);
        Assert.True(shutdown.Status.TransportDisposed); Assert.True(shutdown.Status.ProcessExitConfirmed);
        Assert.False(shutdown.Status.BrowserCallSettled); Assert.Equal(1, shutdown.Status.ActiveCalls);
        using var caller = new CancellationTokenSource(); var waiting = actual.WaitAsync(caller.Token); caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting); Assert.False(actual.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(500)); browser.SetResult(); await actual;
        Assert.True(shutdown.Status.BrowserCallSettled); Assert.True(shutdown.Status.BrowserCloseAcknowledged);
        Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.Equal(2500d, shutdown.Status.ProcessExitConfirmationMilliseconds); Assert.Equal(3000d, shutdown.Status.NativeElapsedMilliseconds);
    }
}
