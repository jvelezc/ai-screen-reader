using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Native protocol acknowledgement and confirmed owned-container removal are independent facts.</summary>
public sealed record ReaderRetirement(bool NativeShutdownConfirmed, bool ContainerAbsenceConfirmed);

/// <summary>Exclusive reader ownership, deliberately separate from the queue worker's .runtime journal.</summary>
public sealed class IsolatedReaderSupervisor : IAsyncDisposable
{
    private readonly OwnershipJournal _journal;
    private readonly FileStream _exclusive;
    private readonly IsolatedFixtureExecutor _executor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Reservation> _sessions = [];
    private readonly PeriodicTimer _timer;
    private Task _maintenance = Task.CompletedTask;
    private bool _closing;
    private bool _disposed;
    public int Recovered { get; private set; }
    public string JournalRoot { get; }

    private IsolatedReaderSupervisor(string root, string runtime, string image, string? seccomp)
    {
        JournalRoot = Path.Combine(Path.GetFullPath(root), ".reader-runtime");
        _journal = new OwnershipJournal(JournalRoot);
        _exclusive = _journal.AcquireExclusive();
        try
        {
            _executor = new(runtime, image, seccomp, _journal);
            _timer = new(TimeSpan.FromSeconds(1));
        }
        catch { _exclusive.Dispose(); throw; }
    }

    public static async Task<IsolatedReaderSupervisor> StartAsync(string root, string runtime, string image, string? seccomp = null)
    {
        var supervisor = new IsolatedReaderSupervisor(root, runtime, image, seccomp);
        try
        {
            // Complete recovery before admitting a native launch. The exclusive reader lock is retained.
            supervisor.Recovered = await supervisor._executor.RecoverAsync().ConfigureAwait(false);
            supervisor._maintenance = supervisor.MaintainAsync();
            return supervisor;
        }
        catch { supervisor._exclusive.Dispose(); supervisor._timer.Dispose(); throw; }
    }

    public async Task<IsolatedReaderSession> OpenAsync(Guid organization, Guid session, CancellationToken token = default)
    {
        if (organization == Guid.Empty || session == Guid.Empty) throw new ArgumentException("READER_SESSION_ID_INVALID");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_closing) throw new BrowserOperationException("READER_SUPERVISOR_CLOSED");
            await SweepCoreAsync().ConfigureAwait(false);
            if (_sessions.ContainsKey(session)) throw new BrowserOperationException("READER_SESSION_ALREADY_OPEN");
            if (_sessions.Count >= 2 || _sessions.Values.Any(value => value.Reader is null))
                throw new BrowserOperationException("READER_SUPERVISOR_CAPACITY_UNCONFIRMED");
            token.ThrowIfCancellationRequested();
            _sessions.Add(session, new(organization)); // Reserve before the durable intent or native create can begin.
            try
            {
                // These nonsecret ownership labels do not create or claim a queue job or scan.
                var descriptor = new FixtureJob(organization, session, session, 1);
                var reader = await _executor.OpenReaderAsync(descriptor, session, token).ConfigureAwait(false);
                _sessions[session].Reader = reader;
                return reader;
            }
            catch
            {
                // Unknown native ownership keeps its reservation, even when Open never returned a session.
                try
                {
                    if (!_journal.ReadPending().Any(intent => intent.Job.ScanId == session || intent.Job.JobId == session))
                        _sessions.Remove(session);
                }
                catch (Exception) { /* Unreadable durable ownership also retains the reservation. */ }
                throw new BrowserOperationException("READER_CONTAINER_OPEN_UNCONFIRMED");
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Retire only this organization's session, including a failed launch. Never recover the whole live journal.</summary>
    public async Task<ReaderRetirement> RetireAsync(Guid organization, Guid session)
    {
        if (organization == Guid.Empty || session == Guid.Empty) throw new ArgumentException("READER_SESSION_ID_INVALID");
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Startup reconciles the complete inventory; entries leave this map only after confirmed removal.
            if (!_sessions.TryGetValue(session, out var reservation))
            {
                if (_journal.ReadPending().Any(intent => intent.Job.ScanId == session || intent.Job.JobId == session))
                    throw new BrowserOperationException("READER_SESSION_OWNERSHIP_UNCONFIRMED");
                return new(false, true);
            }
            if (reservation.Organization != organization) throw new BrowserOperationException("READER_SESSION_NOT_FOUND");
            var native = false;
            if (reservation.Reader is { } reader)
            {
                try { await reader.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) when (reader.ContainerAbsenceConfirmed) { /* Preserve the missing native acknowledgement below. */ }
                if (!reader.ContainerAbsenceConfirmed) throw new BrowserOperationException("READER_CONTAINER_CLEANUP_UNCONFIRMED");
                native = reader.NativeShutdownConfirmed;
            }
            else
            {
                await _executor.RecoverReaderAsync(new(organization, session, session, 1)).ConfigureAwait(false);
            }
            _sessions.Remove(session);
            return new(native, true);
        }
        finally { _gate.Release(); }
    }

    private async Task SweepCoreAsync()
    {
        foreach (var pair in _sessions.ToArray())
        {
            if (pair.Value.Reader is not { } reader) continue;
            if (reader.Expired)
                try { await reader.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            if (reader.ContainerAbsenceConfirmed) _sessions.Remove(pair.Key);
        }
    }

    private async Task MaintainAsync()
    {
        while (await _timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await SweepCoreAsync().ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _timer.Dispose();
        await _maintenance.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _closing = true;
            foreach (var pair in _sessions.ToArray())
            {
                if (pair.Value.Reader is { } reader)
                {
                    try { await reader.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception) when (reader.ContainerAbsenceConfirmed) { }
                }
            }
            // All attached clients are settled before recovering a failed launch's retained intent.
            await _executor.RecoverAsync().ConfigureAwait(false);
            _sessions.Clear();
            _exclusive.Dispose();
            _disposed = true;
        }
        finally { _gate.Release(); }
    }

    private sealed class Reservation(Guid organization)
    {
        public Guid Organization { get; } = organization;
        public IsolatedReaderSession? Reader { get; set; }
    }
}
