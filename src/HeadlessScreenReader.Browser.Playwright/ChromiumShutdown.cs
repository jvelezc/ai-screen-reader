using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>Escalates a stalled context close, then joins every actual native call. Wait timeouts do not drain work.</summary>
internal sealed class ChromiumShutdown(Func<Task> closeContext, Func<Task> closeBrowser, Action disposeTransport, TimeProvider? timeProvider = null,
    Func<Task>? stopCollectors = null, Func<Task>? waitProcessExit = null, Func<Task>? stopOwnedProcess = null)
{
    private readonly object _sync = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private Task? _actual;
    private BrowserShutdownPhase _phase;
    private bool _contextStarted, _browserStarted, _contextSettled, _browserSettled, _contextAck, _browserAck, _disposed, _escalated;
    private bool _collectorStarted, _collectorSettled, _collectorAck;
    private bool _browserDeadline, _browserRejected;
    private bool _processStarted, _processSettled, _processAck, _transportEscalated, _browserAckBeforeTransport;
    private bool _transportRequested, _transportBeforeExit;
    private bool _transportSettled;
    private Task? _transportTask;
    private long? _nativeOrigin;
    private double? _transportDuration, _processConfirmation;
    private bool _ownedStopStarted, _ownedStopSettled, _ownedStopAck;
    private double? _ownedStopDuration;
    private string? _error;
    private double? _nativeElapsedMilliseconds;
    public BrowserShutdownStatus Status
    {
        get
        {
            lock (_sync) return new(_phase, (_collectorStarted && !_collectorSettled ? 1 : 0)
                + (_contextStarted && !_contextSettled ? 1 : 0) + (_browserStarted && !_browserSettled ? 1 : 0)
                + (_processStarted && !_processSettled ? 1 : 0) + (_transportRequested && !_transportSettled ? 1 : 0)
                + (_ownedStopStarted && !_ownedStopSettled ? 1 : 0),
                _contextAck, _browserAck, _contextSettled, _browserSettled, _disposed, _escalated, _error)
            {
                CollectorStopAcknowledged = _collectorAck,
                CollectorCallSettled = _collectorSettled,
                BrowserDeadlineExceeded = _browserDeadline,
                BrowserCallRejected = _browserRejected,
                ProcessExitConfirmed = _processAck,
                ProcessCallSettled = _processSettled,
                TransportEscalatedAfterProcessExit = _transportEscalated,
                TransportEscalatedBeforeProcessExit = _transportBeforeExit,
                BrowserCloseAcknowledgedBeforeTransport = _browserAckBeforeTransport,
                NativeElapsedMilliseconds = _nativeElapsedMilliseconds,
                TransportCallSettled = _transportSettled,
                TransportStopDurationMilliseconds = _transportDuration,
                ProcessExitConfirmationMilliseconds = _processConfirmation,
                OwnedProcessStopRequested = _ownedStopStarted,
                OwnedProcessStopCallSettled = _ownedStopSettled,
                OwnedProcessStopAcknowledged = _ownedStopAck,
                OwnedProcessStopDurationMilliseconds = _ownedStopDuration
            };
        }
    }
    public Task CloseAsync()
    {
        lock (_sync)
        {
            if (_actual is not null) return _actual;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _actual = completion.Task;
            _ = _actual.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            _ = CloseCoreAsync(completion);
            return _actual;
        }
    }
    private async Task CloseCoreAsync(TaskCompletionSource completion)
    {
        Task<bool>? collector = null, context = null, browser = null, process = null, ownedStop = null;
        long? nativeStarted = null;
        try
        {
            lock (_sync) { _phase = BrowserShutdownPhase.StoppingCollectors; _collectorStarted = true; }
            collector = TrackAsync(stopCollectors ?? (() => Task.CompletedTask), ShutdownCall.Collector);
            try
            {
                if (!await collector.WaitAsync(TimeSpan.FromSeconds(5), _time).ConfigureAwait(false)) Fail("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED");
            }
            catch (TimeoutException) { Fail("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED"); }
            nativeStarted = _time.GetTimestamp();
            lock (_sync) _nativeOrigin = nativeStarted;
            if (waitProcessExit is not null)
            {
                lock (_sync) _processStarted = true;
                process = TrackAsync(waitProcessExit, ShutdownCall.Process);
            }
            lock (_sync) { if (_error is null) _phase = BrowserShutdownPhase.ClosingContext; _contextStarted = true; }
            context = TrackAsync(closeContext, ShutdownCall.Context);
            try
            {
                if (!await context.WaitAsync(TimeSpan.FromSeconds(1), _time).ConfigureAwait(false))
                    lock (_sync) _escalated = true;
            }
            catch (TimeoutException) { lock (_sync) _escalated = true; }
            lock (_sync) { if (_error is null) _phase = BrowserShutdownPhase.ClosingBrowser; _browserStarted = true; }
            browser = TrackAsync(closeBrowser, ShutdownCall.Browser);
            try
            {
                // Context and browser share the same four-second native budget. Unused context time is not discarded.
                var remaining = TimeSpan.FromSeconds(4) - _time.GetElapsedTime(nativeStarted.Value);
                if (remaining <= TimeSpan.Zero && (process is null || !ProcessConfirmedInBudget()))
                {
                    lock (_sync) _browserDeadline = true;
                    Fail("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED");
                }
                else if (process is null)
                {
                    if (!await browser.WaitAsync(remaining, _time).ConfigureAwait(false)) Fail("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED");
                }
                else
                {
                    // Reserve half the existing native budget for our driver's shutdown and OS confirmation.
                    // This is a soft grace period, not acknowledgement of a still-running process.
                    var grace = TimeSpan.FromSeconds(2) - _time.GetElapsedTime(nativeStarted.Value);
                    if (grace > TimeSpan.Zero)
                    {
                        try
                        {
                            var first = await Task.WhenAny(browser, process).WaitAsync(grace, _time).ConfigureAwait(false);
                            if (first == browser && !await browser.ConfigureAwait(false))
                            { Fail("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED"); return; }
                            grace = TimeSpan.FromSeconds(2) - _time.GetElapsedTime(nativeStarted.Value);
                            if (!process.IsCompleted && grace > TimeSpan.Zero)
                                await process.WaitAsync(grace, _time).ConfigureAwait(false);
                        }
                        catch (TimeoutException) { /* Soft escalation only; the total native deadline remains four seconds. */ }
                    }
                    if (!process.IsCompleted && !ProcessConfirmedInBudget())
                    {
                        if (stopOwnedProcess is not null)
                        {
                            lock (_sync) _ownedStopStarted = true;
                            ownedStop = TrackAsync(() => Task.Factory.StartNew(stopOwnedProcess, CancellationToken.None,
                                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap(), ShutdownCall.OwnedStop);
                            remaining = TimeSpan.FromSeconds(4) - _time.GetElapsedTime(nativeStarted.Value);
                            if (remaining <= TimeSpan.Zero) throw new TimeoutException();
                            var stopped = await Task.WhenAny(ownedStop, process).WaitAsync(remaining, _time).ConfigureAwait(false);
                            if (stopped == ownedStop && !await ownedStop.ConfigureAwait(false))
                            { lock (_sync) _transportBeforeExit = true; _ = StopOwnedTransport(); }
                        }
                        else
                        {
                            lock (_sync) _transportBeforeExit = true;
                            _ = StopOwnedTransport();
                        }
                    }
                    // SDK acknowledgement alone is not process exit. The OS watch remains actual work too.
                    remaining = TimeSpan.FromSeconds(4) - _time.GetElapsedTime(nativeStarted.Value);
                    bool exited;
                    if (process.IsCompleted || ProcessConfirmedInBudget()) exited = await process.ConfigureAwait(false);
                    else
                    {
                        if (remaining <= TimeSpan.Zero) throw new TimeoutException();
                        exited = await process.WaitAsync(remaining, _time).ConfigureAwait(false);
                    }
                    if (!exited) Fail("CHROMIUM_PROCESS_EXIT_UNCONFIRMED");
                    // A task completed before WaitAsync registers its timer can still be a late observation.
                    else if (!ProcessConfirmedInBudget()) throw new TimeoutException();
                    lock (_sync)
                    {
                        if (_browserRejected) Fail("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED");
                        // Positive kernel exit permits stopping our driver before its SDK finalization response.
                        _transportEscalated = !_transportRequested && _processAck && !browser.IsCompleted;
                    }
                }
            }
            catch (TimeoutException)
            {
                if (process is null || !ProcessConfirmedInBudget())
                { lock (_sync) _browserDeadline = true; Fail("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED"); }
            }
        }
        catch { Fail("CHROMIUM_SHUTDOWN_UNCONFIRMED"); }
        finally
        {
            var transport = StopOwnedTransport();
            lock (_sync) if (_error is null) _phase = BrowserShutdownPhase.DrainingCalls;
            // Disposal must settle callbacks too. Keep the actual close task pending if a native call refuses to settle.
            await transport.ConfigureAwait(false);
            if (collector is not null) await collector.ConfigureAwait(false);
            if (context is not null) await context.ConfigureAwait(false);
            if (browser is not null) await browser.ConfigureAwait(false);
            if (process is not null) await process.ConfigureAwait(false);
            if (ownedStop is not null) await ownedStop.ConfigureAwait(false);
            lock (_sync)
            {
                _nativeElapsedMilliseconds = nativeStarted is { } nativeStart ? _time.GetElapsedTime(nativeStart).TotalMilliseconds : null;
                if (_browserRejected) Fail("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED");
                if (_ownedStopStarted && !_ownedStopAck) Fail("CHROMIUM_OWNED_ROOT_STOP_UNCONFIRMED");
                if (!_collectorAck || !_collectorSettled || !_browserAck || !_contextSettled || !_browserSettled || !_disposed
                    || process is not null && (!_processAck || !_processSettled)) Fail("CHROMIUM_SHUTDOWN_UNCONFIRMED");
                if (_error is null) { _phase = BrowserShutdownPhase.Closed; completion.TrySetResult(); }
                else
                {
                    _phase = BrowserShutdownPhase.Faulted;
                    var facts = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Shutdown = Status,
                        NativeElapsedMilliseconds = nativeStarted is { } start ? _time.GetElapsedTime(start).TotalMilliseconds : (double?)null
                    }, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
                    completion.TrySetException(new BrowserOperationException(_error, new InvalidOperationException(facts)));
                }
            }
        }
    }
    private bool ProcessConfirmedInBudget()
    {
        lock (_sync) return _processAck && _processConfirmation is >= 0 and < 4000;
    }
    private Task StopOwnedTransport()
    {
        lock (_sync)
        {
            if (_transportTask is not null) return _transportTask;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _transportTask = completion.Task;
            _transportRequested = true;
            _browserAckBeforeTransport = _browserAck;
            // IDisposable is synchronous. Keep its actual worker and leave the OS-exit watchdog responsive.
            try
            {
                _ = Task.Factory.StartNew(() =>
                {
                    var started = _time.GetTimestamp();
                    try { disposeTransport(); lock (_sync) _disposed = true; }
                    catch { Fail("CHROMIUM_TRANSPORT_CLOSE_UNCONFIRMED"); }
                    finally
                    {
                        lock (_sync) { _transportSettled = true; _transportDuration = _time.GetElapsedTime(started).TotalMilliseconds; }
                        completion.TrySetResult();
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            }
            catch
            {
                _transportSettled = true;
                Fail("CHROMIUM_TRANSPORT_CLOSE_UNCONFIRMED");
                completion.TrySetResult();
            }
            return _transportTask;
        }
    }
    private enum ShutdownCall { Collector, Context, Browser, Process, OwnedStop }
    private async Task<bool> TrackAsync(Func<Task> operation, ShutdownCall call)
    {
        var acknowledged = false;
        var started = _time.GetTimestamp();
        try { await operation().ConfigureAwait(false); acknowledged = true; return true; }
        catch { return false; }
        finally
        {
            lock (_sync)
            {
                if (call == ShutdownCall.Collector) { _collectorSettled = true; _collectorAck = acknowledged; }
                else if (call == ShutdownCall.Context) { _contextSettled = true; _contextAck = acknowledged; }
                else if (call == ShutdownCall.Browser) { _browserSettled = true; _browserAck = acknowledged; _browserRejected = !acknowledged; }
                else if (call == ShutdownCall.Process)
                {
                    _processSettled = true; _processAck = acknowledged;
                    if (acknowledged && _nativeOrigin is { } origin) _processConfirmation = _time.GetElapsedTime(origin).TotalMilliseconds;
                }
                else
                {
                    _ownedStopSettled = true; _ownedStopAck = acknowledged;
                    _ownedStopDuration = _time.GetElapsedTime(started).TotalMilliseconds;
                    if (!acknowledged) Fail("CHROMIUM_OWNED_ROOT_STOP_UNCONFIRMED");
                }
            }
        }
    }
    private void Fail(string code) { lock (_sync) { _error ??= code; _phase = BrowserShutdownPhase.Faulted; } }
}
