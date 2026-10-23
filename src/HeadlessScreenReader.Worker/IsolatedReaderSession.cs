using System.Diagnostics;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>One fixed-fixture connection. A child acknowledgement never substitutes for native removal.</summary>
public sealed class IsolatedReaderSession : IAsyncDisposable
{
    private readonly Process _client;
    private readonly Func<Task> _remove;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime;
    private readonly TimeProvider _time;
    private readonly long _openedAt;
    private Task<byte[]>? _errors;
    private int _sequence;
    private bool _failed;
    private bool _clientStopped;
    private bool _started;
    public Guid SessionId { get; }
    public AccessibilitySnapshot InitialSnapshot { get; private set; } = null!;
    public bool ContainerAbsenceConfirmed { get; private set; }
    public bool NativeShutdownConfirmed { get; private set; }
    internal bool Expired => _time.GetElapsedTime(_openedAt) >= ReaderWorkerRequest.Lifetime;

    internal IsolatedReaderSession(ProcessStartInfo start, Guid session, Func<Task> remove, TimeProvider? time = null)
    {
        if (session == Guid.Empty) throw new ArgumentException("READER_SESSION_ID_INVALID");
        SessionId = session;
        _time = time ?? TimeProvider.System;
        _openedAt = _time.GetTimestamp();
        _lifetime = new(ReaderWorkerRequest.Lifetime, _time);
        _remove = remove;
        _client = new Process { StartInfo = start };
    }

    internal async Task OpenAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_started || _failed || ContainerAbsenceConfirmed) throw new InvalidOperationException();
            _started = _client.Start();
            if (!_started) throw new IOException();
            _errors = DrainErrorsAsync();
            var response = await ExchangeAsync(ReaderWorkerOperation.Open, token).ConfigureAwait(false);
            InitialSnapshot = response.Snapshot!;
            token.ThrowIfCancellationRequested();
        }
        catch
        {
            _failed = true;
            await StopAndRemoveAsync().ConfigureAwait(false);
            throw new BrowserOperationException("READER_CONTAINER_OPEN_UNCONFIRMED");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Open already captured once; at most 99 additional captures are admitted.</summary>
    public async Task<AccessibilitySnapshot> ObserveAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_failed || ContainerAbsenceConfirmed || !_started)
                throw new BrowserOperationException("READER_CONTAINER_CLOSED");
            if (_sequence >= ReaderWorkerRequest.MaximumCaptures)
                throw new BrowserOperationException("READER_CONTAINER_BUDGET_EXHAUSTED");
            try
            {
                var response = await ExchangeAsync(ReaderWorkerOperation.Observe, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return response.Snapshot!;
            }
            catch
            {
                _failed = true;
                await StopAndRemoveAsync().ConfigureAwait(false);
                throw new BrowserOperationException("READER_CONTAINER_OBSERVATION_UNCONFIRMED");
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<ReaderWorkerResponse> ExchangeAsync(ReaderWorkerOperation operation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var request = new ReaderWorkerRequest(ReaderWorkerRequest.CurrentSchema, SessionId, ++_sequence, operation);
        request.Validate();
        await ReaderWorkerWire.WriteAsync(_client.StandardInput.BaseStream,
            JsonSerializer.SerializeToUtf8Bytes(request, ReaderWorkerWire.Json), ReaderWorkerRequest.MaximumRequestBytes, timeout.Token).ConfigureAwait(false);
        return await ReaderWorkerWire.ReadResponseAsync(_client.StandardOutput.BaseStream, request,
            InitialSnapshot?.DocumentId, timeout.Token).ConfigureAwait(false);
    }

    private async Task<byte[]> DrainErrorsAsync()
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var count = await _client.StandardError.BaseStream.ReadAsync(buffer, _lifetime.Token).ConfigureAwait(false);
                if (count == 0) return bytes.ToArray();
                if (bytes.Length + count > 8192) throw new IOException();
                bytes.Write(buffer, 0, count);
            }
        }
        catch
        {
            KillClient();
            throw new IOException("READER_CONTAINER_DIAGNOSTICS_UNCONFIRMED");
        }
    }

    private void KillClient()
    {
        try { if (_started && !_clientStopped && !_client.HasExited) _client.Kill(); }
        catch (Exception) { /* Native ownership is independently reconciled below. */ }
    }

    private async Task StopAndRemoveAsync()
    {
        if (ContainerAbsenceConfirmed) return;
        KillClient();
        try
        {
            // Independent bounded runtime commands, never canceled by the departed HTTP caller.
            await _remove().ConfigureAwait(false);
        }
        catch { throw new BrowserOperationException("READER_CONTAINER_CLEANUP_UNCONFIRMED"); }
        finally
        {
            if (!_clientStopped)
            {
                if (_started)
                {
                    await _lifetime.CancelAsync().ConfigureAwait(false);
                    await _client.WaitForExitAsync().ConfigureAwait(false);
                    if (_errors is not null)
                        try { await _errors.ConfigureAwait(false); } catch (IOException) { } catch (OperationCanceledException) { }
                }
                _clientStopped = true;
                _client.Dispose();
                _lifetime.Dispose();
            }
        }
        ContainerAbsenceConfirmed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ContainerAbsenceConfirmed) return;
            if (_failed || !_started || NativeShutdownConfirmed)
            {
                await StopAndRemoveAsync().ConfigureAwait(false);
                return;
            }
            try
            {
                await ExchangeAsync(ReaderWorkerOperation.Close, CancellationToken.None).ConfigureAwait(false);
                _client.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var kill = timeout.Token.Register(KillClient);
                var tail = ReaderWorkerWire.ReadAsync(_client.StandardOutput.BaseStream,
                    ReaderWorkerRequest.MaximumResponseBytes, timeout.Token);
                await Task.WhenAll(tail, _client.WaitForExitAsync(timeout.Token), _errors!).ConfigureAwait(false);
                if (await tail.ConfigureAwait(false) is not null || _client.ExitCode != 0 || (await _errors!.ConfigureAwait(false)).Length != 0)
                    throw new IOException();
                NativeShutdownConfirmed = true;
            }
            catch
            {
                _failed = true;
                await StopAndRemoveAsync().ConfigureAwait(false);
                throw new BrowserOperationException("READER_CONTAINER_CLOSE_UNCONFIRMED");
            }
            await StopAndRemoveAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
