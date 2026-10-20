using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Egress;

public sealed record IsolationReceipt(bool Linux, bool NonRoot, bool NoNewPrivileges, bool ReadOnlyRoot,
    bool CapabilitiesRestricted, bool PrivateTcpBlocked, bool PrivateUdpBlocked, bool PublicTcpBlocked,
    bool PublicIpv6Blocked, bool ProxyDenialsObserved, bool ApprovedTlsVerified)
{
    public bool Passed => Linux && NonRoot && NoNewPrivileges && ReadOnlyRoot && CapabilitiesRestricted
        && PrivateTcpBlocked && PrivateUdpBlocked && PublicTcpBlocked && PublicIpv6Blocked && ProxyDenialsObserved && ApprovedTlsVerified;
}

public static class IsolationProbe
{
    public static async Task<IsolationReceipt> VerifyAsync(IPAddress proxy, PublicTargetPolicy policy, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) return new(false, false, false, false, false, false, false, false, false, false, false);
        var status = File.ReadAllLines("/proc/self/status");
        var uid = status.First(line => line.StartsWith("Uid:", StringComparison.Ordinal)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1];
        var noPrivileges = status.Any(line => line.StartsWith("NoNewPrivs:", StringComparison.Ordinal) && line.TrimEnd().EndsWith('1'));
        var restricted = new[] { "CapEff:", "CapPrm:", "CapInh:", "CapBnd:" }.All(field =>
            CapabilitiesAreRestricted(status.First(line => line.StartsWith(field, StringComparison.Ordinal))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1]));
        var root = File.ReadLines("/proc/self/mountinfo").Select(line => line.Split(' ')).First(fields => fields[4] == "/");
        var readOnly = root[5].Split(',').Contains("ro", StringComparer.Ordinal);
        var privateTcpBlocked = !await CanConnectAsync(proxy, 3130, token).ConfigureAwait(false);
        var privateUdpBlocked = !await SentinelAsync(proxy, udp: true, token).ConfigureAwait(false);
        var publicTcpBlocked = !await CanConnectAsync(IPAddress.Parse("1.1.1.1"), 443, token).ConfigureAwait(false);
        var publicIpv6Blocked = !await CanConnectAsync(IPAddress.Parse("2606:4700:4700::1111"), 443, token).ConfigureAwait(false);
        var denied = true;
        foreach (var authority in new[] { "127.0.0.1:443", "169.254.169.254:443", "[::1]:443", "unapproved.invalid:443", $"{policy.AllowedHosts[0]}:80" })
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(proxy, 3128, token).ConfigureAwait(false);
            var request = Encoding.ASCII.GetBytes($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n\r\n");
            await client.GetStream().WriteAsync(request, token).ConfigureAwait(false);
            var header = await ProxyProtocol.ReadHeaderAsync(client.GetStream(), token).ConfigureAwait(false);
            denied &= header.StartsWith("HTTP/1.1 403 ", StringComparison.Ordinal);
        }
        var tls = await VerifyTlsAsync(proxy, policy.AllowedHosts[0], token).ConfigureAwait(false);
        return new(true, uid != "0", noPrivileges, readOnly, restricted, privateTcpBlocked, privateUdpBlocked,
            publicTcpBlocked, publicIpv6Blocked, denied, tls);
    }

    public static bool CapabilitiesAreRestricted(string value) =>
        ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bits)
        && (bits & ~0x40000UL) == 0; // Zero or only SYS_CHROOT; never require a privilege to be effective.

    public static async Task<bool> SentinelAsync(IPAddress address, bool udp, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(750));
        try
        {
            if (udp)
            {
                using var client = new UdpClient(AddressFamily.InterNetwork);
                await client.SendAsync("hsr-probe"u8.ToArray(), new IPEndPoint(address, 3130), deadline.Token).ConfigureAwait(false);
                var received = await client.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                return Encoding.ASCII.GetString(received.Buffer) == ConnectProxy.Sentinel;
            }
            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync(address, 3130, deadline.Token).ConfigureAwait(false);
            var buffer = new byte[64];
            var read = await tcp.GetStream().ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
            return Encoding.ASCII.GetString(buffer, 0, read) == ConnectProxy.Sentinel;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or IOException) { return false; }
    }

    private static async Task<bool> CanConnectAsync(IPAddress address, int port, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(750));
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            await client.ConnectAsync(address, port, deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException) { return false; }
    }

    private static async Task<bool> VerifyTlsAsync(IPAddress proxy, string host, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(proxy, 3128, deadline.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {host}:443 HTTP/1.1\r\nHost: {host}:443\r\n\r\n"), deadline.Token).ConfigureAwait(false);
            if (!(await ProxyProtocol.ReadHeaderAsync(stream, deadline.Token).ConfigureAwait(false)).StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal)) return false;
            using var tls = new SslStream(stream);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, deadline.Token).ConfigureAwait(false);
            return tls.IsAuthenticated && tls.IsEncrypted;
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException) { return false; }
    }
}
