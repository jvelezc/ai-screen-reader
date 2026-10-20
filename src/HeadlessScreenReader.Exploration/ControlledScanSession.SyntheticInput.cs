using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

public sealed partial class ControlledScanSession
{
    private ControllerRole _role = ControllerRole.Automation;
    private SyntheticInputLease? _nativeSyntheticLease;
    private Task? _syntheticStop;

    public async Task<ControllerLease> AcquireSimulatedAssistanceAsync(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.FromSeconds(1) || lifetime > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        ISyntheticExcludedInput adapter;
        CaptureExclusionLease gap;
        lock (_sync)
        {
            ExpireIfNeeded(); EnsureOpen();
            if (_phase != ControllerPhase.Unowned) throw new BrowserOperationException("CONTROL_BUSY");
            if (_generation >= _maximumGrants) throw new BrowserOperationException("CONTROL_GRANT_LIMIT");
            if (_gap is null || _browser is not ISyntheticExcludedInput provider || !provider.SyntheticStatus.Supported)
                throw new BrowserOperationException("CONTROL_SYNTHETIC_SCOPE_UNAVAILABLE");
            if (!_owned.SupportsAssistance) throw new BrowserOperationException("CONTROL_ASSISTANCE_STORE_UNAVAILABLE");
            gap = _gap; adapter = provider; _role = ControllerRole.SimulatedAssistance;
            Change(ControllerPhase.Acquiring, "CONTROL_SIMULATION_ACQUIRE_STARTED");
        }
        await _changes.WaitAsync().ConfigureAwait(false);
        try
        {
            var nativeGap = _owned.RequireAdapterGapAuthority(gap);
            lock (_sync)
            {
                EnsureOpen();
                _nativeSyntheticLease = adapter.GrantSyntheticInput(nativeGap, _generation + 1, lifetime);
                _syntheticStop = null;
                _leaseStarted = _time.GetTimestamp(); _lifetime = lifetime;
                _lease = new(_owned.Status.ScanId, Guid.NewGuid(), ++_generation, _time.GetUtcNow() + lifetime)
                { Role = ControllerRole.SimulatedAssistance };
                _revocation = null;
                Change(ControllerPhase.Active, "CONTROL_SIMULATION_GRANTED");
                var generation = _generation;
                _timer = _time.CreateTimer(_ => Expire(generation), null, lifetime, Timeout.InfiniteTimeSpan);
                return _lease;
            }
        }
        catch { Fault("CONTROL_SIMULATION_ACQUIRE_UNCONFIRMED"); throw new BrowserOperationException("CONTROL_SIMULATION_ACQUIRE_UNCONFIRMED"); }
        finally { _changes.Release(); }
    }

    public Task<OwnedAssistanceReceipt> ExecuteSimulatedAssistanceAsync(ControllerLease lease, SyntheticInputAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        CaptureExclusionLease gap;
        lock (_sync)
        {
            ExpireIfNeeded(); EnsureLease(lease);
            if (_role != ControllerRole.SimulatedAssistance || _nativeSyntheticLease is null || _gap is null)
                throw new BrowserOperationException("CONTROL_ROLE_DENIED");
            if (!Enum.IsDefined(action)) throw new BrowserOperationException("CONTROL_SYNTHETIC_ACTION_DENIED");
            gap = _gap;
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var actual = ExecuteSimulationCoreAsync(lease, gap, action, cancellationToken);
        Observe(actual); return WaitSimulationAsync(actual, cancellationToken);
    }
    private async Task<OwnedAssistanceReceipt> ExecuteSimulationCoreAsync(ControllerLease lease, CaptureExclusionLease gap,
        SyntheticInputAction action, CancellationToken cancellationToken)
    {
        try
        {
            return await _owned.ExecuteAssistanceTrackedAsync(gap, lease.Generation, action, token =>
            {
                lock (_sync)
                {
                    ExpireIfNeeded();
                    try { EnsureLease(lease); }
                    catch (BrowserOperationException) { throw new BrowserInputDeniedException("SYNTHETIC_INPUT_FENCED"); }
                    if (_role != ControllerRole.SimulatedAssistance || _nativeSyntheticLease is not { } native)
                        throw new BrowserInputDeniedException("SYNTHETIC_INPUT_FENCED");
                    return ((ISyntheticExcludedInput)_browser).ExecuteSyntheticInputAsync(native, action, token);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch { lock (_sync) ObserveOwnedFault(); throw; }
        finally { lock (_sync) if (--_active == 0) _drained.TrySetResult(); }
    }
    private async Task<OwnedAssistanceReceipt> WaitSimulationAsync(Task<OwnedAssistanceReceipt> actual, CancellationToken cancellationToken)
    {
        try { return await actual.WaitAsync(TimeSpan.FromSeconds(30), _time, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { throw new BrowserOperationException("CONTROL_ASSISTANCE_UNCONFIRMED"); }
    }
    private Task StopSyntheticInput()
    {
        if (_nativeSyntheticLease is not { } lease || _browser is not ISyntheticExcludedInput adapter)
            throw new BrowserOperationException("CONTROL_SIMULATION_STOP_UNCONFIRMED");
        return _syntheticStop ??= adapter.RevokeSyntheticInputAsync(lease);
    }
    private void TryStopSyntheticInput()
    {
        if (_nativeSyntheticLease is null) return;
        try { Observe(StopSyntheticInput()); }
        catch { /* The controller remains closed; native/context shutdown is still required. */ }
    }
}
