using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>A transient fixture session. Expiry is enforced using monotonic elapsed time.</summary>
internal sealed record PortalReaderSessionResponse(Guid OrganizationId, Guid SessionId, string Fixture,
    string ReaderProfile, string State, DateTimeOffset ExpiresAt, bool ReadOnly);
/// <summary>Observed source semantics, without input, retained recording or a goal claim.</summary>
internal sealed record PortalReaderSnapshotResponse(Guid SessionId, string ReaderProfile,
    AccessibilitySnapshot Snapshot, int DispatchedInputs, bool GoalVerified);
/// <summary>Actual resource cleanup acknowledgement, not just a requested close.</summary>
internal sealed record PortalReaderClosedResponse(Guid SessionId, string State);

/// <summary>Host resource seam; implementations must join actual capture and shutdown work.</summary>
internal interface IPortalReaderBrowser : IAsyncDisposable
{
    Task<AccessibilitySnapshot> CaptureAsync();
}

internal sealed class PortalFixtureReaderBrowser(ChromiumFixtureBrowser browser) : IPortalReaderBrowser
{
    public Task<AccessibilitySnapshot> CaptureAsync() => browser.CaptureAsync(CancellationToken.None);
    public ValueTask DisposeAsync() => browser.DisposeAsync();
    public static async Task<IPortalReaderBrowser> OpenAsync() => new PortalFixtureReaderBrowser(
        await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Foundation).ConfigureAwait(false));
}

/// <summary>
/// Process-local read-only sessions, with fresh RLS authorization and bounded browser ownership.
/// These resources are deliberately separate from the durable scan queue and stdio MCP workspace.
/// </summary>
internal sealed class PortalReaderSessions : IAsyncDisposable
{
    internal const int MaximumSessions = 2;
    internal const int MaximumObservations = 100;
    internal const int MaximumNodes = 2048;
    internal const int MaximumResponseBytes = 262144;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly IPortalProjectClient _project;
    private readonly TimeProvider _time;
    private readonly Func<Task<IPortalReaderBrowser>> _open;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Entry> _sessions = [];
    private readonly PeriodicTimer _timer;
    private readonly Task _maintenance;
    private bool _disposed;

    public PortalReaderSessions(IPortalProjectClient project, TimeProvider time,
        Func<Task<IPortalReaderBrowser>>? open = null)
    {
        _project = project;
        _time = time;
        _open = open ?? PortalFixtureReaderBrowser.OpenAsync;
        _timer = new(TimeSpan.FromSeconds(1), time);
        _maintenance = MaintainAsync();
    }

    public async Task<PortalReaderSessionResponse> OpenAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            EnsureOpen();
            await AuthorizeAsync(actor, organization, token).ConfigureAwait(false);
            await ReapCoreAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (_sessions.Values.Any(entry => entry.User == actor.UserId)) throw new PortalFault(409, "CONTROL_READER_ALREADY_OPEN");
            if (_sessions.Count >= MaximumSessions) throw new PortalFault(409, "CONTROL_READER_CAPACITY_EXHAUSTED");
            // Once native launch begins, caller cancellation must not orphan its browser.
            var browser = await _open().ConfigureAwait(false);
            var entry = new Entry(actor.UserId, organization, Guid.NewGuid(), _time.GetTimestamp(),
                _time.GetUtcNow() + Lifetime, browser);
            _sessions.Add(entry.Id, entry);
            if (token.IsCancellationRequested)
            {
                await CloseCoreAsync(entry).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
            return Response(entry);
        }
        finally { _gate.Release(); }
    }

    public async Task<PortalReaderSessionResponse> ReadAsync(PortalIdentity actor, Guid organization, Guid session, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return Response(await FindAsync(actor, organization, session, token).ConfigureAwait(false)); }
        finally { _gate.Release(); }
    }

    public async Task<PortalReaderSnapshotResponse> ObserveAsync(PortalIdentity actor, Guid organization, Guid session, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var entry = await FindAsync(actor, organization, session, token).ConfigureAwait(false);
            if (entry.Observations >= MaximumObservations) throw new PortalFault(409, "CONTROL_READER_BUDGET_EXHAUSTED");
            entry.Observations++;
            try
            {
                // Join the actual capture rather than releasing the controller when the HTTP caller leaves.
                var snapshot = await entry.Browser.CaptureAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (Expired(entry))
                {
                    await CloseCoreAsync(entry).ConfigureAwait(false);
                    throw new PortalFault(404, "CONTROL_READER_NOT_FOUND");
                }
                var response = new PortalReaderSnapshotResponse(entry.Id, ScreenReaderSession.Profile, snapshot, 0, false);
                if (snapshot.Nodes.Length > MaximumNodes || JsonSerializer.SerializeToUtf8Bytes(response, PortalJson.Options).Length > MaximumResponseBytes)
                    throw new PortalFault(503, "CONTROL_READER_OBSERVATION_TOO_LARGE");
                return response;
            }
            catch
            {
                // A canceled, oversized or failed capture never leaves an uncertain active collector reusable.
                if (_sessions.ContainsKey(entry.Id)) await CloseCoreAsync(entry).ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<PortalReaderClosedResponse> CloseAsync(PortalIdentity actor, Guid organization, Guid session, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var entry = await FindAsync(actor, organization, session, token, allowQuarantined: true).ConfigureAwait(false);
            await CloseCoreAsync(entry).ConfigureAwait(false);
            return new(session, "Closed");
        }
        finally { _gate.Release(); }
    }

    private async Task<Entry> FindAsync(PortalIdentity actor, Guid organization, Guid session, CancellationToken token, bool allowQuarantined = false)
    {
        EnsureOpen();
        await AuthorizeAsync(actor, organization, token).ConfigureAwait(false);
        if (!_sessions.TryGetValue(session, out var entry) || entry.User != actor.UserId || entry.Organization != organization)
            throw new PortalFault(404, "CONTROL_READER_NOT_FOUND");
        if (Expired(entry) && !entry.Quarantined)
        {
            await CloseCoreAsync(entry).ConfigureAwait(false);
            throw new PortalFault(404, "CONTROL_READER_NOT_FOUND");
        }
        if (entry.Quarantined && !allowQuarantined) throw new PortalFault(503, "CONTROL_READER_CLOSE_UNCONFIRMED");
        token.ThrowIfCancellationRequested();
        return entry;
    }

    private async Task AuthorizeAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    {
        if (actor.UserId == Guid.Empty || organization == Guid.Empty
            || await _project.WorkspaceAsync(actor, organization, token).ConfigureAwait(false) is null)
            throw new PortalFault(404, "CONTROL_WORKSPACE_NOT_FOUND");
    }
    private void EnsureOpen()
    {
        if (_disposed) throw new PortalFault(503, "CONTROL_READER_HOST_CLOSED");
    }
    private bool Expired(Entry entry) => _time.GetElapsedTime(entry.OpenedAt) >= Lifetime;
    private static PortalReaderSessionResponse Response(Entry entry) => new(entry.Organization, entry.Id,
        "Foundation", ScreenReaderSession.Profile, "Open", entry.ExpiresAt, true);

    private async Task CloseCoreAsync(Entry entry)
    {
        try { await entry.Browser.DisposeAsync().ConfigureAwait(false); }
        catch
        {
            entry.Quarantined = true;
            throw new PortalFault(503, "CONTROL_READER_CLOSE_UNCONFIRMED");
        }
        _sessions.Remove(entry.Id);
    }

    internal async Task SweepAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) await ReapCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private async Task ReapCoreAsync()
    {
        foreach (var entry in _sessions.Values.Where(entry => !entry.Quarantined && Expired(entry)).ToArray())
            try { await CloseCoreAsync(entry).ConfigureAwait(false); }
            catch (PortalFault) { /* Quarantine retains capacity; later explicit close can retry. */ }
    }
    private async Task MaintainAsync()
    {
        while (await _timer.WaitForNextTickAsync().ConfigureAwait(false)) await SweepAsync().ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        _timer.Dispose();
        await _maintenance.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            var failures = new List<Exception>();
            foreach (var entry in _sessions.Values.ToArray())
                try { await CloseCoreAsync(entry).ConfigureAwait(false); }
                catch (PortalFault fault) { failures.Add(fault); }
            if (failures.Count > 0) throw new AggregateException("CONTROL_READER_CLOSE_UNCONFIRMED", failures);
        }
        finally { _gate.Release(); }
    }
    private sealed class Entry(Guid user, Guid organization, Guid id, long openedAt, DateTimeOffset expiresAt, IPortalReaderBrowser browser)
    {
        public Guid User { get; } = user;
        public Guid Organization { get; } = organization;
        public Guid Id { get; } = id;
        public long OpenedAt { get; } = openedAt;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public IPortalReaderBrowser Browser { get; } = browser;
        public int Observations { get; set; }
        public bool Quarantined { get; set; }
    }
}
