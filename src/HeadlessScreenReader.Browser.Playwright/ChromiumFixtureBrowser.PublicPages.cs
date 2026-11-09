using System.Net;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    /// <summary>The trusted host must verify the independent network fence before calling this API.</summary>
    public static Task<ChromiumFixtureBrowser> LaunchPublicPageAsync(string url, IPAddress proxy,
        PublicTargetPolicy policy, CancellationToken token = default) => LaunchPublicCoreAsync(url, proxy, policy, false, token);

    internal static Task<ChromiumFixtureBrowser> LaunchPublicTaskPageAsync(string url, IPAddress proxy,
        PublicTargetPolicy policy, CancellationToken token) => LaunchPublicCoreAsync(url, proxy, policy, true, token);

    private Func<int> _taskDenialCount = () => 0;
    private Func<int> _taskHttpStatus = () => 0;

    private static async Task<ChromiumFixtureBrowser> LaunchPublicCoreAsync(string url, IPAddress proxy,
        PublicTargetPolicy policy, bool navigationTask, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new BrowserOperationException("ISOLATED_LINUX_REQUIRED");
        if (!policy.AssessUrl(url).Allowed) throw new BrowserOperationException("TARGET_URL_DENIED");
        token.ThrowIfCancellationRequested();
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        IBrowser? browser = null;
        OwnedChromiumProcess? nativeProcess = null;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                ChromiumSandbox = true,
                Timeout = 15_000,
                Proxy = new() { Server = $"http://{proxy}:3128", Bypass = "<-loopback>" },
                Args = ["--disable-quic", "--disable-http2", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp"]
            }).ConfigureAwait(false);
            var context = await browser.NewContextAsync(new()
            {
                AcceptDownloads = false,
                ServiceWorkers = ServiceWorkerPolicy.Block,
                ViewportSize = new() { Width = 1280, Height = 720 }
            }).ConfigureAwait(false);
            var blocked = new HashSet<string>(StringComparer.Ordinal);
            var deniedRequests = 0;
            void Denied(string origin) { Interlocked.Increment(ref deniedRequests); lock (blocked) { if (blocked.Count < 25) blocked.Add(origin); } }
            await context.RouteAsync("**/*", async route =>
            {
                if (navigationTask && route.Request.Method is not ("GET" or "HEAD"))
                {
                    Denied("Non-GET request (navigation task)");
                    await route.AbortAsync().ConfigureAwait(false);
                }
                else if (policy.AssessUrl(route.Request.Url).Allowed) await route.ContinueAsync().ConfigureAwait(false);
                else
                {
                    Denied(Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var target) ? $"{target.Scheme}://{target.IdnHost}" : "unsupported target");
                    await route.AbortAsync().ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            await context.RouteWebSocketAsync("**/*", async route =>
            {
                Denied("WebSocket (unsupported)");
                await route.CloseAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            var documentStatus = 0;
            page.Response += (_, response) => { if (response.Request.IsNavigationRequest && response.Frame == page.MainFrame) Interlocked.Exchange(ref documentStatus, response.Status); };
            context.Page += async (_, popup) =>
            {
                Denied("Additional page (unsupported)");
                try { await popup.CloseAsync().ConfigureAwait(false); }
                catch (PlaywrightException) { }
            };
            page.SetDefaultTimeout(5000);
            var response = await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Load, Timeout = 15_000 }).ConfigureAwait(false);
            if (response?.Status >= 400) throw new BrowserOperationException("TARGET_HTTP_ERROR")
            { Data = { ["HttpStatusCode"] = response.Status } };
            if (!policy.AssessUrl(page.Url).Allowed) throw new BrowserOperationException("TARGET_REDIRECT_DENIED");
            var cdp = await context.NewCDPSessionAsync(page).ConfigureAwait(false);
            await cdp.SendAsync("Accessibility.enable").ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            nativeProcess = await OwnedChromiumProcess.AttachAsync(browser).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new(playwright, browser, context, page, cdp, null, false, new(), nativeProcess, policy, blocked, page.Url)
            { _taskDenialCount = () => Volatile.Read(ref deniedRequests), _taskHttpStatus = () => Volatile.Read(ref documentStatus) };
        }
        catch
        {
            try { if (browser is not null) await browser.CloseAsync().ConfigureAwait(false); }
            finally { try { playwright.Dispose(); } finally { nativeProcess?.Dispose(); } }
            throw;
        }
    }
}
