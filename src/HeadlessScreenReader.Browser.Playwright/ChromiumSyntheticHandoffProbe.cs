using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record SyntheticHandoffMediaAudit(bool Verified, bool PositiveControlDetected, bool CanaryTextAbsent,
    ImmutableArray<ProbeSegment> Segments, ImmutableArray<ProbeArtifact> Artifacts, string? ErrorCode);

/// <summary>Opt-in fixed fixture only. A diagnostic positive-control screenshot stays in memory and is never an artifact.</summary>
public sealed class ChromiumSyntheticHandoffProbe : IAsyncDisposable
{
    private readonly ChromiumFixtureBrowser _browser;
    private readonly string _folder;
    private readonly List<ProbeSegment> _segments = [];
    private CaptureProbeSegment? _current;
    private Task? _stop;
    private bool _positive;
    internal ChromiumSyntheticHandoffProbe(ChromiumFixtureBrowser browser, string folder) { _browser = browser; _folder = folder; }
    public ChromiumFixtureBrowser Browser => _browser;
    public FixtureBrowserEnvironment Environment => _browser.Environment;

    internal async Task StartSegmentAsync()
    {
        if (_segments.Count > 1 || _current is not null && _stop?.IsCompletedSuccessfully != true)
            throw new BrowserOperationException("SYNTHETIC_SEGMENT_BOUNDARY_INVALID");
        _current = await CaptureProbeSegment.StartAsync(_browser.DiagnosticPage, _folder, $"segment-{_segments.Count + 1}").ConfigureAwait(false);
        _stop = null;
        await _current.WaitForFramesAsync().ConfigureAwait(false);
    }
    internal Task StopSegmentAsync() => _stop ??= StopCoreAsync();
    private async Task StopCoreAsync()
    {
        if (_current is null) return;
        await _current.StopAsync().ConfigureAwait(false);
        _segments.Add(_current.Receipt);
    }

    public async Task<bool> ObserveInMemoryPositiveControlAsync()
    {
        ChromiumFixtureBrowser.EnsureSyntheticDiagnosticsDisabled();
        if (_browser.CaptureStatus.Phase != CapturePhase.Excluded || !_browser.SyntheticStatus.CanaryMayRemain || _stop?.IsCompletedSuccessfully != true)
            throw new BrowserOperationException("SYNTHETIC_POSITIVE_CONTROL_REQUIRES_GAP");
        var present = await _browser.DiagnosticPage.EvaluateAsync<bool>(
            "expected => document.querySelector('input')?.value === expected && document.querySelector('h1')?.textContent === expected",
            _browser.SyntheticCanaryForAudit).ConfigureAwait(false);
        var bytes = await _browser.DiagnosticPage.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg }).ConfigureAwait(false);
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var decoder = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true }).ConfigureAwait(false);
        var page = await decoder.NewPageAsync().ConfigureAwait(false);
        await page.RouteAsync("**/*", route => route.AbortAsync()).ConfigureAwait(false);
        var pixels = await CaptureProbeDecoder.InspectAsync(page, bytes, video: false).ConfigureAwait(false);
        return _positive = present && pixels.CanaryPixels > 320 * 240 / 2;
    }

    public async Task<SyntheticHandoffMediaAudit> InspectClosedEvidenceAsync(string evidenceRoot, CancellationToken cancellationToken = default)
    {
        ChromiumFixtureBrowser.EnsureSyntheticDiagnosticsDisabled();
        if (_browser.CaptureStatus.Phase != CapturePhase.Closed || _stop?.IsCompletedSuccessfully != true)
            throw new BrowserOperationException("SYNTHETIC_AUDIT_REQUIRES_CLOSED_CONTEXT");
        evidenceRoot = Path.GetFullPath(evidenceRoot);
        if (!_folder.StartsWith(evidenceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new BrowserOperationException("SYNTHETIC_AUDIT_ROOT_INVALID");
        var artifacts = ImmutableArray.CreateBuilder<ProbeArtifact>();
        string? error = null;
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var decoder = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true }).ConfigureAwait(false);
        var page = await decoder.NewPageAsync().ConfigureAwait(false);
        await page.RouteAsync("**/*", route => route.AbortAsync()).ConfigureAwait(false);
        foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(file);
            if (extension is not (".webm" or ".jpg")) throw new BrowserOperationException("SYNTHETIC_MEDIA_UNRECOGNIZED");
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            var pixels = await CaptureProbeDecoder.InspectAsync(page, bytes, extension == ".webm").ConfigureAwait(false);
            artifacts.Add(new(Path.GetRelativePath(_folder, file).Replace('\\', '/'), bytes.Length,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), pixels));
        }
        var textAbsent = true;
        var canary = Encoding.UTF8.GetBytes(_browser.SyntheticCanaryForAudit);
        foreach (var file in Directory.EnumerateFiles(evidenceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            if (bytes.AsSpan().IndexOf(canary) >= 0) textAbsent = false;
        }
        if (!_positive || !textAbsent || artifacts.Count == 0 || artifacts.Any(a => a.Pixels.CanaryPixels != 0))
            error = "SYNTHETIC_CANARY_LEAK_OR_MISSING_CONTROL";
        if (_segments.Count != 2 || _segments.Any(s => s.ErrorCode is not null || s.Frames.Length < 3)
            || _segments.SelectMany(s => s.Frames).Any(f => !artifacts.Any(a => a.File == f.File))
            || !artifacts.Any(a => a.File == "segment-1.webm") || !artifacts.Any(a => a.File == "segment-2.webm"))
            error ??= "SYNTHETIC_MEDIA_INCOMPLETE";
        return new(error is null, _positive, textAbsent, [.. _segments], artifacts.ToImmutable(), error);
    }
    public ValueTask DisposeAsync() => _browser.DisposeAsync();
}

public sealed partial class ChromiumFixtureBrowser
{
    public static async Task<ChromiumSyntheticHandoffProbe> LaunchSyntheticHandoffProbeAsync(string folder, bool headed = false,
        CancellationToken cancellationToken = default)
    {
        EnsureSyntheticDiagnosticsDisabled();
        folder = Path.GetFullPath(folder);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new BrowserOperationException("SYNTHETIC_MEDIA_FOLDER_NOT_EMPTY");
        Directory.CreateDirectory(folder);
        var browser = await LaunchCoreAsync(headed, cancellationToken, BundledFixture.SyntheticHandoff, null,
            Path.Combine(folder, "staging")).ConfigureAwait(false);
        try
        {
            var probe = new ChromiumSyntheticHandoffProbe(browser, folder);
            browser._stopProbeCollector = probe.StopSegmentAsync;
            browser._restartProbeCollector = probe.StartSegmentAsync;
            await probe.StartSegmentAsync().ConfigureAwait(false);
            return probe;
        }
        catch { await browser.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
