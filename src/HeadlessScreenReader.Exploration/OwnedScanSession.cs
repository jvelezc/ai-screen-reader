using System.Runtime.CompilerServices;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Exploration;

/// <summary>One trusted owner of a reader, adapter and typed evidence store. No human input authority.</summary>
public sealed class OwnedScanSession : IAsyncDisposable
{
    private static readonly ConditionalWeakTable<IScreenReaderBrowser, OwnedScanSession> Owners = new();
    private static readonly object OwnersLock = new();
    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly IScreenReaderBrowser _browser;
    private readonly IOwnedEvidenceStore _store;
    private readonly ObservingBrowser _observed;
    private readonly ScreenReaderSession _reader;
    private readonly TimeProvider _time;
    private readonly long _started;
    private readonly TraversalClock _clock;
    private readonly int _maximumCommands;
    private readonly Queue<CaptureLifecycleEvent> _events = new();
    private CapturePhase _phase = CapturePhase.Permitted;
    private long _epoch = 1, _ordinal, _lifecycleSequence;
    private int _active, _writes;
    private bool _truncated, _closing;
    private string? _fault;
    private Guid _token;
    private CaptureExclusionLease? _readerLease;
    private Task<CaptureExclusionLease>? _stop;
    private Task? _close;
    private TaskCompletionSource _drained = Completed();
    private TaskCompletionSource _writesDrained = Completed();

    public OwnedScanSession(IScreenReaderBrowser browser, IOwnedEvidenceStore store,
        TimeProvider? timeProvider = null, int maximumCommands = 100)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCommands, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCommands, 500);
        if (!Guid.TryParseExact(store.ScanId, "N", out _) || browser is not ICaptureControl control
            || control.CaptureStatus is not { Phase: CapturePhase.Permitted, Epoch: 1 })
            throw new BrowserOperationException("OWNED_SCAN_REQUIRES_FRESH_CONTROL");
        lock (OwnersLock)
        {
            if (Owners.TryGetValue(browser, out _)) throw new BrowserOperationException("OWNED_BROWSER_ALREADY_OWNED");
            Owners.Add(browser, this);
        }
        _browser = browser; _store = store; _maximumCommands = maximumCommands;
        _time = timeProvider ?? TimeProvider.System; _started = _time.GetTimestamp();
        _observed = new(this, browser, control);
        var before = Elapsed();
        _reader = new(_observed, maximumCommands, timeProvider: _time);
        _clock = new(_started, _time.TimestampFrequency, new(before, Elapsed()));
    }

    public OwnedScanStatus Status
    {
        get { lock (_sync) return new(_store.ScanId, new(_phase, _epoch, [.. _events], _truncated), _active, _writes, _fault); }
    }
    private double Elapsed() => _time.GetElapsedTime(_started).TotalMilliseconds;
    private void Ensure(long epoch)
    {
        if (_closing) throw new BrowserOperationException("OWNED_SCAN_CLOSED");
        if (_phase != CapturePhase.Permitted) throw new BrowserOperationException(_fault ?? "OWNED_SCAN_EXCLUDED");
        if (epoch != _epoch) throw new BrowserOperationException("OWNED_SCAN_EPOCH_STALE");
    }

    public Task<OwnedCommandReceipt> ExecuteAndPersistAsync(long expectedEpoch, ReaderCommand command, string? key = null,
        CancellationToken cancellationToken = default, ObservationExpectation? expectation = null, string? expectedDocumentId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            Ensure(expectedEpoch);
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var actual = ExecuteCoreAsync(expectedEpoch, command, key, cancellationToken, expectation, expectedDocumentId);
        ObserveFault(actual);
        return WaitForCallerAsync(actual, cancellationToken);
    }

    private async Task<OwnedCommandReceipt> WaitForCallerAsync(Task<OwnedCommandReceipt> actual, CancellationToken token)
    {
        try { return await actual.WaitAsync(TimeSpan.FromSeconds(30), _time, token).ConfigureAwait(false); }
        catch (TimeoutException) { throw new BrowserOperationException("OWNED_OPERATION_UNCONFIRMED"); }
    }

    private async Task<OwnedCommandReceipt> ExecuteCoreAsync(long epoch, ReaderCommand command, string? key,
        CancellationToken callerToken, ObservationExpectation? expectation, string? expectedDocumentId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, deadline.Token);
        try
        {
            await _commands.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                OwnedCommandIntent intent;
                lock (_sync)
                {
                    Ensure(epoch);
                    if (_ordinal >= _maximumCommands) throw new BrowserOperationException("OWNED_COMMAND_BUDGET_EXHAUSTED");
                    intent = new(_store.ScanId, ++_ordinal, epoch, command);
                }
                _observed.Reset();
                OwnedWriteReceipt? intentWrite = null, resultWrite = null;
                OwnedCommandEvidence? evidence = null;
                var state = OwnedCompletionState.ResultExcluded;
                string? code = "OWNED_RESULT_EXCLUDED";
                try
                {
                    intentWrite = await WriteAsync(() => _store.WriteIntentAsync(intent)).ConfigureAwait(false);
                    lock (_sync) Ensure(epoch);
                    linked.Token.ThrowIfCancellationRequested();
                    var before = _reader.Timeline.LastOrDefault()?.Sequence ?? 0;
                    var step = await _reader.ExecuteAsync(command, key, linked.Token, expectation, expectedDocumentId).ConfigureAwait(false);
                    var timeline = _reader.Timeline;
                    evidence = new(intent, step, _observed.Snapshot is { } snapshot ? snapshot with { DocumentUrl = null } : null,
                        _observed.Window, [.. timeline.Where(e => e.Sequence > before)], _reader.TimelineTruncated, _clock);
                    lock (_sync) Ensure(epoch);
                    if (_observed.EpochMismatch) throw new BrowserOperationException("OWNED_CAPTURE_EPOCH_MISMATCH");
                    // Admission and the actual write are one tracked task; exclusion waits for its acknowledgement.
                    resultWrite = await WritePayloadAsync(epoch, evidence).ConfigureAwait(false);
                    state = OwnedCompletionState.ResultStored;
                    code = "OWNED_RESULT_STORED";
                }
                catch (OperationCanceledException) { state = OwnedCompletionState.Cancelled; code = "OWNED_COMMAND_CANCELLED"; }
                catch (BrowserOperationException)
                {
                    if (_observed.EpochMismatch) { state = OwnedCompletionState.EngineError; code = "OWNED_CAPTURE_EPOCH_MISMATCH"; Fault(code); }
                    else { state = OwnedCompletionState.ResultExcluded; code = "OWNED_RESULT_EXCLUDED"; }
                    evidence = null;
                }
                catch
                {
                    state = OwnedCompletionState.WriteUnconfirmed;
                    code = "OWNED_WRITE_UNCONFIRMED";
                    evidence = null;
                    Fault(code);
                }
                var completion = new OwnedCommandCompletion(intent, state, _observed.InputRequests,
                    _observed.CompletedInputCalls, _observed.InputUncertain, code);
                OwnedWriteReceipt completionWrite;
                try { completionWrite = await WriteAsync(() => _store.WriteCompletionAsync(completion)).ConfigureAwait(false); }
                catch { Fault("OWNED_COMPLETION_UNCONFIRMED"); throw new BrowserOperationException("OWNED_COMPLETION_UNCONFIRMED"); }
                lock (_sync) Ensure(epoch);
                if (intentWrite is null) throw new BrowserOperationException("OWNED_WRITE_UNCONFIRMED");
                return new(completion, evidence, intentWrite, resultWrite, completionWrite);
            }
            finally { _commands.Release(); }
        }
        finally { lock (_sync) if (--_active == 0) _drained.TrySetResult(); }
    }

    private Task<OwnedWriteReceipt> WritePayloadAsync(long epoch, OwnedCommandEvidence evidence)
    {
        lock (_sync)
        {
            Ensure(epoch);
            return WriteAsync(() => _store.WriteResultAsync(evidence));
        }
    }
    private async Task<OwnedWriteReceipt> WriteAsync(Func<Task<OwnedWriteReceipt>> write)
    {
        lock (_sync)
            if (_writes++ == 0) _writesDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try { return await write().ConfigureAwait(false); }
        catch { throw new WriteUnconfirmedException(); }
        finally { lock (_sync) if (--_writes == 0) _writesDrained.TrySetResult(); }
    }
    private sealed class WriteUnconfirmedException() : Exception("OWNED_WRITE_UNCONFIRMED");
    private CaptureLifecycleEvent NewEvent(CapturePhase phase) => new(++_lifecycleSequence, phase, _epoch, Elapsed());
    private void Apply(CaptureLifecycleEvent item)
    {
        _phase = item.Phase;
        if (_events.Count == 256) { _events.Dequeue(); _truncated = true; }
        _events.Enqueue(item);
    }
    private Task<OwnedWriteReceipt> Journal(CaptureLifecycleEvent item, string? code = null) =>
        WriteAsync(() => _store.WriteLifecycleAsync(new(_store.ScanId, item, code)));
    private Task<CaptureExclusionLease> StopReader()
    {
        lock (_sync) return _stop ??= _reader.ExcludeCaptureAsync();
    }
    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    private void Fault(string code)
    {
        lock (_sync)
        {
            _fault ??= code;
            if (_phase is not (CapturePhase.Faulted or CapturePhase.Closed)) Apply(NewEvent(CapturePhase.Faulted));
        }
        try { ObserveFault(StopReader()); }
        catch { /* Fault remains closed even when stop cannot be confirmed. */ }
    }

    public async Task<CaptureExclusionLease> ExcludeCaptureAsync()
    {
        lock (_sync) if (_closing) throw new BrowserOperationException("OWNED_SCAN_CLOSED");
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            Task drain;
            CaptureLifecycleEvent start;
            lock (_sync)
            {
                Ensure(_epoch);
                if (_epoch >= 64) throw new BrowserOperationException("OWNED_EPOCH_LIMIT");
                _token = Guid.NewGuid();
                start = NewEvent(CapturePhase.Draining); Apply(start); drain = _drained.Task;
            }
            try
            {
                var stop = StopReader();
                await Task.WhenAll(drain, stop, Journal(start)).WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                CaptureLifecycleEvent excluded;
                lock (_sync)
                {
                    if (_phase != CapturePhase.Draining) throw new BrowserOperationException("OWNED_SCAN_FAULTED");
                    _readerLease = stop.Result;
                    excluded = NewEvent(CapturePhase.Excluded);
                }
                await Journal(excluded).WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                lock (_sync) { Apply(excluded); _observed.Reset(); return new(_token, _epoch); }
            }
            catch { Fault("OWNED_EXCLUSION_UNCONFIRMED"); throw new BrowserOperationException("OWNED_EXCLUSION_UNCONFIRMED"); }
        }
        finally { _transition.Release(); }
    }

    public async Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync) if (_closing) throw new BrowserOperationException("OWNED_SCAN_CLOSED");
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            CaptureLifecycleEvent resuming;
            CaptureExclusionLease readerLease;
            lock (_sync)
            {
                if (_closing || _phase != CapturePhase.Excluded || lease.Token != _token || lease.Epoch != _epoch || _readerLease is null)
                    throw new BrowserOperationException("OWNED_LEASE_INVALID");
                readerLease = _readerLease; resuming = NewEvent(CapturePhase.Resuming); Apply(resuming);
            }
            try
            {
                await Journal(resuming).WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                lock (_sync) _stop = null;
                var baseline = await _reader.ResumeCaptureAsync(readerLease).WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                if (baseline.CaptureEpoch != _epoch + 1) throw new BrowserOperationException("OWNED_BASELINE_EPOCH_INVALID");
                var record = new OwnedScanBaseline(_store.ScanId, baseline.CaptureEpoch, baseline with { DocumentUrl = null });
                await WriteAsync(() => _store.WriteBaselineAsync(record)).WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                CaptureLifecycleEvent permitted;
                lock (_sync) { _epoch = baseline.CaptureEpoch; permitted = NewEvent(CapturePhase.Permitted); }
                await Journal(permitted).WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                lock (_sync) { Apply(permitted); _readerLease = null; _token = Guid.Empty; }
                return record.Snapshot;
            }
            catch { Fault("OWNED_RESUME_UNCONFIRMED"); throw new BrowserOperationException("OWNED_RESUME_UNCONFIRMED"); }
        }
        finally { _transition.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _closing = true;
            return new(_close ??= CloseCoreAsync());
        }
    }
    private async Task CloseCoreAsync()
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        var complete = false;
        try
        {
            Task drain;
            CaptureLifecycleEvent start;
            lock (_sync)
            {
                start = NewEvent(_phase == CapturePhase.Faulted ? CapturePhase.Faulted : CapturePhase.Draining);
                Apply(start);
                drain = _drained.Task;
            }
            try
            {
                var journal = Journal(start, "OWNED_CLOSE_STARTED");
                Task writes;
                lock (_sync) writes = _writesDrained.Task;
                if (_browser is IAsyncDisposable disposable)
                    await disposable.DisposeAsync().AsTask().WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                await Task.WhenAll(drain, journal, writes).WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                if (_stop is { IsCompleted: false } stop)
                    try { await stop.WaitAsync(TransitionTimeout, _time).ConfigureAwait(false); }
                    catch (BrowserOperationException) when (stop.IsCompleted) { /* Native close settled the failed stop. */ }
                CaptureLifecycleEvent closed;
                lock (_sync) closed = NewEvent(CapturePhase.Closed);
                await Journal(closed).WaitAsync(TransitionTimeout, _time).ConfigureAwait(false);
                lock (_sync) Apply(closed);
                _reader.Dispose(); _commands.Dispose();
                lock (OwnersLock) Owners.Remove(_browser);
                complete = true;
            }
            catch { Fault("OWNED_CLOSE_UNCONFIRMED"); throw new BrowserOperationException("OWNED_CLOSE_UNCONFIRMED"); }
        }
        finally
        {
            _transition.Release();
            if (complete) _transition.Dispose();
        }
    }
    private static TaskCompletionSource Completed()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); result.SetResult(); return result;
    }

    private sealed class ObservingBrowser(OwnedScanSession owner, IScreenReaderBrowser browser, ICaptureControl control)
        : IScreenReaderBrowser, ICaptureControl
    {
        public AccessibilitySnapshot? Snapshot { get; private set; }
        public EvidenceInterval? Window { get; private set; }
        public int InputRequests { get; private set; }
        public int CompletedInputCalls { get; private set; }
        public bool InputUncertain { get; private set; }
        public bool EpochMismatch { get; private set; }
        public CaptureStatus CaptureStatus => control.CaptureStatus;
        public Task<CaptureExclusionLease> ExcludeCaptureAsync() => control.ExcludeCaptureAsync();
        public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease) => control.ResumeCaptureAsync(lease);
        public void Reset() { Snapshot = null; Window = null; InputRequests = CompletedInputCalls = 0; InputUncertain = EpochMismatch = false; }
        public async Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            var before = owner.Elapsed();
            var snapshot = await browser.CaptureAsync(cancellationToken).ConfigureAwait(false);
            lock (owner._sync)
            {
                owner.Ensure(owner._epoch);
                if (snapshot.CaptureEpoch != owner._epoch) { EpochMismatch = true; throw new BrowserOperationException("OWNED_CAPTURE_EPOCH_MISMATCH"); }
                Snapshot = snapshot; Window = new(before, owner.Elapsed());
            }
            return snapshot;
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Input(() => browser.PressKeyAsync(key, cancellationToken));
        public Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken cancellationToken = default) =>
            Input(() => browser.PressListboxKeyAsync(input, cancellationToken));
        private async Task Input(Func<Task> dispatch)
        {
            InputRequests++;
            lock (owner._sync)
                if (owner._closing || owner._phase != CapturePhase.Permitted) throw new BrowserInputDeniedException("OWNED_SCAN_EXCLUDED");
            try { await dispatch().ConfigureAwait(false); CompletedInputCalls++; }
            catch (BrowserInputDeniedException) { throw; }
            catch { InputUncertain = true; throw; }
        }
    }
}
