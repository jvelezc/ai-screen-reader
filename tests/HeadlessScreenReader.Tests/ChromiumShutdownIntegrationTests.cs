using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumShutdownIntegrationTests
{
    [Fact]
    public async Task ActualOwnedExitWatch_ConfirmsKernelExitOutsideSharedThreadPoolAndPreservesAnotherBrowser()
    {
        await using var other = await ChromiumFixtureBrowser.LaunchAsync();
        var otherIdentity = await other.CaptureFixtureProcessIdentityAsync();
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        using var watch = await OwnedChromiumProcess.AttachAsync(browser);
        using var independent = System.Diagnostics.Process.GetProcessById(watch.ProcessId);
        _ = independent.Handle;
        Assert.False(watch.Exited.IsCompleted);
        var witness = watch.Exited.ContinueWith(actual =>
            (Succeeded: actual.IsCompletedSuccessfully, Pool: Thread.CurrentThread.IsThreadPoolThread,
                KernelExited: independent.HasExited), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        await browser.CloseAsync();
        var receipt = await witness.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(receipt.Succeeded); Assert.False(receipt.Pool); Assert.True(receipt.KernelExited);
        Assert.Equal(otherIdentity, await other.CaptureFixtureProcessIdentityAsync());
        Assert.NotEmpty((await other.CaptureAsync()).Nodes);
    }

    [Fact]
    public async Task ActualOwnedRootCapability_ConfirmsExitAndLeavesAnotherPrivateBrowserAlive()
    {
        await using var other = await ChromiumFixtureBrowser.LaunchAsync();
        var otherIdentity = await other.CaptureFixtureProcessIdentityAsync();
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
        await page.SetContentAsync("<h1>Separate owned root stop fixture</h1>");
        using var watch = await OwnedChromiumProcess.AttachAsync(browser);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Only the SDK-response callback is fault-injected. Context, retained handle, native stop and OS watch are real.
        var shutdown = new ChromiumShutdown(() => context.CloseAsync(), () => pending.Task,
            () => { playwright.Dispose(); pending.TrySetResult(); }, waitProcessExit: () => watch.Exited, stopOwnedProcess: watch.WindowsRootStop);
        try
        {
            Assert.Equal(OperatingSystem.IsWindows(), watch.WindowsRootStop is not null);
            await shutdown.CloseAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.True(watch.Exited.IsCompletedSuccessfully); Assert.True(shutdown.Status.ProcessExitConfirmed);
            Assert.Equal(OperatingSystem.IsWindows(), shutdown.Status.OwnedProcessStopRequested);
            Assert.Equal(OperatingSystem.IsWindows(), shutdown.Status.OwnedProcessStopAcknowledged);
            Assert.Equal(OperatingSystem.IsWindows(), shutdown.Status.OwnedProcessStopCallSettled);
            Assert.Equal(!OperatingSystem.IsWindows(), shutdown.Status.TransportEscalatedBeforeProcessExit);
            Assert.InRange(shutdown.Status.ProcessExitConfirmationMilliseconds!.Value, 0, 3999.9999);
            Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase); Assert.Equal(0, shutdown.Status.ActiveCalls);
            Assert.Equal(otherIdentity, await other.CaptureFixtureProcessIdentityAsync()); Assert.NotEmpty((await other.CaptureAsync()).Nodes);
        }
        finally { pending.TrySetResult(); await browser.CloseAsync(); }
    }
    [Fact]
    public async Task ActualOwnedDriverStop_ConfirmsChromiumRootExitWithAnInjectedStalledCloseCallback()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
        await page.SetContentAsync("<h1>Owned shutdown fault injection</h1>");
        using var watch = await OwnedChromiumProcess.AttachAsync(browser);
        using var root = System.Diagnostics.Process.GetProcessById(watch.ProcessId);
        _ = root.Handle; // Bind emergency cleanup to this live owned process before shutdown can permit PID reuse.
        // Fault injection replaces only the close callback; the driver, context and OS exit watch are real.
        var pendingResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stops = 0;
        var shutdown = new ChromiumShutdown(() => context.CloseAsync(), () => pendingResponse.Task,
            () => { stops++; playwright.Dispose(); pendingResponse.SetResult(); }, waitProcessExit: () => watch.Exited);
        Assert.False(watch.Exited.IsCompleted);
        try
        {
            await shutdown.CloseAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.True(root.HasExited); Assert.True(watch.Exited.IsCompletedSuccessfully);
            Assert.Equal(1, stops); Assert.Equal(BrowserShutdownPhase.Closed, shutdown.Status.Phase);
            Assert.True(shutdown.Status.TransportEscalatedBeforeProcessExit); Assert.False(shutdown.Status.TransportEscalatedAfterProcessExit);
            Assert.False(shutdown.Status.BrowserCloseAcknowledgedBeforeTransport); Assert.True(shutdown.Status.BrowserCloseAcknowledged);
            Assert.True(shutdown.Status.ProcessExitConfirmed); Assert.True(shutdown.Status.ProcessCallSettled);
            Assert.Equal(0, shutdown.Status.ActiveCalls); Assert.Null(shutdown.Status.ErrorCode);
        }
        finally
        {
            // Only this test-created root is eligible for emergency cleanup if the acceptance witness fails.
            playwright.Dispose(); if (!root.HasExited) root.Kill();
            await watch.Exited.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
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
