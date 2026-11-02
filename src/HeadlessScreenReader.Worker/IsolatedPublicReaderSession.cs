using System.Diagnostics;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>A source-bound read-only connection; native Closed and all-resource absence are separate facts.</summary>
public sealed class IsolatedPublicReaderSession : IAsyncDisposable
{
    private readonly Process _client;
    private readonly Func<Task> _remove;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime;
    private readonly TimeProvider _time;
    private readonly long _openedAt;
    private readonly string _runtime;
    private Task<byte[]>? _errors;
    private int _sequence;
    private bool _failed, _started, _clientStopped;
    public Guid SessionId { get; }
    public PublicReaderSource Source { get; }
    public AccessibilitySnapshot InitialSnapshot { get; private set; } = null!;
    public bool ResourcesAbsenceConfirmed { get; private set; }
    public bool NativeShutdownConfirmed { get; private set; }
    public int RuntimeStdinAlreadyClosedWarnings { get; private set; }
    internal bool Expired => _time.GetElapsedTime(_openedAt) >= ReaderWorkerRequest.Lifetime;

    internal IsolatedPublicReaderSession(ProcessStartInfo start, PublicReaderOwnership intent, Func<Task> remove, TimeProvider? time = null)
    {
        PublicReaderOwnershipJournal.Validate(intent);
        SessionId = intent.SessionId; Source = intent.Source; _runtime = intent.Runtime;
        _time = time ?? TimeProvider.System; _openedAt = _time.GetTimestamp();
        _lifetime = new(ReaderWorkerRequest.Lifetime, _time); _remove = remove;
        _client = new Process { StartInfo = start };
    }

    internal async Task OpenAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_started || _failed || ResourcesAbsenceConfirmed) throw new InvalidOperationException();
            _started = _client.Start();
            if (!_started) throw new IOException();
            _errors = DrainErrorsAsync();
            var response = await ExchangeAsync(ReaderWorkerOperation.Open, token).ConfigureAwait(false);
            InitialSnapshot = response.Snapshot!; token.ThrowIfCancellationRequested();
        }
        catch
        {
            _failed = true; await StopAndRemoveAsync().ConfigureAwait(false);
            throw new BrowserOperationException("PUBLIC_READER_OPEN_UNCONFIRMED");
        }
        finally { _gate.Release(); }
    }

    public async Task<AccessibilitySnapshot> ObserveAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_failed || !_started || ResourcesAbsenceConfirmed) throw new BrowserOperationException("PUBLIC_READER_CLOSED");
            if (_sequence >= ReaderWorkerRequest.MaximumCaptures) throw new BrowserOperationException("PUBLIC_READER_BUDGET_EXHAUSTED");
            try
            {
                var response = await ExchangeAsync(ReaderWorkerOperation.Observe, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); return response.Snapshot!;
            }
            catch
            {
                _failed = true; await StopAndRemoveAsync().ConfigureAwait(false);
                throw new BrowserOperationException("PUBLIC_READER_OBSERVATION_UNCONFIRMED");
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<PublicReaderWorkerResponse> ExchangeAsync(ReaderWorkerOperation operation, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        // Provisioning plus isolation proof and navigation are part of Open, not a long HTTP request.
        deadline.CancelAfter(TimeSpan.FromSeconds(operation == ReaderWorkerOperation.Open ? 60 : 20));
        var request = new PublicReaderWorkerRequest(PublicReaderWorkerRequest.CurrentSchema, SessionId, ++_sequence, operation);
        await PublicReaderWorkerWire.WriteRequestAsync(_client.StandardInput.BaseStream, request, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        return await PublicReaderWorkerWire.ReadResponseAsync(_client.StandardOutput.BaseStream, request, Source,
            InitialSnapshot?.DocumentId, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
    }

    private async Task<byte[]> DrainErrorsAsync()
    {
        using var bytes = new MemoryStream(); var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var count = await _client.StandardError.BaseStream.ReadAsync(buffer, _lifetime.Token).ConfigureAwait(false);
                if (count == 0) return bytes.ToArray();
                if (bytes.Length + count > 16384) throw new IOException();
                bytes.Write(buffer, 0, count);
            }
        }
        catch
        {
            KillClient(); throw new IOException("PUBLIC_READER_DIAGNOSTICS_UNCONFIRMED");
        }
    }

    private void KillClient()
    {
        try { if (_started && !_clientStopped && !_client.HasExited) _client.Kill(entireProcessTree: true); }
        catch (Exception) { /* Independent native ownership remains authoritative. */ }
    }

    private async Task StopAndRemoveAsync()
    {
        if (ResourcesAbsenceConfirmed) return;
        KillClient();
        try { await _remove().ConfigureAwait(false); }
        catch { throw new BrowserOperationException("PUBLIC_READER_CLEANUP_UNCONFIRMED"); }
        finally
        {
            if (!_clientStopped)
            {
                if (_started)
                {
                    await _lifetime.CancelAsync().ConfigureAwait(false);
                    using var stopped = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { await _client.WaitForExitAsync(stopped.Token).ConfigureAwait(false); }
                    catch { throw new BrowserOperationException("PUBLIC_READER_CLIENT_EXIT_UNCONFIRMED"); }
                    if (_errors is not null)
                        try { await _errors.WaitAsync(stopped.Token).ConfigureAwait(false); } catch (IOException) { } catch (OperationCanceledException) { }
                }
                _clientStopped = true; _client.Dispose(); _lifetime.Dispose();
            }
        }
        ResourcesAbsenceConfirmed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ResourcesAbsenceConfirmed) return;
            if (_failed || !_started || NativeShutdownConfirmed) { await StopAndRemoveAsync().ConfigureAwait(false); return; }
            try
            {
                await ExchangeAsync(ReaderWorkerOperation.Close, CancellationToken.None).ConfigureAwait(false);
                _client.StandardInput.Close();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var kill = deadline.Token.Register(KillClient);
                await _client.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                var tail = new byte[1];
                var count = await _client.StandardOutput.BaseStream.ReadAsync(tail, deadline.Token).ConfigureAwait(false);
                var diagnostics = await _errors!.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (count != 0 || _client.ExitCode != 0) throw new IOException();
                RuntimeStdinAlreadyClosedWarnings = PublicReaderDiagnostics.ClassifyAfterConfirmedClose(diagnostics, _runtime);
                NativeShutdownConfirmed = true;
            }
            catch
            {
                _failed = true; await StopAndRemoveAsync().ConfigureAwait(false);
                throw new BrowserOperationException("PUBLIC_READER_CLOSE_UNCONFIRMED");
            }
            await StopAndRemoveAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
