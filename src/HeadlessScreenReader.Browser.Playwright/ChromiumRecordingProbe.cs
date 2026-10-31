using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record BrowserActionMarker(int Objective, double UnixMilliseconds);
public sealed record RecordingArtifact(string File, long Bytes, string Sha256, int DecodedFrames);
public sealed record CompletedCaptureSource(PreciseCaptureFrame Frame, byte[] Content);
public sealed record RecordingProbeMedia(ImmutableArray<SourceMarkerFrame> Source, ImmutableArray<VideoMarkerFrame> Video,
    ImmutableArray<RecordingArtifact> Artifacts, ImmutableArray<PreciseCaptureFrame> PreciseFrames)
{
    public string SourceClock => "ChromiumCdpJsonDouble";
    public int VideoFramesPerSecond => 25;
    // CDP delivery order can differ from presentation order at startup. Retain
    // raw deliveries and every timestamp; only timing uses this explicit index.
    public ImmutableArray<int> SourcePresentationOrder => Source.IsDefault ? [] : Source.Select((frame, index) => (frame, index))
        .OrderBy(item => item.frame.PresentationUnixMilliseconds).ThenBy(item => item.index).Select(item => item.index).ToImmutableArray();
    public ImmutableArray<SourceMarkerFrame> SourceInPresentationOrder => SourcePresentationOrder.Select(index => Source[index]).ToImmutableArray();
}

/// <summary>Owns a trusted, input-free recording fixture; exposes neither arbitrary script nor general recording.</summary>
public sealed class ChromiumRecordingProbe : IAsyncDisposable
{
    private readonly ChromiumFixtureBrowser _browser;
    private readonly PreciseScreencastSource _source;
    private readonly string _folder;
    private readonly CaptureFrameDrain _exports = new(0, maxBytes: 25 * 1024 * 1024);
    private readonly SemaphoreSlim _copy = new(1, 1);
    private readonly HashSet<int> _copied = [];
    private bool _videoStarted;
    private Task? _stop;

    internal ChromiumRecordingProbe(ChromiumFixtureBrowser browser, PreciseScreencastSource source, string folder)
    { _browser = browser; _source = source; _folder = folder; }

    public IScreenReaderBrowser Browser => _browser;
    public FixtureBrowserEnvironment Environment => _browser.Environment;
    public CaptureStatus CaptureStatus => _browser.CaptureStatus;
    public BrowserShutdownStatus ShutdownStatus => _browser.ShutdownStatus;

    public async Task CopyCompletedSourcesAsync(Func<ImmutableArray<CompletedCaptureSource>, CancellationToken, Task> sink,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        await _copy.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (CaptureStatus.Phase != CapturePhase.Permitted) throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_CAPTURE_EXCLUDED");
            var frames = _source.Frames.Where(frame => !_copied.Contains(frame.Sequence)).ToArray();
            if (frames.Length == 0) return;
            long total = 0;
            foreach (var frame in frames)
            {
                if (frame.File != $"source-{frame.Sequence:D4}.jpg") throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_INVALID");
                var file = new FileInfo(Path.Combine(_folder, frame.File));
                if (!file.Exists || file.LinkTarget is not null || file.Length is < 4 or > 512 * 1024)
                    throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_INVALID");
                total += file.Length;
            }
            Exception? failure = null; var admitted = false;
            await _exports.WriteAsync(0, checked((int)total), async _ =>
            {
                admitted = true;
                try
                {
                    var originals = ImmutableArray.CreateBuilder<CompletedCaptureSource>();
                    foreach (var frame in frames)
                    {
                        var path = Path.Combine(_folder, frame.File);
                        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                            throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_ORIGINAL_CHANGED");
                        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                        if (input.Length is < 4 or > 512 * 1024) throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_ORIGINAL_CHANGED");
                        var bytes = new byte[checked((int)input.Length)];
                        await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                        if (input.ReadByte() != -1) throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_ORIGINAL_CHANGED");
                        if (bytes.Length is < 4 or > 512 * 1024 || !bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })
                            || Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != frame.Sha256)
                            throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_ORIGINAL_CHANGED");
                        originals.Add(new(frame, bytes));
                    }
                    await sink(originals.ToImmutable(), token).ConfigureAwait(false);
                    foreach (var frame in frames) _copied.Add(frame.Sequence);
                }
                catch (Exception exception) { failure = exception; throw; }
            }).ConfigureAwait(false);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if (!admitted || _exports.Status.ErrorCode is not null || CaptureStatus.Phase != CapturePhase.Permitted)
                throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_EXPORT_UNCONFIRMED");
        }
        finally { _copy.Release(); }
    }

    public Task<double> ReadBrowserClockAsync() => _browser.DiagnosticPage.EvaluateAsync<double>(
        "() => performance.timeOrigin + performance.now()");

    public async Task<ImmutableArray<BrowserActionMarker>> ReadActionMarkersAsync()
    {
        var result = await _browser.DiagnosticPage.EvaluateAsync<JsonElement>("() => window.markerEvents").ConfigureAwait(false);
        return result.EnumerateArray().Select(e => new BrowserActionMarker(e.GetProperty("objective").GetInt32(),
            e.GetProperty("unixMilliseconds").GetDouble())).ToImmutableArray();
    }

    internal async Task StartAsync()
    {
        await _browser.DiagnosticPage.SetViewportSizeAsync(320, 240).ConfigureAwait(false);
        await _browser.DiagnosticPage.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))").ConfigureAwait(false);
        await _source.StartAsync().ConfigureAwait(false);
        var firstVideoFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _videoStarted = true;
        _ = await _browser.DiagnosticPage.Screencast.StartAsync(new()
        {
            Path = Path.Combine(_folder, "original.webm"),
            Size = new() { Width = 320, Height = 240 },
            Quality = 90,
            // Readiness only. The SDK's Single timestamp is never used as timing evidence.
            OnFrame = _ => { firstVideoFrame.TrySetResult(); return Task.CompletedTask; }
        }).ConfigureAwait(false);
        await firstVideoFrame.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    internal Task StopCollectorsAsync() => _stop ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        var exports = _exports.CloseAdmission();
        try { await _source.StopAsync().ConfigureAwait(false); }
        finally
        {
            await exports.ConfigureAwait(false);
            if (_videoStarted) await _browser.DiagnosticPage.Screencast.StopAsync().ConfigureAwait(false);
        }
        if (_exports.Status.ErrorCode is not null) throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_EXPORT_UNCONFIRMED");
    }

    public async Task<RecordingProbeMedia> FinishAndInspectAsync()
    {
        await _browser.ExcludeCaptureAsync().ConfigureAwait(false);
        await _browser.DisposeAsync().ConfigureAwait(false);
        var precise = _source.Frames;
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true }).ConfigureAwait(false);
        var page = await browser.NewPageAsync().ConfigureAwait(false);
        await page.RouteAsync("**/*", route => route.AbortAsync()).ConfigureAwait(false);
        var source = ImmutableArray.CreateBuilder<SourceMarkerFrame>();
        ImmutableArray<VideoMarkerFrame> video = [];
        var artifacts = ImmutableArray.CreateBuilder<RecordingArtifact>();
        foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(_folder, file).Replace('\\', '/');
            var extension = Path.GetExtension(file);
            if (extension is not (".webm" or ".jpg")) throw new BrowserOperationException("RECORDING_ARTIFACT_UNRECOGNIZED");
            var bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
            var decoded = await CaptureProbeDecoder.InspectMarkersAsync(page, bytes, extension == ".webm").ConfigureAwait(false);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            artifacts.Add(new(name, bytes.Length, hash, decoded.Length));
            if (name == "original.webm") video = decoded;
            if (extension == ".jpg")
            {
                var frame = precise.SingleOrDefault(f => f.File == name)
                    ?? throw new BrowserOperationException("RECORDING_SOURCE_METADATA_MISSING");
                if (frame.Sha256 != hash) throw new BrowserOperationException("RECORDING_SOURCE_HASH_CHANGED");
                source.Add(new(name, frame.PresentationUnixMilliseconds, decoded.Single().Marker));
            }
        }
        if (source.Count != precise.Length) throw new BrowserOperationException("RECORDING_SOURCE_ARTIFACT_MISSING");
        return new(source.ToImmutable(), video, artifacts.ToImmutable(), precise);
    }

    public ValueTask DisposeAsync() => _browser.DisposeAsync();
}

public sealed partial class ChromiumFixtureBrowser
{
    public static async Task<ChromiumRecordingProbe> LaunchRecordingProbeAsync(string folder, CancellationToken cancellationToken = default)
        => await LaunchControlledRecordingAsync(folder, BundledFixture.RecordingMarkers, cancellationToken).ConfigureAwait(false);

    public static async Task<ChromiumRecordingProbe> LaunchTraversalRecordingAsync(string folder, CancellationToken cancellationToken = default)
        => await LaunchControlledRecordingAsync(folder, BundledFixture.TraversalRecording, cancellationToken).ConfigureAwait(false);

    private static async Task<ChromiumRecordingProbe> LaunchControlledRecordingAsync(string folder, BundledFixture fixture,
        CancellationToken cancellationToken)
    {
        folder = Path.GetFullPath(folder);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new BrowserOperationException("RECORDING_PROBE_FOLDER_NOT_EMPTY");
        Directory.CreateDirectory(folder);
        var browser = await LaunchCoreAsync(false, cancellationToken, fixture, null,
            Path.Combine(folder, "staging")).ConfigureAwait(false);
        try
        {
            var session = await browser._context.NewCDPSessionAsync(browser._page).ConfigureAwait(false);
            var probe = new ChromiumRecordingProbe(browser, new(session, folder), folder);
            browser._stopProbeCollector = probe.StopCollectorsAsync;
            browser._restartProbeCollector = () => Task.FromException(new BrowserOperationException("RECORDING_PROBE_RESUME_UNAVAILABLE"));
            await probe.StartAsync().ConfigureAwait(false);
            return probe;
        }
        catch { await browser.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
