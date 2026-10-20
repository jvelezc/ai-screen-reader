namespace HeadlessScreenReader.Browser.Playwright;

internal sealed record FrameDrainStatus(int Accepted, int DroppedStale, long Bytes, string? ErrorCode);

/// <summary>One immutable segment. Closing admission is permanent; SDK callbacks never own the drain.</summary>
internal sealed class CaptureFrameDrain
{
    private readonly object _sync = new();
    private readonly double _minimumTimestamp;
    private readonly int _maxFrames;
    private readonly long _maxBytes;
    private bool _accepting = true;
    private int _active;
    private int _accepted;
    private int _dropped;
    private long _bytes;
    private string? _error;
    private TaskCompletionSource _drained = Completed();

    public CaptureFrameDrain(double minimumTimestamp, int maxFrames = 250, long maxBytes = 16 * 1024 * 1024)
    {
        if (!double.IsFinite(minimumTimestamp) || minimumTimestamp < 0) throw new ArgumentOutOfRangeException(nameof(minimumTimestamp));
        if (maxFrames is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        if (maxBytes is < 1 or > 32 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _minimumTimestamp = minimumTimestamp;
        _maxFrames = maxFrames;
        _maxBytes = maxBytes;
    }

    public FrameDrainStatus Status
    {
        get { lock (_sync) return new(_accepted, _dropped, _bytes, _error); }
    }

    public Task WriteAsync(double timestamp, int bytes, Func<int, Task> sink)
    {
        int sequence;
        lock (_sync)
        {
            if (!_accepting) return Task.CompletedTask;
            if (!double.IsFinite(timestamp) || bytes <= 0) return Fail("CAPTURE_FRAME_INVALID");
            if (timestamp < _minimumTimestamp) { _dropped++; return Task.CompletedTask; }
            if (_accepted == _maxFrames || bytes > _maxBytes - _bytes) return Fail("CAPTURE_FRAME_LIMIT");
            sequence = ++_accepted;
            _bytes += bytes;
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return FinishAsync(sequence, sink);
    }

    public Task CloseAdmission()
    {
        lock (_sync) { _accepting = false; return _drained.Task; }
    }

    private async Task FinishAsync(int sequence, Func<int, Task> sink)
    {
        try { await sink(sequence).ConfigureAwait(false); }
        catch { lock (_sync) Fail("CAPTURE_FRAME_WRITE_FAILED"); }
        finally { lock (_sync) if (--_active == 0) _drained.TrySetResult(); }
    }

    private Task Fail(string code)
    {
        _error ??= code;
        _accepting = false;
        return Task.CompletedTask;
    }

    private static TaskCompletionSource Completed()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
