using System.Net;
using System.Net.Sockets;
using System.Text;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Egress;

/// <summary>CONNECT-only TLS passthrough. No credentials, HTTP bodies, page content or request logging.</summary>
public sealed class ConnectProxy(PublicTargetPolicy policy, string? leaseToken = null)
{
    private readonly SemaphoreSlim _slots = new(16, 16);
    private readonly ProxyLease? _lease = leaseToken is null ? null : new(leaseToken, TimeProvider.System);
    private readonly CancellationTokenSource _stop = new();
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
            if (!await _slots.WaitAsync(0).ConfigureAwait(false)) return;
            var connected = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var downstream = client.GetStream();
                var header = await ProxyProtocol.ReadHeaderAsync(downstream, deadline.Token).ConfigureAwait(false);
                var control = header.Split("\r\n", 2)[0].Split(' ');
                if (control.Length == 3 && control[2] == "HTTP/1.1" && control[0] is "HSR-LEASE" or "HSR-CLOSE")
                {
                    if (_lease is null || !_lease.Matches(control[1])) throw new TargetDeniedException("LEASE_DENIED");
                    if (control[0] == "HSR-LEASE" && !_lease.Touch(control[1])) throw new TargetDeniedException("LEASE_EXPIRED");
                    await downstream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), deadline.Token).ConfigureAwait(false);
                    if (control[0] == "HSR-CLOSE") _stop.Cancel();
                    return;
                }
                var address = await ProxyProtocol.ResolveAsync(policy, ProxyProtocol.Authority(header),
                    (host, token) => Dns.GetHostAddressesAsync(host, token), deadline.Token).ConfigureAwait(false);
                using var upstream = new TcpClient(AddressFamily.InterNetwork);
                // IP address overload prevents a second resolution/rebinding between validation and connection.
                await upstream.ConnectAsync(address, 443, deadline.Token).ConfigureAwait(false);
                await downstream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), deadline.Token).ConfigureAwait(false);
                connected = true;
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var remote = upstream.GetStream();
                var upload = ProxyProtocol.CopyBoundedAsync(downstream, remote, deadline.Token);
                var download = ProxyProtocol.CopyBoundedAsync(remote, downstream, deadline.Token);
                await Task.WhenAny(upload, download).ConfigureAwait(false);
                deadline.Cancel();
                try { await Task.WhenAll(upload, download).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or SocketException) { }
            }
            catch (Exception exception) when (exception is TargetDeniedException or IOException or SocketException or OperationCanceledException)
            {
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
            finally { _slots.Release(); }
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
