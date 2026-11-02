using System.Diagnostics;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record PublicReaderRetirement(Guid OrganizationId, Guid SessionId, bool ResourcesAbsenceConfirmed, bool NativeShutdownConfirmed);
internal sealed record PublicReaderResourceRemoval(string Schema, Guid Owner, string Runtime, string Image, bool ContainersAbsent, bool NetworksAbsent);

/// <summary>Trusted host composition for a bounded public reader; caller authority remains with its API/job owner.</summary>
public sealed class IsolatedPublicReaderSupervisor : IAsyncDisposable
{
    private readonly PublicReaderOwnershipJournal _journal;
    private readonly FileStream _exclusive;
    private readonly string _runtime, _image, _scripts;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Reservation> _sessions = [];
    private readonly PeriodicTimer _timer;
    private Task _maintenance = Task.CompletedTask;
    private bool _closing, _disposed;
    public string JournalRoot { get; }
    public int Recovered { get; private set; }

    private IsolatedPublicReaderSupervisor(string root, string scripts, string runtime, string image)
    {
        // Validate configuration before a journal or native process can be created.
        PublicReaderOwnershipJournal.Validate(new(PublicReaderOwnership.CurrentSchema, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            runtime, image, new(PublicReaderSource.CurrentProfile, "https://screenreader.privensic.com/ci/v1/source", "screenreader.privensic.com")));
        _scripts = Path.GetFullPath(scripts);
        foreach (var file in new[] { "Start-IsolatedPublicReader.ps1", "Stop-IsolatedPublicReader.ps1", "ReaderIsolation.psm1" })
            if (!File.Exists(Path.Combine(_scripts, file))) throw new ArgumentException("PUBLIC_READER_LAUNCHER_REQUIRED");
        _runtime = runtime; _image = image;
        JournalRoot = Path.Combine(Path.GetFullPath(root), ".public-reader-runtime");
        _journal = new(JournalRoot); _exclusive = _journal.AcquireExclusive();
        _timer = new(TimeSpan.FromSeconds(1));
    }

    public static async Task<IsolatedPublicReaderSupervisor> StartAsync(string root, string scripts, string runtime, string image)
    {
        var supervisor = new IsolatedPublicReaderSupervisor(root, scripts, runtime, image);
        try
        {
            var pending = supervisor._journal.ReadPending();
            if (pending.Any(intent => intent.Runtime != runtime)) throw new InvalidDataException("PUBLIC_READER_RECOVERY_RUNTIME_MISMATCH");
            foreach (var intent in pending)
            {
                await supervisor.RemoveAsync(intent, recovered: true).ConfigureAwait(false); supervisor.Recovered++;
            }
            supervisor._maintenance = supervisor.MaintainAsync(); return supervisor;
        }
        catch { supervisor._exclusive.Dispose(); supervisor._timer.Dispose(); throw; }
    }

    public async Task<IsolatedPublicReaderSession> OpenAsync(Guid organization, Guid session, PublicReaderSource source, CancellationToken token = default)
    {
        if (organization == Guid.Empty || session == Guid.Empty) throw new ArgumentException("PUBLIC_READER_SESSION_ID_INVALID");
        source.Validate();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_closing) throw new BrowserOperationException("PUBLIC_READER_SUPERVISOR_CLOSED");
            await SweepAsync().ConfigureAwait(false);
            if (_sessions.ContainsKey(session)) throw new BrowserOperationException("PUBLIC_READER_SESSION_ALREADY_OPEN");
            if (_sessions.Count >= 2 || _sessions.Values.Any(value => value.Reader is null))
                throw new BrowserOperationException("PUBLIC_READER_CAPACITY_UNCONFIRMED");
            token.ThrowIfCancellationRequested();
            var reservation = new Reservation(organization); _sessions.Add(session, reservation);
            try
            {
                var intent = await _journal.BeginAsync(organization, session, source, _runtime, _image, token).ConfigureAwait(false);
                reservation.Intent = intent;
                var start = Launch("Start-IsolatedPublicReader.ps1", intent);
                start.ArgumentList.Add("-AllowedHost"); start.ArgumentList.Add(source.AllowedHost);
                start.ArgumentList.Add("-TargetUrl"); start.ArgumentList.Add(source.TargetUrl);
                var reader = new IsolatedPublicReaderSession(start, intent, () => RemoveAsync(intent, recovered: false));
                reservation.Reader = reader;
                await reader.OpenAsync(token).ConfigureAwait(false); return reader;
            }
            catch
            {
                try
                {
                    if (!_journal.ReadPending().Any(intent => intent.OrganizationId == organization && intent.SessionId == session))
                        _sessions.Remove(session);
                }
                catch (Exception) { /* Corrupt or uncertain durable ownership retains capacity. */ }
                throw new BrowserOperationException("PUBLIC_READER_OPEN_UNCONFIRMED");
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<PublicReaderRetirement?> RetireAsync(Guid organization, Guid session)
    {
        if (organization == Guid.Empty || session == Guid.Empty) throw new ArgumentException("PUBLIC_READER_SESSION_ID_INVALID");
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) throw new BrowserOperationException("PUBLIC_READER_SUPERVISOR_CLOSED");
            if (_sessions.TryGetValue(session, out var reservation) && reservation.Organization != organization)
                throw new BrowserOperationException("PUBLIC_READER_SCOPE_DENIED");
            var pending = _journal.ReadPending();
            if (pending.Any(intent => intent.SessionId == session && intent.OrganizationId != organization))
                throw new BrowserOperationException("PUBLIC_READER_SCOPE_DENIED");
            if (reservation?.Reader is { } reader)
            {
                try { await reader.DisposeAsync().ConfigureAwait(false); }
                catch (BrowserOperationException) when (reader.ResourcesAbsenceConfirmed) { }
                if (!reader.ResourcesAbsenceConfirmed) throw new BrowserOperationException("PUBLIC_READER_CLEANUP_UNCONFIRMED");
                _sessions.Remove(session);
                return new(organization, session, true, reader.NativeShutdownConfirmed);
            }
            var own = pending.Where(intent => intent.OrganizationId == organization && intent.SessionId == session).ToArray();
            foreach (var intent in own) await RemoveAsync(intent, recovered: true).ConfigureAwait(false);
            if (own.Length == 0 && reservation is null) return null;
            _sessions.Remove(session); return new(organization, session, true, false);
        }
        finally { _gate.Release(); }
    }

    private ProcessStartInfo Launch(string file, PublicReaderOwnership intent)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment.Clear();
        foreach (var pair in IsolatedFixtureExecutor.ChildEnvironment()) start.Environment[pair.Key] = pair.Value;
        if (OperatingSystem.IsWindows()) start.Environment["PATHEXT"] = ".EXE";
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(_scripts, file),
            "-Runtime", intent.Runtime, "-OwnerToken", intent.Owner.ToString("N"), "-Image", intent.Image }) start.ArgumentList.Add(arg);
        return start;
    }

    private async Task RemoveAsync(PublicReaderOwnership intent, bool recovered)
    {
        PublicReaderOwnershipJournal.Validate(intent);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = Launch("Stop-IsolatedPublicReader.ps1", intent) };
        if (!process.Start()) throw new IOException("PUBLIC_READER_REMOVAL_UNCONFIRMED");
        using var kill = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { } });
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, deadline.Token);
        var errors = ReadBoundedAsync(process.StandardError.BaseStream, deadline.Token);
        process.StandardInput.Close();
        try
        {
            await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0 || (await errors.ConfigureAwait(false)).Length != 0) throw new IOException();
            var proof = JsonSerializer.Deserialize<PublicReaderResourceRemoval>(await output.ConfigureAwait(false), ObservationStore.Json);
            if (proof is null || proof.Schema != "hsr-public-reader-resource-removal-v1" || proof.Owner != intent.Owner
                || proof.Runtime != intent.Runtime || proof.Image != intent.Image || !proof.ContainersAbsent || !proof.NetworksAbsent)
                throw new IOException();
            await _journal.RecordRemovalAsync(intent, recovered, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var settle = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Task.WhenAll(output, errors, process.WaitForExitAsync(settle.Token)).WaitAsync(settle.Token).ConfigureAwait(false);
            }
            catch (Exception) { /* No removal acknowledgement is written when transport settlement is unknown. */ }
            throw new BrowserOperationException("PUBLIC_READER_REMOVAL_UNCONFIRMED");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var bytes = new MemoryStream(); var buffer = new byte[1024];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) return bytes.ToArray();
            if (bytes.Length + count > 16384) throw new IOException("PUBLIC_READER_REMOVAL_UNCONFIRMED");
            bytes.Write(buffer, 0, count);
        }
    }

    private async Task SweepAsync()
    {
        foreach (var pair in _sessions.ToArray())
        {
            var reader = pair.Value.Reader;
            if (reader is null || !reader.Expired && !reader.ResourcesAbsenceConfirmed) continue;
            try { await reader.DisposeAsync().ConfigureAwait(false); } catch (BrowserOperationException) { }
            if (reader.ResourcesAbsenceConfirmed) _sessions.Remove(pair.Key);
        }
    }

    private async Task MaintainAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try { if (_closing) return; await SweepAsync().ConfigureAwait(false); }
                catch (Exception) { /* Failed retirement remains in the journal and capacity reservation. */ }
                finally { _gate.Release(); }
            }
        }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _closing = true; _timer.Dispose();
            foreach (var value in _sessions.Values)
                if (value.Reader is not null) try { await value.Reader.DisposeAsync().ConfigureAwait(false); } catch (BrowserOperationException) { }
            foreach (var intent in _journal.ReadPending()) await RemoveAsync(intent, recovered: true).ConfigureAwait(false);
            _sessions.Clear(); _disposed = true; _exclusive.Dispose();
        }
        finally { _gate.Release(); }
        await _maintenance.ConfigureAwait(false);
    }

    private sealed class Reservation(Guid organization)
    {
        internal Guid Organization { get; } = organization;
        internal PublicReaderOwnership? Intent { get; set; }
        internal IsolatedPublicReaderSession? Reader { get; set; }
    }
}
