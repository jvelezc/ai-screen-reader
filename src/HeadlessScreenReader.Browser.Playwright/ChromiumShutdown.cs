using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>Escalates a stalled context close, then joins every actual native call. Wait timeouts do not drain work.</summary>
internal sealed class ChromiumShutdown(Func<Task> closeContext, Func<Task> closeBrowser, Action disposeTransport, TimeProvider? timeProvider = null,
    Func<Task>? stopCollectors = null)
{
    private readonly object _sync = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private Task? _actual;
    private BrowserShutdownPhase _phase;
    private bool _contextStarted, _browserStarted, _contextSettled, _browserSettled, _contextAck, _browserAck, _disposed, _escalated;
    private bool _collectorStarted, _collectorSettled, _collectorAck;
    private string? _error;
    public BrowserShutdownStatus Status
    {
        get
        {
            lock (_sync) return new(_phase, (_collectorStarted && !_collectorSettled ? 1 : 0)
                + (_contextStarted && !_contextSettled ? 1 : 0) + (_browserStarted && !_browserSettled ? 1 : 0),
                _contextAck, _browserAck, _contextSettled, _browserSettled, _disposed, _escalated, _error)
            { CollectorStopAcknowledged = _collectorAck, CollectorCallSettled = _collectorSettled };
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
        Task<bool>? collector = null, context = null, browser = null;
        try
        {
            lock (_sync) { _phase = BrowserShutdownPhase.StoppingCollectors; _collectorStarted = true; }
            collector = TrackAsync(stopCollectors ?? (() => Task.CompletedTask), ShutdownCall.Collector);
            try
            {
                if (!await collector.WaitAsync(TimeSpan.FromSeconds(5), _time).ConfigureAwait(false)) Fail("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED");
            }
            catch (TimeoutException) { Fail("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED"); }
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
                if (!await browser.WaitAsync(TimeSpan.FromSeconds(3), _time).ConfigureAwait(false))
                    Fail("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED");
            }
            catch (TimeoutException) { Fail("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED"); }
        }
        catch { Fail("CHROMIUM_SHUTDOWN_UNCONFIRMED"); }
        finally
        {
            try { disposeTransport(); lock (_sync) _disposed = true; }
            catch { Fail("CHROMIUM_TRANSPORT_CLOSE_UNCONFIRMED"); }
            lock (_sync) if (_error is null) _phase = BrowserShutdownPhase.DrainingCalls;
            // Disposal must settle callbacks too. Keep the actual close task pending if a native call refuses to settle.
            if (collector is not null) await collector.ConfigureAwait(false);
            if (context is not null) await context.ConfigureAwait(false);
            if (browser is not null) await browser.ConfigureAwait(false);
            lock (_sync)
            {
                if (!_collectorAck || !_collectorSettled || !_browserAck || !_contextSettled || !_browserSettled || !_disposed) Fail("CHROMIUM_SHUTDOWN_UNCONFIRMED");
                if (_error is null) { _phase = BrowserShutdownPhase.Closed; completion.TrySetResult(); }
                else { _phase = BrowserShutdownPhase.Faulted; completion.TrySetException(new BrowserOperationException(_error)); }
            }
        }
    }
    private enum ShutdownCall { Collector, Context, Browser }
    private async Task<bool> TrackAsync(Func<Task> operation, ShutdownCall call)
    {
        var acknowledged = false;
        try { await operation().ConfigureAwait(false); acknowledged = true; return true; }
        catch { return false; }
        finally
        {
            lock (_sync)
            {
                if (call == ShutdownCall.Collector) { _collectorSettled = true; _collectorAck = acknowledged; }
                else if (call == ShutdownCall.Context) { _contextSettled = true; _contextAck = acknowledged; }
                else { _browserSettled = true; _browserAck = acknowledged; }
            }
        }
    }
    private void Fail(string code) { lock (_sync) { _error ??= code; _phase = BrowserShutdownPhase.Faulted; } }
}
