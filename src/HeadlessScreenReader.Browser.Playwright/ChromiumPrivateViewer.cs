using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record PrivateViewerAudit(string Phase, int DisplayedNodes, bool PixelsDecoded, bool ContentCleared);
/// <summary>Separate fixed display surface. No network, connection credentials or target-input API.</summary>
public sealed class ChromiumPrivateViewer : IAsyncDisposable, IBrowserShutdownDiagnostics
{
    private readonly object _sync = new();
    private readonly IPage _page;
    private readonly OwnedChromiumProcess _process;
    private readonly ChromiumShutdown _shutdown;
    private readonly HashSet<Task> _renders = [];
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _close;
    private PrivateViewIntent _intent = new(PrivateViewAction.Refresh, 0);
    private ChromiumPrivateViewer(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page, OwnedChromiumProcess process)
    {
        _page = page; _process = process;
        _shutdown = new(() => context.CloseAsync(), () => browser.CloseAsync(), playwright.Dispose, waitProcessExit: () => process.Exited);
        page.Close += (_, _) => _ended.TrySetResult(); browser.Disconnected += (_, _) => _ended.TrySetResult();
        page.FrameNavigated += (_, _) => { lock (_sync) { _intent = new(PrivateViewAction.End, _intent.Revision); _ended.TrySetResult(); } };
    }
    public PrivateViewIntent Intent { get { lock (_sync) return _intent; } }
    public Task Ended => _ended.Task;
    public BrowserShutdownStatus ShutdownStatus => _shutdown.Status;
    public int ProcessId => _process.ProcessId;
    internal IPage DiagnosticPage => _page;
    internal int ActiveRenders { get { lock (_sync) return _renders.Count; } }
    public static async Task<ChromiumPrivateViewer> LaunchAsync(bool headed = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        IBrowser? browser = null; OwnedChromiumProcess? process = null;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = !headed, ChromiumSandbox = true, Timeout = 15000 }).ConfigureAwait(false);
            process = await OwnedChromiumProcess.AttachAsync(browser).ConfigureAwait(false);
            var context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block, ViewportSize = new() { Width = 1024, Height = 800 } }).ConfigureAwait(false);
            await context.RouteAsync("**/*", route => route.AbortAsync()).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false); page.SetDefaultTimeout(5000);
            var viewer = new ChromiumPrivateViewer(playwright, browser, context, page, process);
            await page.ExposeBindingAsync("__viewerIntent", (BindingSource source, string action, int revision) =>
            {
                lock (viewer._sync)
                {
                    if (source.Page != page || source.Frame != page.MainFrame || source.Context != context || viewer._close is not null
                        || viewer._intent.Action == PrivateViewAction.End || revision != viewer._intent.Revision + 1 || revision > 1000000
                        || !Enum.TryParse<PrivateViewAction>(action, out var kind) || !Enum.IsDefined(kind) || kind.ToString() != action) return false;
                    viewer._intent = new(kind, revision);
                    if (kind == PrivateViewAction.End) viewer._ended.TrySetResult();
                    return true;
                }
            }).ConfigureAwait(false);
            await page.SetContentAsync(ReadHtml(), new() { Timeout = 15000 }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); return viewer;
        }
        catch { try { if (browser is not null) await browser.CloseAsync().ConfigureAwait(false); } finally { playwright.Dispose(); process?.Dispose(); } throw; }
    }
    private static string ReadHtml()
    {
        string Read(string name)
        {
            using var stream = typeof(ChromiumPrivateViewer).Assembly.GetManifestResourceStream($"HeadlessScreenReader.Browser.Playwright.PrivateViewer.view.{name}")!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        var css = Read("css"); var script = Read("js");
        static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var csp = $"default-src 'none'; script-src 'sha256-{Hash(script)}'; style-src 'sha256-{Hash(css)}'; img-src blob:; connect-src 'none'; base-uri 'none'; form-action 'none'; frame-src 'none'";
        return Read("html").Replace("@@CSP@@", csp, StringComparison.Ordinal).Replace("@@STYLE@@", css, StringComparison.Ordinal).Replace("@@SCRIPT@@", script, StringComparison.Ordinal);
    }
    public Task<bool> RenderAsync(int revision, PrivateViewPhase phase, PrivateViewContent? content = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(phase) || (phase == PrivateViewPhase.Viewing) != (content is not null)) throw new ArgumentException("VIEW_CLIENT_RENDER_INVALID");
        lock (_sync)
        {
            if (_close is not null) throw new BrowserOperationException("VIEW_CLIENT_CLOSED");
            if (_ended.Task.IsCompleted || _page.Url != "about:blank") return Task.FromResult(false);
            var actual = RenderCoreAsync(revision, phase, content); _renders.Add(actual);
            _ = actual.ContinueWith(task => { _ = task.Exception; lock (_sync) _renders.Remove(task); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return actual.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
    private async Task<bool> RenderCoreAsync(int revision, PrivateViewPhase phase, PrivateViewContent? content)
    {
        try
        {
            var rendered = await _page.EvaluateAsync<bool>("data => window.__renderView(data)", new
            {
                Revision = revision,
                Phase = phase.ToString(),
                Message = phase switch
                {
                    PrivateViewPhase.Viewing => "Read-only view. Pixels and semantics are separate observations.",
                    PrivateViewPhase.Excluded => "Capture is unavailable during an excluded interval. Displayed content cleared.",
                    PrivateViewPhase.Ended => "Viewing ended or access denied. Displayed content cleared.",
                    PrivateViewPhase.Unavailable => "The refresh failed. Displayed content cleared; use Refresh to retry.",
                    _ => "Waiting for a permitted observation."
                },
                Epoch = content?.Epoch,
                Ordinal = content?.ObservationOrdinal,
                Baseline = content?.PixelBaselineOrdinal,
                Lines = content?.Lines.ToArray() ?? [],
                Truncated = content?.Truncated ?? false,
                Assisted = content?.PriorSimulatedAssistance ?? false,
                Png = content is null ? "" : Convert.ToBase64String(content.Png.AsSpan())
            }).ConfigureAwait(false);
            if (rendered && phase == PrivateViewPhase.Ended)
            { lock (_sync) { _intent = new(PrivateViewAction.End, _intent.Revision); _ended.TrySetResult(); } }
            return rendered;
        }
        catch { throw new BrowserOperationException("VIEW_CLIENT_RENDER_UNCONFIRMED"); }
    }
    public async Task ActivateControlAsync(PrivateViewAction action)
    {
        var id = action switch { PrivateViewAction.Pause or PrivateViewAction.Resume => "pause", PrivateViewAction.Refresh => "refresh", PrivateViewAction.End => "end", _ => throw new ArgumentOutOfRangeException(nameof(action)) };
        await _page.Locator("#" + id).ClickAsync().ConfigureAwait(false);
    }
    public async Task<PrivateViewerAudit> ObserveAuditAsync()
    {
        var data = await _page.EvaluateAsync<JsonElement>("""
        () => { const image = document.getElementById('pixels'); const nodes = document.getElementById('nodes').children.length;
          return { Phase: document.getElementById('state').textContent, DisplayedNodes: nodes,
            PixelsDecoded: !image.hidden && image.complete && image.naturalWidth === 320 && image.naturalHeight === 240,
            ContentCleared: image.hidden && !image.hasAttribute('src') && nodes === 0 }; }
        """).ConfigureAwait(false);
        return new(data.GetProperty("Phase").GetString()!, data.GetProperty("DisplayedNodes").GetInt32(), data.GetProperty("PixelsDecoded").GetBoolean(), data.GetProperty("ContentCleared").GetBoolean());
    }
    public ValueTask DisposeAsync() { lock (_sync) return new(_close ??= CloseAsync()); }
    private async Task CloseAsync()
    {
        _ended.TrySetResult();
        try { await _shutdown.CloseAsync().ConfigureAwait(false); }
        finally
        {
            Task[] pending; lock (_sync) pending = [.. _renders];
            try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { /* Joined actual render faults are already sanitized. */ }
            _process.Dispose();
        }
    }
}
