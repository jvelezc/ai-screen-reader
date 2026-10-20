using System.Net;
using System.Net.Sockets;
using System.Text;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Egress;

namespace HeadlessScreenReader.Mcp;

public sealed class VerifiedPublicAccess
{
    private readonly string _leaseToken;
    private VerifiedPublicAccess(IPAddress proxy, PublicTargetPolicy policy, IsolationReceipt receipt, string leaseToken)
    { Proxy = proxy; Policy = policy; Receipt = receipt; _leaseToken = leaseToken; }

    public IPAddress Proxy { get; }
    public PublicTargetPolicy Policy { get; }
    public IsolationReceipt Receipt { get; }

    public static async Task<VerifiedPublicAccess> CreateAsync(string proxyIp, string hosts, string leaseToken, CancellationToken token)
    {
        var proxy = IPAddress.Parse(proxyIp);
        var policy = new PublicTargetPolicy(hosts.Split(','));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var receipt = await IsolationProbe.VerifyAsync(proxy, policy, deadline.Token).ConfigureAwait(false);
        if (!receipt.Passed) throw new InvalidOperationException("ISOLATION_NOT_VERIFIED");
        var access = new VerifiedPublicAccess(proxy, policy, receipt, leaseToken);
        await access.PulseAsync(false, token).ConfigureAwait(false);
        return access;
    }

    public async Task PulseAsync(bool close, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(Proxy, 3128, deadline.Token).ConfigureAwait(false);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"{(close ? "HSR-CLOSE" : "HSR-LEASE")} {_leaseToken} HTTP/1.1\r\n\r\n"), deadline.Token).ConfigureAwait(false);
        var response = await ProxyProtocol.ReadHeaderAsync(client.GetStream(), deadline.Token).ConfigureAwait(false);
        if (!response.StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal)) throw new IOException("ISOLATION_LEASE_DENIED");
    }
}
