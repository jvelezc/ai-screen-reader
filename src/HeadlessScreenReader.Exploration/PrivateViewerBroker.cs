using System.Security.Cryptography;
using System.Text;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>Trusted composition supplies authenticated subject identity. No identity provider or input authority.</summary>
public sealed class PrivateViewerBroker : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly ViewerScope _scope;
    private readonly Guid _subject;
    private readonly TimeProvider _time;
    private readonly int _maximumGrants;
    private readonly Dictionary<string, BootstrapState> _bootstraps = [];
    private readonly Dictionary<Guid, ConnectionState> _connections = [];
    private bool _closed;
    private int _reads;
    private Task? _close;

    public PrivateViewerBroker(ViewerScope scope, Guid authorizedSubject, TimeProvider? timeProvider = null, int maximumGrants = 16)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!CanonicalId(scope.ScanId) || !CanonicalId(scope.SessionId) || authorizedSubject == Guid.Empty)
            throw new ArgumentException("VIEWER_SCOPE_INVALID");
        if (maximumGrants is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maximumGrants));
        _scope = scope; _subject = authorizedSubject; _time = timeProvider ?? TimeProvider.System; _maximumGrants = maximumGrants;
    }
    public ViewerBrokerStatus Status
    {
        get { lock (_sync) { ExpireConnections(); return new(_scope, _bootstraps.Count, _connections.Values.Count(c => c.Phase == ViewerConnectionPhase.Active), _reads, _closed); } }
    }
    public ViewerBootstrap IssueBootstrap(Guid authenticatedSubject, ViewerScope scope, TimeSpan lifetime)
    {
        Duration(lifetime, 60);
        lock (_sync)
        {
            Authorize(authenticatedSubject, scope);
            if (_bootstraps.Count >= _maximumGrants) throw new BrowserOperationException("VIEWER_GRANT_LIMIT");
            var secret = NewSecret();
            _bootstraps.Add(Digest(secret.Value), new(_time.GetTimestamp(), lifetime));
            return new(secret, _time.GetUtcNow() + lifetime);
        }
    }
    public ViewerConnection Exchange(Guid authenticatedSubject, ViewerScope scope, string bootstrap, TimeSpan lifetime)
    {
        Duration(lifetime, 120);
        lock (_sync)
        {
            Authorize(authenticatedSubject, scope);
            if (!ValidSecret(bootstrap)) Denied();
            if (!_bootstraps.TryGetValue(Digest(bootstrap), out var ticket)
                || ticket.Used || _time.GetElapsedTime(ticket.Started) >= ticket.Lifetime) Denied();
            ticket.Used = true;
            var secret = NewSecret(); var id = Guid.NewGuid();
            var state = new ConnectionState(Digest(secret.Value), _time.GetTimestamp(), lifetime);
            _connections.Add(id, state);
            state.Timer = _time.CreateTimer(_ => { lock (_sync) Expire(state); }, null, lifetime, Timeout.InfiniteTimeSpan);
            return new(id, secret, _time.GetUtcNow() + lifetime);
        }
    }
    /// <summary>The returned task is actual source and delivery work. A cancelled wait cannot drain it.</summary>
    public Task DeliverAsync(Guid authenticatedSubject, ViewerScope scope, Guid connectionId, string secret, long epoch,
        Func<Func<RetainedViewerObservation, Task>, CancellationToken, Task> source,
        Func<RetainedViewerObservation, CancellationToken, Task> deliver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(deliver);
        cancellationToken.ThrowIfCancellationRequested();
        ConnectionState state;
        lock (_sync)
        {
            state = RequireConnection(authenticatedSubject, scope, connectionId, secret);
            if (epoch < 1) throw new BrowserOperationException("VIEWER_EPOCH_INVALID");
            if (state.Reads != 0 || _reads >= 4) throw new BrowserOperationException("VIEWER_READ_BUSY");
            state.Reads++; _reads++; state.Drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return DeliverCoreAsync(state, epoch, source, deliver, cancellationToken);
    }
    private async Task DeliverCoreAsync(ConnectionState state, long epoch,
        Func<Func<RetainedViewerObservation, Task>, CancellationToken, Task> source,
        Func<RetainedViewerObservation, CancellationToken, Task> deliver, CancellationToken caller)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, state.Stop.Token);
        var publications = 0;
        var sourceSettled = false;
        Task? dispatched = null;
        try
        {
            try
            {
                await source(observation =>
                {
                    lock (_sync)
                    {
                        Expire(state);
                        if (_closed || state.Phase != ViewerConnectionPhase.Active || sourceSettled) Denied();
                        linked.Token.ThrowIfCancellationRequested();
                        if (++publications != 1 || observation is null || observation.Ordinal < 0 || observation.ScanId != _scope.ScanId || observation.Epoch != epoch
                            || observation.Snapshot is null || observation.Snapshot.CaptureEpoch != epoch || observation.Snapshot.DocumentUrl is not null)
                            throw new BrowserOperationException("VIEWER_SOURCE_INVALID");
                        // Check and actual transport-dispatch start share one lock. Revocation drains this actual task.
                        return dispatched = deliver(observation, linked.Token);
                    }
                }, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                Task? send;
                lock (_sync) { sourceSettled = true; send = dispatched; }
                // Even a source that forgets to await its sink cannot release actual delivery work.
                if (send is not null) await send.ConfigureAwait(false);
            }
            if (publications != 1) throw new BrowserOperationException("VIEWER_SOURCE_UNAVAILABLE");
        }
        finally
        {
            lock (_sync) { state.Reads--; _reads--; state.Drained.TrySetResult(); }
        }
    }
    public Task RevokeAsync(Guid authenticatedSubject, ViewerScope scope, Guid connectionId, string secret, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var state = RequireConnection(authenticatedSubject, scope, connectionId, secret, allowStopped: true);
            return WaitStopAsync(StartStop(state), cancellationToken);
        }
    }
    private async Task WaitStopAsync(Task actual, CancellationToken token)
    {
        try { await actual.WaitAsync(TimeSpan.FromSeconds(5), _time, token).ConfigureAwait(false); }
        catch (TimeoutException) { throw new BrowserOperationException("VIEWER_STOP_UNCONFIRMED"); }
    }
    private Task StartStop(ConnectionState state)
    {
        if (state.Stopping is not null) return state.Stopping;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Stopping = completion.Task;
        _ = completion.Task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        state.Phase = ViewerConnectionPhase.Revoking; state.Timer?.Dispose(); state.Timer = null;
        try { state.Stop.Cancel(); }
        catch { state.Phase = ViewerConnectionPhase.Faulted; }
        _ = FinishStopAsync(state, completion);
        return completion.Task;
    }
    private async Task FinishStopAsync(ConnectionState state, TaskCompletionSource completion)
    {
        try
        {
            await state.Drained.Task.WaitAsync(TimeSpan.FromSeconds(5), _time).ConfigureAwait(false);
            lock (_sync)
            {
                if (state.Phase == ViewerConnectionPhase.Faulted) throw new BrowserOperationException("VIEWER_STOP_UNCONFIRMED");
                state.Phase = ViewerConnectionPhase.Revoked;
            }
            completion.TrySetResult();
        }
        catch { lock (_sync) state.Phase = ViewerConnectionPhase.Faulted; completion.TrySetException(new BrowserOperationException("VIEWER_STOP_UNCONFIRMED")); }
    }
    private ConnectionState RequireConnection(Guid subject, ViewerScope scope, Guid id, string secret, bool allowStopped = false)
    {
        Authorize(subject, scope);
        if (!ValidSecret(secret)) Denied();
        if (!_connections.TryGetValue(id, out var state) || !FixedDigestEquals(state.Digest, Digest(secret))) Denied();
        Expire(state);
        if (!allowStopped && state.Phase != ViewerConnectionPhase.Active) Denied();
        return state;
    }
    private void Expire(ConnectionState state)
    {
        if (state.Phase == ViewerConnectionPhase.Active && _time.GetElapsedTime(state.Started) >= state.Lifetime)
        {
            var stop = StartStop(state);
            _ = stop.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
    private void ExpireConnections() { foreach (var state in _connections.Values) Expire(state); }
    private void Authorize(Guid subject, ViewerScope scope) { if (_closed || subject != _subject || scope != _scope) Denied(); }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Denied() => throw new BrowserOperationException("VIEWER_AUTHORITY_DENIED");
    private static void Duration(TimeSpan value, int maximum) { if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromSeconds(maximum)) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static bool CanonicalId(string value) => Guid.TryParseExact(value, "N", out var parsed) && parsed.ToString("N") == value;
    private static bool ValidSecret(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static ViewerSecret NewSecret() => new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static bool FixedDigestEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _closed = true;
            return new(_close ??= WaitStopAsync(Task.WhenAll(_connections.Values.Select(StartStop)), CancellationToken.None));
        }
    }
    private sealed class BootstrapState(long started, TimeSpan lifetime)
    {
        public long Started { get; } = started;
        public TimeSpan Lifetime { get; } = lifetime;
        public bool Used { get; set; }
    }
    private sealed class ConnectionState(string digest, long started, TimeSpan lifetime)
    {
        public string Digest { get; } = digest;
        public long Started { get; } = started;
        public TimeSpan Lifetime { get; } = lifetime;
        public ViewerConnectionPhase Phase { get; set; } = ViewerConnectionPhase.Active;
        public int Reads { get; set; }
        public ITimer? Timer { get; set; }
        public CancellationTokenSource Stop { get; } = new();
        public Task? Stopping { get; set; }
        public TaskCompletionSource Drained { get; set; } = Completed();
        private static TaskCompletionSource Completed() { var value = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); value.SetResult(); return value; }
    }
}
