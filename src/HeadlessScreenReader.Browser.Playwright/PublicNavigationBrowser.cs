using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record PublicTaskMedia(byte[] Video, int DecodedFrames, bool CaptureStopped, bool BrowserClosed);

/// <summary>Fresh anonymous GET-only task context. The independent network fence must already be proven.</summary>
public sealed class PublicNavigationBrowser : IAsyncDisposable
{
    private readonly ChromiumFixtureBrowser _browser;
    private readonly string _videoPath;
    private readonly PublicTaskJob _job;
    private Task? _stopVideo;
    private readonly object _gate = new();
    internal PublicNavigationBrowser(ChromiumFixtureBrowser browser, PublicTaskJob job, string folder)
    { _browser = browser; _job = job; _videoPath = Path.Combine(folder, "original.webm"); }
    public string BrowserVersion => _browser.Environment.BrowserVersion;
    public string PlaywrightVersion => _browser.Environment.PlaywrightVersion;
    public int PolicyDenials => _browser.TaskPolicyDenials;
    public string CurrentUrl => _browser.DiagnosticPage.Url;

    public static async Task<PublicNavigationBrowser> OpenAsync(PublicTaskJob job, IPAddress proxy, string folder, CancellationToken token)
    {
        job.Validate();
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFileSystemEntries(folder).Any()) throw new BrowserOperationException("PUBLIC_TASK_OUTPUT_NOT_EMPTY");
        var browser = await ChromiumFixtureBrowser.LaunchPublicTaskPageAsync(job.Source.TargetUrl, proxy,
            new PublicTargetPolicy([job.Source.AllowedHost]), token).ConfigureAwait(false);
        var owned = new PublicNavigationBrowser(browser, job, folder);
        browser.AttachPublicTaskCollector(owned.StopVideoAsync);
        try
        {
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await browser.DiagnosticPage.Screencast.StartAsync(new()
            {
                Path = owned._videoPath,
                Size = new() { Width = 1280, Height = 720 },
                Quality = 70,
                OnFrame = _ => { first.TrySetResult(); return Task.CompletedTask; }
            }).ConfigureAwait(false);
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            return owned;
        }
        catch { await browser.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken token) => _browser.CaptureAsync(token);
    public Task<PublicTaskFocus?> FocusAsync(CancellationToken token) => _browser.PublicTaskFocusAsync(token);

    private sealed record Link(string Url, string Name, bool Visible);
    private async Task<Link[]> LinksAsync() => (await _browser.DiagnosticPage.Locator("a[href]").EvaluateAllAsync<JsonElement>(
        "as => as.slice(0,500).map(a=>({Url:a.href,Name:(a.getAttribute('aria-label')||a.innerText||'').replace(/\\s+/g,' ').trim().slice(0,500),Visible:a.getClientRects().length>0&&getComputedStyle(a).visibility!=='hidden'&&!a.hasAttribute('download')&&(a.getAttribute('role')===null||a.getAttribute('role')==='link')}))").ConfigureAwait(false)).Deserialize<Link[]>()!;

    public async Task<PublicTaskGoal?> DiscoverAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        foreach (var (kind, title, words) in new[] {
            ("Pricing", "Reach pricing information", new[] { "pricing", "price", "plans" }),
            ("Catalog", "Reach the service or course catalog", new[] { "course", "catalog" }),
            ("Contact", "Reach contact information", new[] { "contact" }),
            ("Help", "Reach help information", new[] { "help", "support", "faq" }) })
            foreach (var link in await LinksAsync().ConfigureAwait(false))
                if (link.Visible && PublicTaskRules.Text(link.Name, 500) && PublicTaskRules.SafeDestination(_job.Source, link.Url)
                    && words.Any(word => (link.Name + " " + new Uri(link.Url).AbsolutePath).Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    var goal = new PublicTaskGoal(kind, title, link.Name, link.Url); goal.Validate(_job.Source); return goal;
                }
        return null;
    }

    public async Task<bool> SameGoalAsync(PublicTaskGoal goal, CancellationToken token)
    {
        goal.Validate(_job.Source); token.ThrowIfCancellationRequested();
        return (await LinksAsync().ConfigureAwait(false)).Any(link => link.Visible && link.Url == goal.DestinationUrl && link.Name == goal.LinkName);
    }

    public async Task PressAsync(string key, PublicTaskGoal goal, CancellationToken token)
    {
        goal.Validate(_job.Source);
        if (key != "Tab" && key != "Enter") throw new BrowserInputDeniedException("PUBLIC_TASK_KEY_DENIED");
        if (key == "Enter" && (await FocusAsync(token).ConfigureAwait(false))?.AnchorUrl != goal.DestinationUrl)
            throw new BrowserInputDeniedException("PUBLIC_TASK_ACTIVATION_DENIED");
        await _browser.PressKeyAsync(key, token).ConfigureAwait(false);
    }

    public async Task PointerAsync(PublicTaskGoal goal, CancellationToken token)
    {
        goal.Validate(_job.Source); token.ThrowIfCancellationRequested();
        var links = await LinksAsync().ConfigureAwait(false);
        var index = Array.FindIndex(links, link => link.Visible && link.Url == goal.DestinationUrl && link.Name == goal.LinkName);
        if (index < 0) throw new BrowserInputDeniedException("PUBLIC_TASK_TARGET_CHANGED");
        await _browser.PublicTaskPointerAsync(index, goal.DestinationUrl, token).ConfigureAwait(false);
    }

    public async Task<bool> GoalObservedAsync(PublicTaskGoal goal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (CurrentUrl != goal.DestinationUrl || _browser.TaskHttpStatus is < 200 or >= 400) return false;
        var snapshot = await CaptureAsync(token).ConfigureAwait(false);
        string[] words = goal.Kind switch
        {
            "Pricing" => ["pric", "plan", "membership", "subscription"],
            "Catalog" => ["course", "catalog", "class", "program", "workshop"],
            "Contact" => ["contact", "reach", "touch", "support"],
            _ => ["help", "support", "faq", "question"]
        };
        return snapshot.Nodes.Any(node => node.Role is "heading" or "Heading"
            && words.Any(word => node.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private Task StopVideoAsync()
    { lock (_gate) return _stopVideo ??= _browser.DiagnosticPage.Screencast.StopAsync(); }

    public async Task<PublicTaskMedia> FinishAsync(CancellationToken token)
    {
        await _browser.EndPublicTaskCaptureAsync().ConfigureAwait(false);
        await _browser.DisposeAsync().ConfigureAwait(false);
        var bytes = await File.ReadAllBytesAsync(_videoPath, token).ConfigureAwait(false);
        if (bytes.Length is < 4 or > 12 * 1024 * 1024 || !bytes.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }))
            throw new BrowserOperationException("PUBLIC_TASK_VIDEO_UNCONFIRMED");
        var frames = await PlaywrightVideoFrames.CountAsync(bytes).ConfigureAwait(false);
        // Decoding proves media presence; synthetic marker pixels are never interpreted as task goals.
        return new(bytes, frames, _browser.CaptureStatus.Phase == CapturePhase.Closed
            && _browser.CaptureStatus.Events.Any(e => e.Phase == CapturePhase.Excluded),
            _browser.ShutdownStatus is
            {
                Phase: BrowserShutdownPhase.Closed, ProcessExitConfirmed: true, ActiveCalls: 0, ErrorCode: null,
                ContextCallSettled: true, BrowserCallSettled: true, CollectorStopAcknowledged: true, TransportDisposed: true,
                TransportCallSettled: true, ProcessCallSettled: true, BrowserDeadlineExceeded: false, BrowserCallRejected: false
            });
    }
    public ValueTask DisposeAsync() => _browser.DisposeAsync();
}

public sealed partial class ChromiumFixtureBrowser
{
    internal int TaskPolicyDenials => _taskDenialCount();
    internal int TaskHttpStatus => _taskHttpStatus();
    internal void AttachPublicTaskCollector(Func<Task> stop) => _stopProbeCollector = stop;
    internal Task<CaptureExclusionLease> EndPublicTaskCaptureAsync() => _captureAdmission.ExcludeAsync(async () =>
    { await _stopProbeCollector().ConfigureAwait(false); await _cdp.SendAsync("Accessibility.disable").ConfigureAwait(false); });

    internal Task<PublicTaskFocus?> PublicTaskFocusAsync(CancellationToken token) => _captureAdmission.RunAsync<PublicTaskFocus?>(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (_publicPolicy is not null && !_publicPolicy.AssessUrl(_page.Url).Allowed) throw new BrowserOperationException("TARGET_SCOPE_CHANGED");
        var snapshot = await CaptureCoreAsync().ConfigureAwait(false);
        var selected = snapshot.FocusedNode;
        var anchor = await _page.EvaluateAsync<string?>("() => document.activeElement?.tagName==='A' ? document.activeElement.href : null").ConfigureAwait(false);
        return selected is { } focus ? new(focus.Id, focus.Role, focus.Name[..Math.Min(focus.Name.Length, 500)], anchor) : null;
    }).WaitAsync(token);

    internal Task PublicTaskPointerAsync(int index, string expected, CancellationToken token) => _captureAdmission.RunAsync(async () =>
    {
        var link = _page.Locator("a[href]").Nth(index);
        if (await link.EvaluateAsync<string>("a => a.href").ConfigureAwait(false) != expected) throw new BrowserInputDeniedException("PUBLIC_TASK_TARGET_CHANGED");
        await link.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
        return true;
    }, input: true).WaitAsync(token);
}
