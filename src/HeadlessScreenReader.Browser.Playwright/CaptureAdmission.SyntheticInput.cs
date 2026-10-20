using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

internal sealed partial class CaptureAdmission
{
    private readonly SemaphoreSlim _syntheticCommands = new(1, 1);
    private SyntheticInputLease? _syntheticLease;
    private long _lastSyntheticGeneration;
    private bool _syntheticClosing;
    private int _syntheticActive;
    private ITimer? _syntheticTimer;
    private long _syntheticStarted;
    private TimeSpan _syntheticLifetime;
    private SyntheticInputLease? _revokingSyntheticLease;
    private Task? _syntheticRevocation;

    internal (bool Active, int Operations) SyntheticState
    {
        get
        {
            lock (_sync)
            {
                ExpireSyntheticInput();
                return (_phase == CapturePhase.Excluded && !_syntheticClosing && _syntheticLease is not null, _syntheticActive);
            }
        }
    }

    internal SyntheticInputLease GrantSyntheticInput(CaptureExclusionLease captureLease, long generation, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(captureLease);
        var duration = lifetime ?? TimeSpan.FromMinutes(2);
        if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        lock (_sync)
        {
            ExpireSyntheticInput();
            if (_phase != CapturePhase.Excluded || captureLease.Token != _token || captureLease.Epoch != _epoch
                || generation <= _lastSyntheticGeneration || _syntheticLease is not null || _syntheticClosing || _active != 0)
                throw new BrowserOperationException("SYNTHETIC_GRANT_DENIED");
            _lastSyntheticGeneration = generation;
            _syntheticStarted = _time.GetTimestamp(); _syntheticLifetime = duration;
            _revokingSyntheticLease = null; _syntheticRevocation = null;
            var lease = _syntheticLease = new(Guid.NewGuid(), _epoch, generation);
            _syntheticTimer = _time.CreateTimer(_ => { lock (_sync) if (_syntheticLease == lease) ExpireSyntheticInput(); },
                null, duration, Timeout.InfiniteTimeSpan);
            return lease;
        }
    }

    internal void EnsureSyntheticInput(SyntheticInputLease lease)
    {
        lock (_sync)
        {
            ExpireSyntheticInput();
            if (_phase != CapturePhase.Excluded || _syntheticClosing || _syntheticLease is null || lease != _syntheticLease)
                throw new BrowserInputDeniedException("SYNTHETIC_INPUT_FENCED");
        }
    }
    internal Task DispatchSyntheticInputAsync(SyntheticInputLease lease, Func<Task> dispatch)
    {
        lock (_sync) { EnsureSyntheticInput(lease); return dispatch(); }
    }

    internal Task<T> RunSyntheticInputAsync<T>(SyntheticInputLease lease, Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            EnsureSyntheticInput(lease);
            _syntheticActive++;
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return FinishSyntheticInputAsync(lease, operation);
    }
    private async Task<T> FinishSyntheticInputAsync<T>(SyntheticInputLease lease, Func<Task<T>> operation)
    {
        try
        {
            await _syntheticCommands.WaitAsync().ConfigureAwait(false);
            try { EnsureSyntheticInput(lease); return await operation().ConfigureAwait(false); }
            finally { _syntheticCommands.Release(); }
        }
        finally
        {
            lock (_sync) { _syntheticActive--; if (--_active == 0) _drained.TrySetResult(); }
        }
    }

    internal Task RevokeSyntheticInputAsync(SyntheticInputLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            ExpireSyntheticInput();
            if (lease == _revokingSyntheticLease && _syntheticRevocation is not null) return _syntheticRevocation;
            EnsureSyntheticInput(lease);
            return StartSyntheticRevocation(lease);
        }
    }
    private Task StartSyntheticRevocation(SyntheticInputLease lease)
    {
        _syntheticTimer?.Dispose(); _syntheticTimer = null;
        _syntheticClosing = true; _syntheticLease = null; _revokingSyntheticLease = lease;
        var actual = RevokeSyntheticCoreAsync(_drained.Task);
        _ = actual.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return _syntheticRevocation = actual;
    }
    private void ExpireSyntheticInput()
    {
        if (_phase == CapturePhase.Excluded && _syntheticLease is { } lease && _time.GetElapsedTime(_syntheticStarted) >= _syntheticLifetime)
            StartSyntheticRevocation(lease);
    }
    private void CloseSyntheticAdmission()
    {
        _syntheticTimer?.Dispose(); _syntheticTimer = null; _syntheticLease = null;
    }
    private async Task RevokeSyntheticCoreAsync(Task drain)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            await drain.WaitAsync(_transitionTimeout, _time).ConfigureAwait(false);
            lock (_sync)
            {
                if (_phase != CapturePhase.Excluded) throw new BrowserOperationException("SYNTHETIC_STOP_UNCONFIRMED");
                _syntheticClosing = false;
            }
        }
        catch { lock (_sync) SetPhase(CapturePhase.Faulted); throw new BrowserOperationException("SYNTHETIC_STOP_UNCONFIRMED"); }
        finally { _transition.Release(); }
    }
}
