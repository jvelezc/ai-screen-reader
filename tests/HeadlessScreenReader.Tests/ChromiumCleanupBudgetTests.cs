using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ChromiumCleanupBudgetTests
{
    [Theory]
    [InlineData(2500, false)]
    [InlineData(2999, false)]
    [InlineData(3000, true)]
    public async Task DriverCleanup_ReservesThreeSecondsWithoutExtendingTheActualExitDeadline(int cleanupMilliseconds, bool late)
    {
        var time = new FakeTimeProvider();
        var process = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdown = new ChromiumShutdown(() => Task.CompletedTask, () => browser.Task, () =>
        {
            time.Advance(TimeSpan.FromMilliseconds(cleanupMilliseconds));
            process.SetResult(); browser.SetResult(); stopped.SetResult();
        }, time, waitProcessExit: () => process.Task);
        var actual = shutdown.CloseAsync();
        try
        {
            time.Advance(TimeSpan.FromMilliseconds(999));
            Assert.False(shutdown.Status.TransportEscalatedBeforeProcessExit);
            time.Advance(TimeSpan.FromMilliseconds(1));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (late)
                Assert.Equal("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => actual)).Code);
            else await actual;
            Assert.Equal(1000d + cleanupMilliseconds, shutdown.Status.ProcessExitConfirmationMilliseconds);
            Assert.Equal(late, shutdown.Status.BrowserDeadlineExceeded);
            Assert.Equal(late ? BrowserShutdownPhase.Faulted : BrowserShutdownPhase.Closed, shutdown.Status.Phase);
            Assert.True(shutdown.Status.TransportEscalatedBeforeProcessExit);
            Assert.True(shutdown.Status.TransportCallSettled); Assert.True(shutdown.Status.BrowserCallSettled);
            Assert.True(shutdown.Status.ProcessCallSettled); Assert.Equal(0, shutdown.Status.ActiveCalls);
        }
        finally
        {
            // Also join the original operation when this regression is run against the old two-second grace.
            time.Advance(TimeSpan.FromSeconds(1));
            try { await actual.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (BrowserOperationException) { }
        }
    }
}
