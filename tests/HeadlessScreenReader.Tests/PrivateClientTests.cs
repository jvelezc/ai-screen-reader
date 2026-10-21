using System.Collections.Immutable;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class PrivateClientTests
{
    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(1, 0, 0, false)]
    [InlineData(128, 512, 80, false)]
    [InlineData(129, 513, 81, true)]
    public void Presentation_BoundsNodesAndTextWithoutRenderingMarkupOrInventingFocus(int nodes, int nameLength, int roleLength, bool truncated)
    {
        var snapshot = new AccessibilitySnapshot("doc", [.. Enumerable.Range(0, nodes).Select(i => new AccessibleNode(i.ToString(), new string('r', roleLength), new string('x', nameLength)))], []) { CaptureEpoch = 1 };
        var view = PrivateViewContent.Create(snapshot, 3, 0, PrivatePixelTests.Envelope(), false);
        Assert.Equal(Math.Min(nodes, 128), view.Lines.Length); Assert.Equal(truncated, view.Truncated);
        var expectedRole = new string('r', Math.Min(roleLength, 80)) + (roleLength > 80 ? "…" : "");
        var expectedName = new string('x', Math.Min(nameLength, 512)) + (nameLength > 512 ? "…" : "");
        Assert.All(view.Lines, line => Assert.Equal(expectedRole + ": " + expectedName, line));
        Assert.DoesNotContain(view.Lines, line => line.Contains("[focused]", StringComparison.Ordinal)); Assert.Equal(3, view.ObservationOrdinal);
    }
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    [InlineData(1, 0, 1)]
    public void Presentation_RejectsUnboundEpochAndOrdinals(long epoch, long ordinal, long baseline)
    {
        var snapshot = new AccessibilitySnapshot("doc", [], []) { CaptureEpoch = epoch };
        Assert.Equal("VIEW_CLIENT_CONTENT_INVALID", Assert.Throws<BrowserOperationException>(() => PrivateViewContent.Create(snapshot, ordinal, baseline, PrivatePixelTests.Envelope(), false)).Code);
    }
    [Fact]
    public async Task ActualViewer_UsesTextNodesClearsOnKeyboardPauseAndRejectsLateRender()
    {
        await using var viewer = await ChromiumPrivateViewer.LaunchAsync(false);
        var page = viewer.DiagnosticPage; var errors = new List<string>(); page.PageError += (_, text) => errors.Add(text);
        var snapshot = new AccessibilitySnapshot("doc", [new("n", "button", "<img src=x onerror=alert(1)>", IsFocused: true)], []) { CaptureEpoch = 1 };
        var content = PrivateViewContent.Create(snapshot, 1, 0, PrivatePixelTests.Envelope(), false);
        Assert.True(await viewer.RenderAsync(0, PrivateViewPhase.Viewing, content));
        Assert.Equal("button: <img src=x onerror=alert(1)> [focused]", await page.Locator("#nodes li").InnerTextAsync());
        Assert.Equal(0, await page.Locator("#nodes img").CountAsync());
        await page.EvaluateAsync("() => { window.liveStateChanges = 0; new MutationObserver(() => window.liveStateChanges++).observe(document.getElementById('state'), { childList: true }); }");
        Assert.True(await viewer.RenderAsync(0, PrivateViewPhase.Viewing, content));
        Assert.Equal(0, await page.EvaluateAsync<int>("window.liveStateChanges"));
        await page.Locator("#pause").FocusAsync(); await page.Keyboard.PressAsync("Enter");
        await PrivateClientDemo.UntilAsync(() => Task.FromResult(viewer.Intent.Action == PrivateViewAction.Pause));
        Assert.True((await viewer.ObserveAuditAsync()).ContentCleared);
        Assert.False(await viewer.RenderAsync(0, PrivateViewPhase.Viewing, content));
        Assert.False(await viewer.RenderAsync(1, PrivateViewPhase.Viewing, content));
        Assert.Equal("pause", await page.EvaluateAsync<string>("document.activeElement.id")); Assert.Empty(errors);
    }
    [Fact]
    public async Task ActualNativeClient_ComposesScopedHttpAndSeparateChromiumWithoutTargetInput()
    {
        await using var target = await ChromiumFixtureBrowser.LaunchPrivatePixelFixtureAsync();
        await using var control = new ControlledScanSession(target, new OwnedScanSessionTests.RecordingStore());
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(2)); await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var scope = new ViewerScope(control.Status.ScanId, control.SessionId); var subject = Guid.NewGuid(); var identity = new ProcessViewerIdentity(subject);
        await using var broker = new PrivateViewerBroker(scope, subject);
        var bootstrap = broker.IssueBootstrap(subject, scope, TimeSpan.FromMinutes(1));
        await using var server = await PrivateViewerServer.StartAsync(identity, new PrivateViewerService(broker, control), pixels: new PrivatePixelViewerService(broker, control));
        using var client = new PrivateViewerHttpClient(server.BaseAddress, scope, identity.Credential.Value);
        await client.ExchangeAsync(bootstrap.Secret.Value);
        var content = await client.ReadAsync(1); Assert.NotEmpty(content.Lines); Assert.Equal(1, content.Epoch);
        await using var viewer = await ChromiumPrivateViewer.LaunchAsync(false);
        var before = await target.ReadPixelFixtureStateAsync();
        Assert.True(await new PrivateViewerRefresh(client, control, viewer).RefreshAsync());
        await PrivateClientDemo.UntilAsync(async () => (await viewer.ObserveAuditAsync()).PixelsDecoded);
        Assert.Equal(before, await target.ReadPixelFixtureStateAsync());
        var page = viewer.DiagnosticPage;
        foreach (var width in new[] { 1024, 320 })
        {
            await page.SetViewportSizeAsync(width, 800);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
            Assert.True(await page.Locator("#pause").IsVisibleAsync());
            var folder = Environment.GetEnvironmentVariable("HSR_PRIVATE_CLIENT_QA");
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                await page.ScreenshotAsync(new() { Path = Path.Combine(folder, width == 1024 ? "desktop.png" : "mobile.png"), FullPage = true });
            }
        }
        Assert.DoesNotContain(identity.Credential.Value, await page.ContentAsync());
        Assert.DoesNotContain(bootstrap.Secret.Value, await page.ContentAsync());
        Assert.Equal("about:blank", page.Url);
    }
    [Fact]
    public async Task ActualViewer_RejectsForgedBindingAndNetworkThenEndsOnNavigation()
    {
        await using var viewer = await ChromiumPrivateViewer.LaunchAsync(false); var page = viewer.DiagnosticPage;
        Assert.False(await page.EvaluateAsync<bool>("() => window.__viewerIntent('Pause', 2)"));
        Assert.False(await page.EvaluateAsync<bool>("() => window.__viewerIntent('1', 1)")); Assert.Equal(0, viewer.Intent.Revision);
        var requests = new List<string>(); page.Request += (_, request) => requests.Add(request.Url);
        Assert.False(await page.EvaluateAsync<bool>("async () => { try { await fetch('http://127.0.0.1:1/probe'); return true; } catch { return false; } }"));
        Assert.Empty(requests);
        await page.GotoAsync("data:text/html,<title>Untrusted</title>"); await viewer.Ended.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PrivateViewAction.End, viewer.Intent.Action);
        Assert.False(await viewer.RenderAsync(0, PrivateViewPhase.Unavailable));
    }
    [Fact]
    public async Task ActualViewer_CancelledWaitDoesNotDrainPendingRenderAndCloseJoinsItsActualCallback()
    {
        var viewer = await ChromiumPrivateViewer.LaunchAsync(false);
        try
        {
            await viewer.DiagnosticPage.EvaluateAsync("() => { window.__renderView = () => new Promise(() => {}); }");
            using var cancelled = new CancellationTokenSource(); var waiting = viewer.RenderAsync(0, PrivateViewPhase.Loading, cancellationToken: cancelled.Token);
            cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting); Assert.Equal(1, viewer.ActiveRenders);
            await viewer.DisposeAsync(); Assert.Equal(0, viewer.ActiveRenders); Assert.Equal(BrowserShutdownPhase.Closed, viewer.ShutdownStatus.Phase);
            Assert.True(viewer.ShutdownStatus.ProcessExitConfirmed);
            await Assert.ThrowsAsync<BrowserOperationException>(() => viewer.RenderAsync(0, PrivateViewPhase.Loading));
        }
        finally { await viewer.DisposeAsync(); }
    }
    [Fact]
    public async Task ActualComposedProof_PauseResumeGapAndRevocationPreserveTargetAndCloseBothRoots()
    {
        var proof = await PrivateClientDemo.ExecuteAsync();
        Assert.True(proof.Ready, proof.ErrorCode); Assert.Equal("completed", proof.Stage);
        Assert.True(proof.PixelsDecoded); Assert.True(proof.PauseClearedWithoutRead); Assert.True(proof.ResumeRendered);
        Assert.True(proof.ExcludedCleared); Assert.True(proof.RevokedCleared); Assert.True(proof.TargetUnchanged); Assert.True(proof.BrowserCloseConfirmed);
        Assert.False(proof.HumanAcceptancePerformed); Assert.False(proof.PublicListenerEnabled);
    }
}
