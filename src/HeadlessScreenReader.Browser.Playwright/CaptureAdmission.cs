using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>
/// Closes admission before draining actual operations. Cancelling a caller's wait never
/// releases an operation that Chromium may still be executing. Transitions are serialized.
/// </summary>
internal sealed partial class CaptureAdmission
{
    private readonly TimeProvider _time;
    private readonly long _started;
    private readonly TimeSpan _transitionTimeout;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly Queue<CaptureLifecycleEvent> _events = new();
    private CapturePhase _phase = CapturePhase.Permitted;
    private long _epoch = 1;
    private long _sequence;
    private int _active;
    private bool _truncated;
    private Guid _token;
    private TaskCompletionSource _drained = Completed();

    public CaptureAdmission(TimeProvider? timeProvider = null, TimeSpan? transitionTimeout = null)
    {
        _transitionTimeout = transitionTimeout ?? TimeSpan.FromSeconds(5);
        if (_transitionTimeout <= TimeSpan.Zero || _transitionTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(transitionTimeout));
        _time = timeProvider ?? TimeProvider.System;
        _started = _time.GetTimestamp();
    }

    public CaptureStatus Status
    {
        get { lock (_sync) return new(_phase, _epoch, [.. _events], _truncated); }
    }

    public Task<T> RunAsync<T>(Func<Task<T>> operation, bool input = false)
    {
        long epoch;
        lock (_sync)
        {
            if (_phase != CapturePhase.Permitted)
                return Task.FromException<T>(input
                    ? new BrowserInputDeniedException("CAPTURE_EXCLUDED")
                    : new BrowserOperationException("CAPTURE_EXCLUDED"));
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            epoch = _epoch;
        }
        return FinishAsync(operation, epoch);
    }

    private async Task<T> FinishAsync<T>(Func<Task<T>> operation, long epoch)
    {
        try
        {
            var result = await operation().ConfigureAwait(false);
            lock (_sync)
            {
                if (_phase != CapturePhase.Permitted || epoch != _epoch)
                    throw new BrowserOperationException("CAPTURE_RESULT_EXCLUDED");
            }
            return result;
        }
        finally
        {
            lock (_sync) if (--_active == 0) _drained.TrySetResult();
        }
    }

    // Once the transition starts it is deliberately not cancellable. Caller cancellation
    // may stop waiting, but cannot imply that a stop acknowledgement has been received.
    public async Task<CaptureExclusionLease> ExcludeAsync(Func<Task> stopCollectors)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            Task drain;
            lock (_sync)
            {
                if (_phase != CapturePhase.Permitted) throw new BrowserOperationException("CAPTURE_TRANSITION_DENIED");
                _token = Guid.NewGuid();
                SetPhase(CapturePhase.Draining);
                drain = _drained.Task;
            }
            try
            {
                await drain.WaitAsync(_transitionTimeout, _time).ConfigureAwait(false);
                await stopCollectors().WaitAsync(_transitionTimeout, _time).ConfigureAwait(false);
                lock (_sync)
                {
                    SetPhase(CapturePhase.Excluded);
                    return new(_token, _epoch);
                }
            }
            catch
            {
                lock (_sync) SetPhase(CapturePhase.Faulted);
                throw new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED");
            }
        }
        finally { _transition.Release(); }
    }

    public async Task<T> ResumeAsync<T>(CaptureExclusionLease lease, Func<Task<T>> freshBaseline)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_phase != CapturePhase.Excluded || lease.Token != _token || lease.Epoch != _epoch)
                    throw new BrowserOperationException("CAPTURE_LEASE_INVALID");
                if (_syntheticLease is not null || _syntheticClosing)
                    throw new BrowserOperationException("SYNTHETIC_INPUT_STILL_OWNED");
                SetPhase(CapturePhase.Resuming);
            }
            try
            {
                var baseline = await freshBaseline().WaitAsync(_transitionTimeout, _time).ConfigureAwait(false);
                lock (_sync)
                {
                    _epoch++;
                    _token = Guid.Empty;
                    SetPhase(CapturePhase.Permitted);
                }
                return baseline;
            }
            catch
            {
                lock (_sync) SetPhase(CapturePhase.Faulted);
                throw new BrowserOperationException("CAPTURE_RESUME_UNCONFIRMED");
            }
        }
        finally { _transition.Release(); }
    }

    public async Task CloseAsync(Func<Task> close)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            Task drain;
            lock (_sync) { SetPhase(CapturePhase.Closed); drain = _drained.Task; }
            // Closing Chromium first aborts hung protocol calls; they still must settle.
            try { await close().ConfigureAwait(false); }
            finally { await drain.ConfigureAwait(false); }
        }
        finally { _transition.Release(); }
    }

    private void SetPhase(CapturePhase phase)
    {
        if (phase != CapturePhase.Excluded) CloseSyntheticAdmission();
        _phase = phase;
        if (_events.Count == 256) { _events.Dequeue(); _truncated = true; }
        _events.Enqueue(new(++_sequence, phase, _epoch, _time.GetElapsedTime(_started).TotalMilliseconds));
    }

    private static TaskCompletionSource Completed()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
