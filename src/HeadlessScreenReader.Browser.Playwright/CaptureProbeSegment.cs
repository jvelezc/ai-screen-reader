using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record ProbeFrame(int Sequence, string File, double ReportedPresentationUnixMilliseconds, int ViewportWidth, int ViewportHeight);
public sealed record ProbeSegment(string Video, ImmutableArray<ProbeFrame> Frames, int DroppedStaleFrames,
    int ReceivedCallbacks, double? LastReportedPresentationUnixMilliseconds, double TimestampUncertaintyMilliseconds, string? ErrorCode);

internal sealed class CaptureProbeSegment(IPage page, string folder, string name)
{
    // Playwright 1.63 reports epoch milliseconds as Single. Do not use that quantized
    // value to admit frames across epochs. Segment callback admission is permanent.
    private readonly CaptureFrameDrain _drain = new(0);
    private readonly object _sync = new();
    private readonly List<ProbeFrame> _frames = [];
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _stop;
    private int _callbacks;
    private double? _lastTimestamp;
    private double _uncertainty;

    public static async Task<CaptureProbeSegment> StartAsync(IPage page, string folder, string name)
    {
        var segment = new CaptureProbeSegment(page, folder, name);
        var video = Path.Combine(folder, name + ".webm");
        if (File.Exists(video)) throw new IOException("CAPTURE_ARTIFACT_EXISTS");
        _ = await page.Screencast.StartAsync(new()
        {
            Path = video,
            Size = new() { Width = 320, Height = 240 },
            Quality = 90,
            OnFrame = segment.OnFrameAsync
        }).ConfigureAwait(false);
        return segment;
    }

    public Task WaitForFramesAsync() => _ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

    public ProbeSegment Receipt
    {
        get
        {
            lock (_sync) return new(name + ".webm", [.. _frames.OrderBy(f => f.Sequence)], _drain.Status.DroppedStale,
                _callbacks, _lastTimestamp, _uncertainty, _drain.Status.ErrorCode);
        }
    }

    public Task StopAsync()
    {
        lock (_sync) return _stop ??= StopCoreAsync();
    }

    private Task OnFrameAsync(ScreencastFrame frame)
    {
        lock (_sync)
        {
            _callbacks++;
            _lastTimestamp = frame.Timestamp;
            _uncertainty = Math.Max(_uncertainty, (double)MathF.BitIncrement(frame.Timestamp) - frame.Timestamp);
        }
        return _drain.WriteAsync(frame.Timestamp, frame.Data.Length, async sequence =>
        {
            var file = $"{name}-frame-{sequence:D4}.jpg";
            await using (var stream = new FileStream(Path.Combine(folder, file), FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 4096, useAsync: true))
                await stream.WriteAsync(frame.Data).ConfigureAwait(false);
            lock (_sync)
            {
                _frames.Add(new(sequence, file, frame.Timestamp, frame.ViewportWidth, frame.ViewportHeight));
                if (_frames.Count >= 3) _ready.TrySetResult();
            }
        });
    }

    private async Task StopCoreAsync()
    {
        var drain = _drain.CloseAdmission();
        await page.Screencast.StopAsync().ConfigureAwait(false);
        await drain.ConfigureAwait(false);
        if (_drain.Status.ErrorCode is { } code) throw new BrowserOperationException(code);
    }
}
