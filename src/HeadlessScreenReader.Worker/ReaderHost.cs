using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>A narrow loopback broker in the trusted worker. It never accepts executable, URL, environment or credential arguments.</summary>
public sealed class ReaderHost : IAsyncDisposable
{
    private readonly IsolatedReaderSupervisor _supervisor;
    private readonly string _descriptor;
    private readonly TcpListener _listener;
    private readonly ReaderHostEndpoint _endpoint;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _gate = new(1, 1), _slots = new(16, 16);
    private readonly Dictionary<Guid, Owner> _owners = [];
    private readonly HashSet<Task> _clients = [];
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1));
    private Task _accept = Task.CompletedTask, _maintenance = Task.CompletedTask;
    private bool _disposed;
    private bool _published;

    private ReaderHost(IsolatedReaderSupervisor supervisor, string root, string image)
    {
        _supervisor = supervisor;
        _descriptor = ReaderHostEndpoint.DescriptorPath(root);
        _listener = new(IPAddress.Loopback, 0);
        _listener.Start(16);
        _endpoint = new(ReaderHostEndpoint.CurrentSchema, Guid.NewGuid(), ((IPEndPoint)_listener.LocalEndpoint).Port,
            image, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public static async Task<ReaderHost> StartAsync(string root, string runtime, string image, string? seccomp = null)
    {
        var supervisor = await IsolatedReaderSupervisor.StartAsync(root, runtime, image, seccomp).ConfigureAwait(false);
        ReaderHost? host = null;
        try
        {
            host = new(supervisor, root, image);
            if (File.Exists(host._descriptor))
            {
                // This reserved file must have our protocol shape; never overwrite unrelated data or a symlink.
                _ = ReaderHostEndpoint.Read(root);
            }
            var temporary = host._descriptor + "." + host._endpoint.Instance.ToString("N");
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var output = new FileStream(temporary, options))
                    await output.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(host._endpoint, ReaderWorkerWire.Json)).ConfigureAwait(false);
                File.Move(temporary, host._descriptor, overwrite: true);
                host._published = true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            host._accept = host.AcceptAsync(); host._maintenance = host.MaintainAsync();
            return host;
        }
        catch
        {
            if (host is not null) await host.DisposeAsync().ConfigureAwait(false);
            else await supervisor.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                try { await _slots.WaitAsync(_stop.Token).ConfigureAwait(false); }
                catch { client.Dispose(); throw; }
                var actual = ServeAsync(client);
                lock (_clients) _clients.Add(actual);
                _ = actual.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    lock (_clients) _clients.Remove(completed);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                var stream = client.GetStream();
                var bytes = await ReaderWorkerWire.ReadAsync(stream, ReaderWorkerRequest.MaximumRequestBytes, timeout.Token).ConfigureAwait(false);
                if (bytes is null) return;
                var request = JsonSerializer.Deserialize<ReaderHostRequest>(bytes, ReaderWorkerWire.Json) ?? throw new InvalidDataException();
                request.Validate(_endpoint);
                ReaderHostResponse response;
                try { response = await DispatchAsync(request).ConfigureAwait(false); }
                catch (Exception error)
                {
                    var code = error is BrowserOperationException fault && fault.Code.StartsWith("READER_", StringComparison.Ordinal)
                        ? fault.Code : "READER_HOST_OPERATION_UNCONFIRMED";
                    response = Reply(request, error: code);
                }
                await ReaderWorkerWire.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(response, ReaderWorkerWire.Json),
                    ReaderWorkerRequest.MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
            }
            catch { /* Invalid, disconnected or timed-out RPCs cannot manufacture a result. Their native reservations remain owned. */ }
            finally { _slots.Release(); }
        }
    }

    private ReaderHostResponse Reply(ReaderHostRequest request, string? error = null, string? document = null,
        AccessibilitySnapshot? snapshot = null, ReaderRetirement? retirement = null) =>
        new(ReaderHostEndpoint.CurrentSchema, _endpoint.Instance, request.RequestId, request.Operation,
            request.Subject, request.Organization, request.Session, error, document, snapshot, retirement, 0, false);

    private async Task<ReaderHostResponse> DispatchAsync(ReaderHostRequest request)
    {
        if (_stop.IsCancellationRequested || _maintenance.IsFaulted)
            throw new BrowserOperationException("READER_HOST_UNAVAILABLE");
        if (request.Operation == ReaderHostOperation.Ready) return Reply(request);
        await _gate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            await ReapAsync().ConfigureAwait(false);
            if (request.Operation == ReaderHostOperation.Open)
            {
                if (_owners.ContainsKey(request.Session) || _owners.Values.Any(owner => owner.Subject == request.Subject))
                    throw new BrowserOperationException("READER_HOST_SUBJECT_ALREADY_OPEN");
                if (_owners.Count >= 2) throw new BrowserOperationException("READER_HOST_CAPACITY_EXHAUSTED");
                var owner = new Owner(request.Subject, request.Organization);
                _owners.Add(request.Session, owner);
                owner.Reader = await _supervisor.OpenAsync(request.Organization, request.Session, CancellationToken.None).ConfigureAwait(false);
                if (owner.Expired)
                {
                    await _supervisor.RetireAsync(owner.Organization, request.Session).ConfigureAwait(false);
                    _owners.Remove(request.Session);
                    throw new BrowserOperationException("READER_HOST_SESSION_EXPIRED");
                }
                return Reply(request, document: owner.Reader.InitialSnapshot.DocumentId);
            }
            if (_owners.TryGetValue(request.Session, out var existing)
                && (existing.Subject != request.Subject || existing.Organization != request.Organization))
                throw new BrowserOperationException("READER_HOST_SESSION_NOT_FOUND");
            if (request.Operation == ReaderHostOperation.Retire)
            {
                var result = await _supervisor.RetireAsync(request.Organization, request.Session).ConfigureAwait(false);
                if (existing?.Reader?.NativeShutdownConfirmed == true && result.ContainerAbsenceConfirmed)
                    result = result with { NativeShutdownConfirmed = true };
                _owners.Remove(request.Session);
                return Reply(request, retirement: result);
            }
            if (existing?.Reader is not { } reader) throw new BrowserOperationException("READER_HOST_SESSION_NOT_FOUND");
            var snapshot = await reader.ObserveAsync(CancellationToken.None).ConfigureAwait(false);
            return Reply(request, document: snapshot.DocumentId, snapshot: snapshot);
        }
        finally { _gate.Release(); }
    }

    private async Task ReapAsync()
    {
        foreach (var pair in _owners.ToArray())
        {
            if (pair.Value.Reader?.ContainerAbsenceConfirmed == true) { _owners.Remove(pair.Key); continue; }
            if (!pair.Value.Expired) continue;
            try
            {
                var result = await _supervisor.RetireAsync(pair.Value.Organization, pair.Key).ConfigureAwait(false);
                if (result.ContainerAbsenceConfirmed) _owners.Remove(pair.Key);
            }
            catch { /* Keep both subject and native ownership reserved until actual removal is known. */ }
        }
    }

    private async Task MaintainAsync()
    {
        while (await _timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await ReapAsync().ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _stop.Cancel(); _listener.Stop(); _timer.Dispose();
        var backgroundFailed = false;
        try { await _accept.ConfigureAwait(false); }
        catch { backgroundFailed = true; }
        Task[] clients; lock (_clients) clients = _clients.ToArray();
        try { await Task.WhenAll(clients.Append(_maintenance)).ConfigureAwait(false); }
        catch { backgroundFailed = true; }
        try
        {
            await _supervisor.DisposeAsync().ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            if (_published && File.Exists(_descriptor))
            {
                var current = ReaderHostEndpoint.Read(Path.GetDirectoryName(Path.GetDirectoryName(_descriptor))!);
                if (current.Instance == _endpoint.Instance) File.Delete(_descriptor);
            }
        }
        if (backgroundFailed) throw new BrowserOperationException("READER_HOST_DRAIN_UNCONFIRMED");
    }

    private sealed class Owner(Guid subject, Guid organization)
    {
        private readonly long _opened = TimeProvider.System.GetTimestamp();
        public Guid Subject { get; } = subject;
        public Guid Organization { get; } = organization;
        public IsolatedReaderSession? Reader { get; set; }
        public bool Expired => TimeProvider.System.GetElapsedTime(_opened) >= ReaderWorkerRequest.Lifetime;
    }
}
