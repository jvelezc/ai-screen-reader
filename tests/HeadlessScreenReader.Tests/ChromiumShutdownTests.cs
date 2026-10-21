using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ChromiumShutdownTests
{
    [Fact]
    public async Task ReentrantCloseCallback_ObservesTheSameActualShutdownWithoutStartingAnotherContextCall()
    {
        ChromiumShutdown? shutdown = null; Task? reentered = null; var contextCalls = 0;
        shutdown = new(() => { contextCalls++; reentered = shutdown!.CloseAsync(); return Task.CompletedTask; },
            () => Task.CompletedTask, () => { });
        var actual = shutdown.CloseAsync(); await actual;
        Assert.Same(actual, reentered); Assert.Equal(1, contextCalls); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase);
    }
    [Fact]
    public async Task NormalShutdown_AcknowledgesContextThenBrowserDisposesTransportAndSharesActualTask()
    {
        var order = new List<string>();
        var shutdown = new ChromiumShutdown(() => { order.Add("context"); return Task.CompletedTask; },
            () => { order.Add("browser"); return Task.CompletedTask; }, () => order.Add("transport"));
        var first = shutdown.CloseAsync(); Assert.Same(first, shutdown.CloseAsync()); await first;
        Assert.Equal(new[] { "context", "browser", "transport" }, order);
        Assert.Equal(new BrowserShutdownStatus(BrowserShutdownPhase.Closed, 0, true, true, true, true, true, false, null)
        { CollectorCallSettled = true, CollectorStopAcknowledged = true }, shutdown.Status);
    }

    [Fact]
    public async Task StalledContext_EscalatesAtOneSecondAndSettlesEveryActualCallBeforeAcknowledgement()
    {
        var time = new FakeTimeProvider(); var context = Signal(); var browser = Signal(); var browserStarted = Signal(); var transport = false;
        var shutdown = new ChromiumShutdown(() => context.Task, () => { browserStarted.SetResult(); return browser.Task; },
            () => { transport = true; context.TrySetException(new InvalidOperationException("private-context-error")); }, time);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(browserStarted.Task.IsCompleted); Assert.Equal(1, shutdown.Status.ActiveCalls);
        time.Advance(TimeSpan.FromMilliseconds(1)); await browserStarted.Task;
        Assert.True(shutdown.Status.ContextEscalated); Assert.Equal(2, shutdown.Status.ActiveCalls); Assert.False(transport);
        browser.SetResult(); await actual;
        Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.False(shutdown.Status.ContextCloseAcknowledged); Assert.True(shutdown.Status.BrowserCloseAcknowledged);
        Assert.True(shutdown.Status.ContextCallSettled); Assert.True(shutdown.Status.TransportDisposed);
        Assert.DoesNotContain("private-context-error", JsonSerializer.Serialize(shutdown.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextFailure_CannotPreventWholeBrowserCloseOrTransportCleanup(bool synchronous)
    {
        var closed = false; var disposed = false;
        var shutdown = new ChromiumShutdown(() => synchronous ? throw new IOException("private-context") : Task.FromException(new IOException("private-context")),
            () => { closed = true; return Task.CompletedTask; }, () => disposed = true);
        await shutdown.CloseAsync(); Assert.True(closed); Assert.True(disposed);
        Assert.True(shutdown.Status.ContextEscalated); Assert.False(shutdown.Status.ContextCloseAcknowledged);
        Assert.True(shutdown.Status.BrowserCloseAcknowledged); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase);
    }

    [Theory]
    [InlineData("browser-sync")]
    [InlineData("browser-async")]
    [InlineData("transport")]
    public async Task UnconfirmedBrowserOrTransport_FaultsClosedWithSanitizedFacts(string failure)
    {
        var transportCalled = false;
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask,
            () => failure == "browser-sync" ? throw new IOException("private-close") : failure == "browser-async" ? Task.FromException(new IOException("private-close")) : Task.CompletedTask,
            () => { transportCalled = true; if (failure == "transport") throw new IOException("private-close"); });
        var actual = shutdown.CloseAsync(); var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Same(actual, shutdown.CloseAsync()); Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase);
        Assert.Equal(0, shutdown.Status.ActiveCalls); Assert.True(transportCalled);
        Assert.Equal(failure == "transport", shutdown.Status.BrowserCloseAcknowledged);
        Assert.Equal(failure != "transport", shutdown.Status.TransportDisposed);
        Assert.DoesNotContain("private-close", JsonSerializer.Serialize(shutdown.Status));
        Assert.DoesNotContain("private-close", error.ToString());
        Assert.Equal(failure != "transport", shutdown.Status.BrowserCallRejected); Assert.False(shutdown.Status.BrowserDeadlineExceeded);
    }

    [Fact]
    public async Task BrowserDeadline_ClosesTransportButCannotDrainOrRecoverActualLateClose()
    {
        var time = new FakeTimeProvider(); var browser = Signal(); var disposed = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () => disposed.SetResult(), time);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(3999)); Assert.False(disposed.Task.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1)); await disposed.Task;
        Assert.False(actual.IsCompleted); Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase); Assert.Equal(1, shutdown.Status.ActiveCalls);
        Assert.True(shutdown.Status.TransportDisposed); browser.SetResult();
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", error.Code);
        Assert.True(shutdown.Status.BrowserDeadlineExceeded); Assert.False(shutdown.Status.BrowserCallRejected);
        Assert.Contains("\"BrowserDeadlineExceeded\":true", error.InnerException!.Message);
        Assert.Contains("\"Phase\":\"Faulted\"", error.InnerException.Message);
        Assert.Contains("\"NativeElapsedMilliseconds\":4000", error.InnerException.Message);
        Assert.True(shutdown.Status.BrowserCloseAcknowledged); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase);
    }

    [Fact]
    public async Task AcknowledgedBrowserClose_DoesNotReleaseAnUnsettledContextCallOrCancelledWaiter()
    {
        var time = new FakeTimeProvider(); var context = Signal(); var disposed = Signal();
        var shutdown = new ChromiumShutdown(() => context.Task, () => Task.CompletedTask, () => disposed.SetResult(), time);
        var actual = shutdown.CloseAsync(); using var wait = new CancellationTokenSource(); var caller = actual.WaitAsync(wait.Token); wait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => caller);
        time.Advance(TimeSpan.FromSeconds(1)); await disposed.Task;
        Assert.False(actual.IsCompleted); Assert.Equal(1, shutdown.Status.ActiveCalls);
        Assert.True(shutdown.Status.BrowserCloseAcknowledged); Assert.Equal(BrowserShutdownPhase.DrainingCalls, shutdown.Status.Phase);
        context.SetResult(); await actual; Assert.Equal(0, shutdown.Status.ActiveCalls); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase);
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    [InlineData(1000)]
    public async Task SharedNativeBudget_BorrowsUnusedContextTimeAndAcceptsActualBrowserCloseBeforeFourSeconds(int contextMilliseconds)
    {
        var time = new FakeTimeProvider(); var context = Signal(); var browser = Signal(); var started = Signal(); var disposed = false;
        var shutdown = new ChromiumShutdown(() => context.Task, () => { started.SetResult(); return browser.Task; }, () => disposed = true, time);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(contextMilliseconds)); context.SetResult();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(3999 - contextMilliseconds)); Assert.False(disposed); Assert.False(actual.IsCompleted);
        browser.SetResult(); await actual;
        Assert.True(disposed); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.True(shutdown.Status.ContextCloseAcknowledged); Assert.True(shutdown.Status.BrowserCloseAcknowledged);
        Assert.False(shutdown.Status.BrowserDeadlineExceeded); Assert.False(shutdown.Status.BrowserCallRejected);
    }
    [Theory]
    [InlineData(250)]
    [InlineData(1000)]
    public async Task SharedNativeBudget_DoesNotRestartFourSecondsAfterAContextDelayOrRecoverLateClosure(int contextMilliseconds)
    {
        var time = new FakeTimeProvider(); var context = Signal(); var browser = Signal(); var started = Signal(); var disposed = Signal();
        var shutdown = new ChromiumShutdown(() => context.Task, () => { started.SetResult(); return browser.Task; }, () => disposed.SetResult(), time);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(contextMilliseconds)); context.SetResult();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(3999 - contextMilliseconds)); Assert.False(disposed.Task.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1)); await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(actual.IsCompleted); Assert.True(shutdown.Status.BrowserDeadlineExceeded); Assert.Equal(1, shutdown.Status.ActiveCalls);
        browser.SetResult(); var error = await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", error.Code); Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase);
        Assert.True(shutdown.Status.BrowserCloseAcknowledged); Assert.Equal(0, shutdown.Status.ActiveCalls);
    }
    [Fact]
    public async Task ExhaustedNativeBudget_CannotAcknowledgeEvenSynchronouslyCompletedLateCalls()
    {
        var time = new FakeTimeProvider(); var transport = false;
        var shutdown = new ChromiumShutdown(() => { time.Advance(TimeSpan.FromSeconds(4)); return Task.CompletedTask; },
            () => Task.CompletedTask, () => transport = true, time);
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => shutdown.CloseAsync());
        Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", error.Code); Assert.True(transport);
        Assert.True(shutdown.Status.BrowserDeadlineExceeded); Assert.False(shutdown.Status.BrowserCallRejected);
        Assert.True(shutdown.Status.BrowserCloseAcknowledged); Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase);
    }

    [Fact]
    public async Task CollectorFailure_CannotBeErasedByAcknowledgedBrowserShutdown()
    {
        var closed = false; var disposed = false;
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => { closed = true; return Task.CompletedTask; }, () => disposed = true,
            stopCollectors: () => Task.FromException(new IOException("private-collector-error")));
        Assert.Equal("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => shutdown.CloseAsync())).Code);
        Assert.True(closed); Assert.True(disposed); Assert.True(shutdown.Status.BrowserCloseAcknowledged);
        Assert.False(shutdown.Status.CollectorStopAcknowledged); Assert.True(shutdown.Status.CollectorCallSettled);
        Assert.Equal(0, shutdown.Status.ActiveCalls); Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase);
        Assert.DoesNotContain("private-collector-error", JsonSerializer.Serialize(shutdown.Status));
    }

    [Fact]
    public async Task CollectorTimeout_EscalatesNativeCleanupButRetainsActualWriteDrainAndFaultAfterLateAcknowledgement()
    {
        var time = new FakeTimeProvider(); var collector = Signal(); var disposed = Signal();
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => Task.CompletedTask, () => disposed.SetResult(), time,
            stopCollectors: () => collector.Task);
        var actual = shutdown.CloseAsync(); time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.False(disposed.Task.IsCompleted); Assert.Equal(BrowserShutdownPhase.StoppingCollectors, shutdown.Status.Phase);
        time.Advance(TimeSpan.FromMilliseconds(1)); await disposed.Task;
        Assert.False(actual.IsCompleted); Assert.Equal(1, shutdown.Status.ActiveCalls); Assert.True(shutdown.Status.BrowserCloseAcknowledged);
        Assert.Equal(BrowserShutdownPhase.Faulted, shutdown.Status.Phase); collector.SetResult();
        await Assert.ThrowsAsync<BrowserOperationException>(() => actual);
        Assert.True(shutdown.Status.CollectorStopAcknowledged); Assert.Equal(0, shutdown.Status.ActiveCalls);
        Assert.Equal("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", shutdown.Status.ErrorCode);
    }
}
