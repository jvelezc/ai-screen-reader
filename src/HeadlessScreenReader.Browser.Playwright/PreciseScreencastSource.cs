using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record PreciseCaptureFrame(int Sequence, string File, double PresentationUnixMilliseconds, string Sha256);

/// <summary>Raw JSON retains CDP's double timestamp; the SDK's Single frame model is never used.</summary>
internal sealed class PreciseScreencastSource(ICDPSession session, string folder)
{
    private readonly object _sync = new();
    private readonly CaptureFrameDrain _writes = new(0);
    private readonly List<PreciseCaptureFrame> _frames = [];
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;
    private bool _started;
    private string? _protocolError;
    private Task? _stop;

    public ImmutableArray<PreciseCaptureFrame> Frames
    {
        get { lock (_sync) return [.. _frames.OrderBy(f => f.Sequence)]; }
    }

    public async Task StartAsync()
    {
        lock (_sync)
        {
            if (_started || _closed) throw new BrowserOperationException("PRECISE_CAPTURE_START_DENIED");
            _started = true;
        }
        session.Event("Page.screencastFrame").OnEvent += OnFrame;
        await session.SendAsync("Page.startScreencast", new Dictionary<string, object>
        { ["format"] = "jpeg", ["quality"] = 90, ["maxWidth"] = 320, ["maxHeight"] = 240 }).ConfigureAwait(false);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        lock (_sync) return _stop ??= StopCoreAsync();
    }

    private void OnFrame(object? sender, JsonElement? payload)
    {
        lock (_sync) if (_closed) return;
        _ = ProcessAsync(payload?.Clone());
    }

    private async Task ProcessAsync(JsonElement? payload)
    {
        try
        {
            var value = payload ?? throw new InvalidDataException();
            var data = value.GetProperty("data").GetString()!;
            if (data.Length > 1024 * 1024) throw new InvalidDataException();
            var bytes = Convert.FromBase64String(data);
            var timestamp = value.GetProperty("metadata").GetProperty("timestamp").GetDouble() * 1000;
            var sessionId = value.GetProperty("sessionId").GetInt32();
            await _writes.WriteAsync(timestamp, bytes.Length, async sequence =>
            {
                var file = $"source-{sequence:D4}.jpg";
                try
                {
                    await using (var stream = new FileStream(Path.Combine(folder, file), FileMode.CreateNew,
                        FileAccess.Write, FileShare.Read, 4096, useAsync: true))
                        await stream.WriteAsync(bytes).ConfigureAwait(false);
                    lock (_sync)
                    {
                        _frames.Add(new(sequence, file, timestamp, Convert.ToHexStringLower(SHA256.HashData(bytes))));
                        if (_frames.Count >= 3) _ready.TrySetResult();
                    }
                }
                finally
                {
                    await session.SendAsync("Page.screencastFrameAck", new Dictionary<string, object>
                    { ["sessionId"] = sessionId }).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            if (_writes.Status.ErrorCode is { } code) _ready.TrySetException(new BrowserOperationException(code));
        }
        catch
        {
            lock (_sync) _protocolError = "PRECISE_CAPTURE_FRAME_INVALID";
            _ready.TrySetException(new BrowserOperationException("PRECISE_CAPTURE_FRAME_INVALID"));
        }
    }

    private async Task StopCoreAsync()
    {
        lock (_sync) _closed = true;
        var drain = _writes.CloseAdmission();
        session.Event("Page.screencastFrame").OnEvent -= OnFrame;
        if (_started) await session.SendAsync("Page.stopScreencast").ConfigureAwait(false);
        await drain.ConfigureAwait(false);
        if ((_protocolError ?? _writes.Status.ErrorCode) is { } code) throw new BrowserOperationException(code);
    }
}
