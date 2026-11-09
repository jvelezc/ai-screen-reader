using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Egress;

/// <summary>CONNECT-only TLS passthrough. No credentials, HTTP bodies, page content or request logging.</summary>
public sealed class ConnectProxy(PublicTargetPolicy policy, string? leaseToken = null)
{
    private readonly SemaphoreSlim _slots = new(policy.PublicDependenciesEnabled ? 64 : 16, policy.PublicDependenciesEnabled ? 64 : 16);
    private readonly ProxyLease? _lease = leaseToken is null ? null : new(leaseToken, TimeProvider.System);
    private readonly CancellationTokenSource _stop = new();
    private readonly long _began = Stopwatch.GetTimestamp();
    private readonly PublicTaskNetworkLog _diagnostics = new(policy.PublicDependenciesEnabled
        ? PublicTaskNetworkTrace.PublicDependencyPolicy : "ExactHostLegacy", DateTimeOffset.UtcNow);
    public const string Sentinel = "HSR_ISOLATION_SENTINEL";

    public async Task RunAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        try { await Task.WhenAll(AcceptAsync(linked.Token), TcpSentinelAsync(linked.Token), UdpSentinelAsync(linked.Token), WatchLeaseAsync(linked.Token)).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
    }

    private async Task WatchLeaseAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            if (_lease?.Expired == true) { _stop.Cancel(); return; }
        }
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.Any, 3128);
        listener.Start(16);
        while (!token.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            _ = HandleAsync(client, token);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client)
        {
            var ownsSlot = false;
            var connected = false;
            var diagnostic = 0;
            double Elapsed() => Stopwatch.GetElapsedTime(_began).TotalMilliseconds;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var downstream = client.GetStream();
                var header = await ProxyProtocol.ReadHeaderAsync(downstream, deadline.Token).ConfigureAwait(false);
                var control = header.Split("\r\n", 2)[0].Split(' ');
                if (control.Length == 3 && control[2] == "HTTP/1.1" && control[0] is "HSR-LEASE" or "HSR-CLOSE" or "HSR-TRACE")
                {
                    if (control[0] == "HSR-TRACE")
                    {
                        ProxyProtocol.ValidateTraceAuthority(_lease, control[1]);
                        var bytes = JsonSerializer.SerializeToUtf8Bytes(_diagnostics.Snapshot());
                        await downstream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\n\r\n"), deadline.Token).ConfigureAwait(false);
                        await downstream.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
                        return;
                    }
                    if (_lease is null || !_lease.Matches(control[1])) throw new TargetDeniedException("LEASE_DENIED");
                    if (control[0] == "HSR-LEASE" && !_lease.Touch(control[1])) throw new TargetDeniedException("LEASE_EXPIRED");
                    await downstream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), deadline.Token).ConfigureAwait(false);
                    if (control[0] == "HSR-CLOSE") _stop.Cancel();
                    return;
                }
                var authority = ProxyProtocol.Authority(header);
                diagnostic = _diagnostics.Begin($"https://{authority}/", "CONNECT", "connect", Elapsed());
                // Lease/diagnostic control is handled before website slots. A busy page cannot starve its own heartbeat.
                if (!await _slots.WaitAsync(TimeSpan.FromSeconds(3), deadline.Token).ConfigureAwait(false))
                    throw new IOException("PROXY_CAPACITY");
                ownsSlot = true;
                if (policy.PublicDependenciesEnabled) deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var address = await ProxyProtocol.ResolveAsync(policy, authority,
                    (host, token) => Dns.GetHostAddressesAsync(host, token), deadline.Token).ConfigureAwait(false);
                using var upstream = new TcpClient(AddressFamily.InterNetwork);
                // IP address overload prevents a second resolution/rebinding between validation and connection.
                await upstream.ConnectAsync(address, 443, deadline.Token).ConfigureAwait(false);
                await downstream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), deadline.Token).ConfigureAwait(false);
                connected = true;
                _diagnostics.Record(diagnostic, "Response", Elapsed(), 200);
                deadline.CancelAfter(TimeSpan.FromSeconds(policy.PublicDependenciesEnabled ? 180 : 30));
                var remote = upstream.GetStream();
                var budget = policy.PublicDependenciesEnabled ? 67_108_864 : 10_485_760;
                var upload = ProxyProtocol.CopyBoundedAsync(downstream, remote, budget, deadline.Token);
                var download = ProxyProtocol.CopyBoundedAsync(remote, downstream, budget, deadline.Token);
                var settled = await Task.WhenAny(upload, download).ConfigureAwait(false);
                var limited = settled.Exception?.InnerExceptions.Any(e => e is IOException { Message: "TUNNEL_BYTE_LIMIT" }) == true;
                if (limited) _diagnostics.Record(diagnostic, "Failed", Elapsed(), code: "TUNNEL_BYTE_LIMIT");
                deadline.Cancel();
                try { await Task.WhenAll(upload, download).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or SocketException)
                {
                    // Cancellation after the first direction closes is ordinary tunnel teardown.
                }
                if (!limited) _diagnostics.Record(diagnostic, "Finished", Elapsed());
            }
            catch (Exception exception) when (exception is TargetDeniedException or IOException or SocketException or OperationCanceledException)
            {
                if (diagnostic != 0) _diagnostics.Record(diagnostic, exception is TargetDeniedException ? "PolicyDenied" : "Failed", Elapsed(),
                    code: exception switch
                    {
                        TargetDeniedException denied => denied.Code,
                        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData } => "DNS_NOT_FOUND",
                        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "CONNECT_REFUSED",
                        OperationCanceledException => "CONNECT_TIMEOUT",
                        IOException { Message: "PROXY_CAPACITY" } => "PROXY_CAPACITY",
                        SocketException => "CONNECT_FAILED",
                        _ => "CONNECT_IO_FAILED"
                    });
                if (!connected && client.Connected)
                {
                    var status = exception is TargetDeniedException ? "403 Forbidden" : "502 Bad Gateway";
                    try
                    {
                        using var write = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), write.Token).ConfigureAwait(false);
                    }
                    catch (Exception writeError) when (writeError is IOException or SocketException or OperationCanceledException) { }
                }
            }
            finally { if (ownsSlot) _slots.Release(); }
        }
    }

    private static async Task TcpSentinelAsync(CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.Any, 3130);
        listener.Start();
        while (!token.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            try { await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(Sentinel), token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or SocketException) { }
        }
    }

    private static async Task UdpSentinelAsync(CancellationToken token)
    {
        using var listener = new UdpClient(3130);
        while (!token.IsCancellationRequested)
        {
            var request = await listener.ReceiveAsync(token).ConfigureAwait(false);
            if (request.Buffer.AsSpan().SequenceEqual("hsr-probe"u8))
                await listener.SendAsync(Encoding.ASCII.GetBytes(Sentinel), request.RemoteEndPoint, token).ConfigureAwait(false);
        }
    }
}
