using System.Runtime.CompilerServices;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>One expiring local AI controller. Trusted composition owns the adapter; no remote/human channel.</summary>
public sealed partial class ControlledScanSession : IAsyncDisposable
{
    private static readonly ConditionalWeakTable<IScreenReaderBrowser, ControlledScanSession> Owners = new();
    private static readonly object OwnersLock = new();
    private readonly object _sync = new();
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly OwnedScanSession _owned;
    private readonly IScreenReaderBrowser _browser;
    private readonly TimeProvider _time;
    private readonly long _started;
    private readonly int _maximumGrants;
    private readonly AsyncLocal<long?> _executingGeneration = new();
    private readonly Queue<ControllerEvent> _events = new();
    private ControllerPhase _phase = ControllerPhase.Unowned;
    private ControllerLease? _lease;
    private CaptureExclusionLease? _gap;
    private ITimer? _timer;
    private Task? _revocation, _close;
    private TaskCompletionSource _drained = Completed();
    private long _generation, _sequence, _leaseStarted;
    private TimeSpan _lifetime;
    private int _active;
    private bool _closing, _truncated;
    private string? _fault;

    public ControlledScanSession(IScreenReaderBrowser browser, IOwnedEvidenceStore store,
        TimeProvider? timeProvider = null, int maximumGrants = 32, int maximumCommands = 100)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumGrants, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumGrants, 32);
        if (browser is not ICaptureControl control || browser is not IAsyncDisposable)
            throw new BrowserOperationException("CONTROL_REQUIRES_OWNED_ADAPTER");
        lock (OwnersLock)
        {
            if (Owners.TryGetValue(browser, out _)) throw new BrowserOperationException("CONTROL_BROWSER_ALREADY_OWNED");
            Owners.Add(browser, this);
        }
        _browser = browser; _time = timeProvider ?? TimeProvider.System; _started = _time.GetTimestamp(); _maximumGrants = maximumGrants;
        try { _owned = new(new FencedBrowser(this, browser, control), store, _time, maximumCommands); }
        catch { lock (OwnersLock) Owners.Remove(browser); throw; }
    }

    public ControllerStatus Status
    {
        get
        {
            lock (_sync)
            {
                ExpireIfNeeded();
                ObserveOwnedFault();
                return new(_owned.Status.ScanId, _phase, _generation, _lease?.ExpiresAt, _active, _fault, [.. _events], _truncated, _owned.Status)
                { Role = _lease?.Role, Shutdown = (_browser as IBrowserShutdownDiagnostics)?.ShutdownStatus };
            }
        }
    }

    public async Task<ControllerLease> AcquireAsync(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.FromSeconds(1) || lifetime > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        lock (_sync)
        {
            ExpireIfNeeded();
            EnsureOpen();
            if (_phase != ControllerPhase.Unowned) throw new BrowserOperationException("CONTROL_BUSY");
            if (_generation >= _maximumGrants) throw new BrowserOperationException("CONTROL_GRANT_LIMIT");
            if (_browser is ISyntheticExcludedInput synthetic && synthetic.SyntheticStatus is { } state
                && (state.CanaryMayRemain || state.AuthorityActive || state.ActiveOperations != 0))
                throw new BrowserOperationException("CONTROL_SYNTHETIC_CLEAR_REQUIRED");
            _role = ControllerRole.Automation;
            Change(ControllerPhase.Acquiring, "CONTROL_ACQUIRE_STARTED");
        }
        await _changes.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_gap is { } gap)
            {
                await _owned.ResumeCaptureAsync(gap).ConfigureAwait(false);
                _gap = null;
            }
            lock (_sync)
            {
                EnsureOpen();
                _leaseStarted = _time.GetTimestamp(); _lifetime = lifetime;
                _lease = new(_owned.Status.ScanId, Guid.NewGuid(), ++_generation, _time.GetUtcNow() + lifetime);
                _revocation = null;
                Change(ControllerPhase.Active, "CONTROL_GRANTED");
                var generation = _generation;
                _timer = _time.CreateTimer(_ => Expire(generation), null, lifetime, Timeout.InfiniteTimeSpan);
                return _lease;
            }
        }
        catch { Fault("CONTROL_ACQUIRE_UNCONFIRMED"); throw new BrowserOperationException("CONTROL_ACQUIRE_UNCONFIRMED"); }
        finally { _changes.Release(); }
    }

    public Task<OwnedCommandReceipt> ExecuteAsync(ControllerLease lease, long expectedEpoch, ReaderCommand command,
        string? key = null, CancellationToken cancellationToken = default, ObservationExpectation? expectation = null,
        string? expectedDocumentId = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ExpireIfNeeded(); EnsureLease(lease);
            if (_role != ControllerRole.Automation) throw new BrowserOperationException("CONTROL_ROLE_DENIED");
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var actual = ExecuteCoreAsync(lease.Generation, expectedEpoch, command, key, cancellationToken, expectation, expectedDocumentId);
        Observe(actual);
        return WaitAsync(actual, cancellationToken);
    }
    private async Task<OwnedCommandReceipt> ExecuteCoreAsync(long generation, long epoch, ReaderCommand command, string? key,
        CancellationToken cancellationToken, ObservationExpectation? expectation, string? expectedDocumentId)
    {
        _executingGeneration.Value = generation;
        try
        {
            return await _owned.ExecuteTrackedAsync(epoch, command, key, cancellationToken, expectation, expectedDocumentId, generation).ConfigureAwait(false);
        }
        catch { lock (_sync) ObserveOwnedFault(); throw; }
        finally
        {
            _executingGeneration.Value = null;
            lock (_sync) if (--_active == 0) _drained.TrySetResult();
        }
    }
    private async Task<OwnedCommandReceipt> WaitAsync(Task<OwnedCommandReceipt> actual, CancellationToken cancellationToken)
    {
        try { return await actual.WaitAsync(TimeSpan.FromSeconds(30), _time, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { throw new BrowserOperationException("CONTROL_COMMAND_UNCONFIRMED"); }
    }

    public Task ReleaseAsync(ControllerLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        Task actual;
        lock (_sync)
        {
            ExpireIfNeeded(); EnsureLease(lease);
            actual = StartRevocation("CONTROL_RELEASED");
        }
        return actual.WaitAsync(cancellationToken);
    }
    private void Expire(long generation)
    {
        lock (_sync) if (generation == _generation) ExpireIfNeeded();
    }
    private void ExpireIfNeeded()
    {
        if (_phase == ControllerPhase.Active && _time.GetElapsedTime(_leaseStarted) >= _lifetime)
            StartRevocation("CONTROL_EXPIRED");
    }
    private Task StartRevocation(string code)
    {
        if (_revocation is not null) return _revocation;
        _timer?.Dispose(); _timer = null;
        _lease = null;
        Change(ControllerPhase.Revoking, code);
        var actual = RevokeCoreAsync(_drained.Task, _role);
        Observe(actual); return _revocation = actual;
    }
    private async Task RevokeCoreAsync(Task drain, ControllerRole role)
    {
        await _changes.WaitAsync().ConfigureAwait(false);
        try
        {
            if (role == ControllerRole.SimulatedAssistance)
                await Task.WhenAll(drain, StopSyntheticInput()).WaitAsync(TimeSpan.FromSeconds(5), _time).ConfigureAwait(false);
            else
            {
                var stop = _owned.ExcludeCaptureAsync();
                await Task.WhenAll(drain, stop).WaitAsync(TimeSpan.FromSeconds(5), _time).ConfigureAwait(false);
                _gap = stop.Result;
            }
            lock (_sync)
            {
                if (role == ControllerRole.SimulatedAssistance) _nativeSyntheticLease = null;
                if (!_closing && _phase == ControllerPhase.Revoking) Change(ControllerPhase.Unowned, "CONTROL_DRAIN_ACKNOWLEDGED");
            }
        }
        catch { Fault("CONTROL_DRAIN_UNCONFIRMED"); throw new BrowserOperationException("CONTROL_DRAIN_UNCONFIRMED"); }
        finally { _changes.Release(); }
    }

    private void EnsureOpen()
    {
        ObserveOwnedFault();
        if (_closing) throw new BrowserOperationException("CONTROL_CLOSED");
        if (_fault is not null || _owned.Status.FaultCode is not null) throw new BrowserOperationException(_fault ?? "CONTROL_OWNED_FAULTED");
    }
    private void ObserveOwnedFault()
    {
        if (_fault is null && !_closing && _owned.Status.FaultCode is not null) Fault("CONTROL_OWNED_FAULTED");
        if (_fault is null && !_closing && _browser is ICaptureControl control && control.CaptureStatus.Phase == CapturePhase.Faulted)
            Fault("CONTROL_ADAPTER_FAULTED");
    }
    private void EnsureLease(ControllerLease lease)
    {
        EnsureOpen();
        if (_phase != ControllerPhase.Active || _lease is null || lease.ScanId != _lease.ScanId
            || lease.Token != _lease.Token || lease.Generation != _generation || lease.Role != _role)
            throw new BrowserOperationException("CONTROL_LEASE_INVALID");
    }
    private Task Dispatch(Func<Task> send)
    {
        lock (_sync)
        {
            ExpireIfNeeded();
            if (_closing || _phase != ControllerPhase.Active || _lease is null || _role != ControllerRole.Automation || _executingGeneration.Value != _generation
                || _owned.Status.FaultCode is not null)
                throw new BrowserInputDeniedException("CONTROL_INPUT_FENCED");
            return send();
        }
    }
    private void Change(ControllerPhase phase, string code)
    {
        _phase = phase;
        if (_events.Count == 256) { _events.Dequeue(); _truncated = true; }
        _events.Enqueue(new ControllerEvent(++_sequence, _generation, phase, code, _time.GetElapsedTime(_started).TotalMilliseconds) { Role = _role });
    }
    private void Fault(string code)
    {
        lock (_sync)
        {
            _fault ??= code; _timer?.Dispose(); _timer = null; _lease = null; Change(ControllerPhase.Faulted, code);
            TryStopSyntheticInput();
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _closing = true; _timer?.Dispose(); _timer = null; _lease = null;
            TryStopSyntheticInput();
            return new(_close ??= CloseCoreAsync());
        }
    }
    private async Task CloseCoreAsync()
    {
        lock (_sync) Change(ControllerPhase.Closing, "CONTROL_CLOSE_STARTED");
        await _changes.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_owned.DisposeAsync().AsTask(), _drained.Task).WaitAsync(TimeSpan.FromSeconds(5), _time).ConfigureAwait(false);
            lock (_sync) Change(ControllerPhase.Closed, "CONTROL_CLOSED");
            lock (OwnersLock) Owners.Remove(_browser);
        }
        catch { Fault("CONTROL_CLOSE_UNCONFIRMED"); throw new BrowserOperationException("CONTROL_CLOSE_UNCONFIRMED"); }
        finally { _changes.Release(); }
    }
    private static void Observe(Task task) =>
        _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    private static TaskCompletionSource Completed()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); result.SetResult(); return result;
    }
    private sealed class FencedBrowser(ControlledScanSession owner, IScreenReaderBrowser browser, ICaptureControl control)
        : IScreenReaderBrowser, ICaptureControl, IPrivatePixelCapture, IAsyncDisposable
    {
        public CaptureStatus CaptureStatus => control.CaptureStatus;
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => browser.CaptureAsync(cancellationToken);
        public Task<BrowserPixelFrame> CapturePixelsAsync(long epoch, string documentId, CancellationToken cancellationToken = default)
            => browser is IPrivatePixelCapture pixels ? pixels.CapturePixelsAsync(epoch, documentId, cancellationToken)
                : throw new BrowserOperationException("OWNED_VIEW_UNAVAILABLE");
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => owner.Dispatch(() => browser.PressKeyAsync(key, cancellationToken));
        public Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken cancellationToken = default) => owner.Dispatch(() => browser.PressListboxKeyAsync(input, cancellationToken));
        public Task<CaptureExclusionLease> ExcludeCaptureAsync() => control.ExcludeCaptureAsync();
        public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease) => control.ResumeCaptureAsync(lease);
        public ValueTask DisposeAsync() => ((IAsyncDisposable)browser).DisposeAsync();
    }
}
