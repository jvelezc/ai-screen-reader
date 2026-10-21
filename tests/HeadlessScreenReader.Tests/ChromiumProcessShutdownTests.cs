using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ChromiumProcessShutdownTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedProcessExit_CannotEraseSdkRejectionBeforeOrDuringTransportCleanup(bool duringTransport)
    {
        var process = Signal(); var browser = Signal(); var started = Signal(); var disposed = false;
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () =>
        {
            started.SetResult(); return duringTransport ? browser.Task : Task.FromException(new IOException("private-sdk-rejection"));
        }, () => { disposed = true; if (duringTransport) browser.SetException(new IOException("private-sdk-rejection")); else process.SetResult(); },
            waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (duringTransport) process.SetResult();
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", error.Code); Assert.True(disposed); Assert.True(shutdown.Status.ProcessExitConfirmed);
        Assert.True(shutdown.Status.BrowserCallRejected); Assert.False(shutdown.Status.BrowserCloseAcknowledged); Assert.False(shutdown.Status.BrowserDeadlineExceeded);
        Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.DoesNotContain("private-sdk-rejection", error.ToString());
    }
    [Fact]
    public async Task VerifiedProcessExit_StopsOwnedTransportAndJoinsThePendingSdkResponse()
    {
        var process = Signal(); var browser = Signal(); var started = Signal(); var transport = false;
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => { started.SetResult(); return browser.Task; },
            () => { transport = true; browser.SetResult(); }, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(transport); Assert.Equal(2, shutdown.Status.ActiveCalls); process.SetResult(); await actual;
        Assert.True(transport); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.True(shutdown.Status.ProcessCallSettled);
        Assert.True(shutdown.Status.TransportEscalatedAfterProcessExit); Assert.False(shutdown.Status.BrowserCloseAcknowledgedBeforeTransport);
        Assert.True(shutdown.Status.BrowserCloseAcknowledged); Assert.False(shutdown.Status.BrowserDeadlineExceeded);
    }
    [Fact]
    public async Task SdkAcknowledgement_StillRequiresActualProcessExitAndCannotDrainACancelledWait()
    {
        var process = Signal(); var acknowledged = Signal(); var transport = false;
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => { acknowledged.SetResult(); return Task.CompletedTask; },
            () => transport = true, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); await acknowledged.Task;
        using var caller = new CancellationTokenSource(); var waiting = actual.WaitAsync(caller.Token); caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(transport); Assert.False(actual.IsCompleted); Assert.Equal(1, shutdown.Status.ActiveCalls);
        Assert.True(shutdown.Status.BrowserCloseAcknowledged); Assert.False(shutdown.Status.ProcessExitConfirmed);
        process.SetResult(); await actual;
        Assert.True(transport); Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.True(shutdown.Status.BrowserCloseAcknowledgedBeforeTransport); Assert.False(shutdown.Status.TransportEscalatedAfterProcessExit);
    }
    [Fact]
    public async Task MissingProcessExit_FailsAtTheSharedDeadlineAndLateExitCannotRecover()
    {
        var time = new FakeTimeProvider(); var process = Signal(); var disposed = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () => disposed.SetResult(), time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(3999)); Assert.False(disposed.Task.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1)); await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(actual.IsCompleted); Assert.Equal(1, shutdown.Status.ActiveCalls); Assert.True(shutdown.Status.BrowserDeadlineExceeded);
        Assert.False(shutdown.Status.ProcessExitConfirmed); process.SetResult();
        Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => actual)).Code);
        Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.Equal(0, shutdown.Status.ActiveCalls); Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase);
        Assert.Equal(4000d, shutdown.Status.NativeElapsedMilliseconds);
    }
    [Fact]
    public async Task NeitherProcessNorSdkAcknowledges_TimeoutStillJoinsBothActualCalls()
    {
        var time = new FakeTimeProvider(); var process = Signal(); var browser = Signal(); var disposed = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () => disposed.SetResult(), time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromSeconds(4)); await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(actual.IsCompleted); Assert.Equal(2, shutdown.Status.ActiveCalls); browser.SetResult();
        Assert.False(actual.IsCompleted); process.SetResult(); await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal(0, shutdown.Status.ActiveCalls); Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase);
        Assert.True(shutdown.Status.BrowserDeadlineExceeded); Assert.True(shutdown.Status.ProcessCallSettled);
    }
    [Fact]
    public async Task FailedProcessWatch_CannotBeReplacedBySdkAcknowledgementOrLeakNativeError()
    {
        var transport = false; var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask,
            () => transport = true, waitProcessExit: () => Task.FromException(new IOException("private-watch-error")));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => shutdown.CloseAsync());
        Assert.Equal("CHROMIUM_PROCESS_EXIT_UNCONFIRMED", error.Code); Assert.True(transport);
        Assert.True(shutdown.Status.ProcessCallSettled); Assert.False(shutdown.Status.ProcessExitConfirmed); Assert.True(shutdown.Status.BrowserCloseAcknowledged);
        Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase); Assert.DoesNotContain("private-watch-error", error.ToString());
    }
    [Fact]
    public async Task VerifiedProcessExit_DoesNotReleasePendingContextUntilTransportSettlesItsCallback()
    {
        var time = new FakeTimeProvider(); var process = Signal(); var context = Signal(); var browser = Signal(); var disposed = false;
        var shutdown = new ChromiumShutdown(() => context.Task, () => browser.Task,
            () => { disposed = true; context.SetException(new IOException("private-context-interrupted")); browser.SetResult(); }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync(); process.SetResult(); time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(disposed); Assert.False(actual.IsCompleted); time.Advance(TimeSpan.FromMilliseconds(1)); await actual;
        Assert.True(disposed); Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.True(shutdown.Status.ContextEscalated);
        Assert.False(shutdown.Status.ContextCloseAcknowledged); Assert.True(shutdown.Status.ContextCallSettled);
        Assert.True(shutdown.Status.TransportEscalatedAfterProcessExit); Assert.Equal(0, shutdown.Status.ActiveCalls); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase);
    }
}
