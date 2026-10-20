using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

/// <summary>Fences whole reader operations and their owned retention, alongside adapter exclusion.</summary>
internal sealed class ReaderCaptureAdmission(TimeProvider time)
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly Queue<CaptureLifecycleEvent> _events = new();
    private readonly long _started = time.GetTimestamp();
    private CapturePhase _phase = CapturePhase.Permitted;
    private long _epoch = 1;
    private long _sequence;
    private int _active;
    private bool _truncated;
    private Guid _token;
    private CaptureExclusionLease? _adapterLease;
    private TaskCompletionSource _drained = Completed();
    private CancellationTokenSource _interruption = new();
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public CaptureStatus Status
    {
        get { lock (_sync) return new(_phase, _epoch, [.. _events], _truncated); }
    }

    public CancellationToken Interruption => Read(() => _interruption.Token);

    public void EnsurePermitted(long? epoch = null)
    {
        lock (_sync)
        {
            if (_phase != CapturePhase.Permitted) throw new BrowserOperationException("READER_CAPTURE_EXCLUDED");
            if (epoch is not null && epoch != _epoch) throw new BrowserOperationException("READER_CAPTURE_EPOCH_CHANGED");
        }
    }

    public T Read<T>(Func<T> read)
    {
        lock (_sync)
        {
            EnsurePermitted();
            return read();
        }
    }

    public void Retain(Action retain)
    {
        lock (_sync)
            if (_phase == CapturePhase.Permitted) retain();
    }

    public Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        lock (_sync)
        {
            EnsurePermitted();
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return FinishAsync(operation);
    }

    private async Task<T> FinishAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation().ConfigureAwait(false);
            lock (_sync)
                if (_phase != CapturePhase.Permitted) throw new BrowserOperationException("READER_CAPTURE_RESULT_EXCLUDED");
            return result;
        }
        catch
        {
            lock (_sync)
                if (_phase != CapturePhase.Permitted) throw new BrowserOperationException("READER_CAPTURE_RESULT_EXCLUDED");
            throw;
        }
        finally
        {
            lock (_sync) if (--_active == 0) _drained.TrySetResult();
        }
    }

    public async Task<CaptureExclusionLease> ExcludeAsync(ICaptureControl adapter, Action clear)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            Task drain;
            lock (_sync)
            {
                EnsurePermitted();
                _token = Guid.NewGuid();
                SetPhase(CapturePhase.Draining);
                drain = _drained.Task;
            }
            try
            {
                _interruption.Cancel();
                // Close adapter admission immediately, not after a stalled reader finishes.
                var stop = adapter.ExcludeCaptureAsync();
                await Task.WhenAll(drain, stop).WaitAsync(Timeout, time).ConfigureAwait(false);
                lock (_sync)
                {
                    if (stop.Result.Token == Guid.Empty || adapter.CaptureStatus.Phase != CapturePhase.Excluded
                        || adapter.CaptureStatus.Epoch != stop.Result.Epoch)
                        throw new BrowserOperationException("READER_CAPTURE_ADAPTER_STOP_INVALID");
                    _adapterLease = stop.Result;
                    clear();
                    SetPhase(CapturePhase.Excluded);
                    return new(_token, _epoch);
                }
            }
            catch
            {
                lock (_sync) SetPhase(CapturePhase.Faulted);
                throw new BrowserOperationException("READER_CAPTURE_STOP_UNCONFIRMED");
            }
        }
        finally { _transition.Release(); }
    }

    public async Task<AccessibilitySnapshot> ResumeAsync(ICaptureControl adapter, CaptureExclusionLease lease)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            CaptureExclusionLease adapterLease;
            lock (_sync)
            {
                if (_phase != CapturePhase.Excluded || lease.Token != _token || lease.Epoch != _epoch || _adapterLease is null)
                    throw new BrowserOperationException("READER_CAPTURE_LEASE_INVALID");
                adapterLease = _adapterLease;
                SetPhase(CapturePhase.Resuming);
            }
            try
            {
                var baseline = await adapter.ResumeCaptureAsync(adapterLease).WaitAsync(Timeout, time).ConfigureAwait(false);
                lock (_sync)
                {
                    if (baseline.CaptureEpoch <= adapterLease.Epoch || adapter.CaptureStatus.Phase != CapturePhase.Permitted
                        || baseline.CaptureEpoch != adapter.CaptureStatus.Epoch)
                        throw new BrowserOperationException("READER_CAPTURE_ADAPTER_BASELINE_INVALID");
                    _epoch++;
                    _interruption.Dispose();
                    _interruption = new();
                    _token = Guid.Empty;
                    _adapterLease = null;
                    SetPhase(CapturePhase.Permitted);
                }
                return baseline;
            }
            catch
            {
                lock (_sync) SetPhase(CapturePhase.Faulted);
                throw new BrowserOperationException("READER_CAPTURE_RESUME_UNCONFIRMED");
            }
        }
        finally { _transition.Release(); }
    }

    private void SetPhase(CapturePhase phase)
    {
        _phase = phase;
        if (_events.Count == 256) { _events.Dequeue(); _truncated = true; }
        _events.Enqueue(new(++_sequence, phase, _epoch, time.GetElapsedTime(_started).TotalMilliseconds));
    }

    public void Close()
    {
        lock (_sync)
        {
            SetPhase(CapturePhase.Closed);
            _interruption.Dispose();
        }
        _transition.Dispose();
    }

    private static TaskCompletionSource Completed()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult();
        return result;
    }
}
