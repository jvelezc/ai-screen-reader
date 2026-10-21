using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumShutdownIntegrationTests
{
    [Fact]
    public async Task RepeatedChromiumDispose_JoinsOneActualShutdownAndRetainsSanitizedAcknowledgements()
    {
        var browser = await ChromiumFixtureBrowser.LaunchAsync(); await browser.CaptureAsync();
        using var processWatch = await OwnedChromiumProcess.AttachAsync(browser.DiagnosticPage.Context.Browser!);
        Assert.Equal((await browser.CaptureFixtureProcessIdentityAsync()).BrowserProcessId, processWatch.ProcessId);
        Assert.False(processWatch.Exited.IsCompleted);
        var first = browser.DisposeAsync().AsTask(); var second = browser.DisposeAsync().AsTask();
        Assert.Same(first, second); await first;
        Assert.Equal(BrowserShutdownPhase.Closed, browser.ShutdownStatus.Phase);
        Assert.Equal(0, browser.ShutdownStatus.ActiveCalls); Assert.True(browser.ShutdownStatus.BrowserCloseAcknowledged);
        Assert.True(browser.ShutdownStatus.ContextCallSettled); Assert.True(browser.ShutdownStatus.BrowserCallSettled);
        Assert.True(browser.ShutdownStatus.TransportDisposed); Assert.Null(browser.ShutdownStatus.ErrorCode);
        Assert.True(browser.ShutdownStatus.ProcessExitConfirmed); Assert.True(browser.ShutdownStatus.ProcessCallSettled);
        await processWatch.Exited.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CapturePhase.Closed, browser.CaptureStatus.Phase);
    }
}
