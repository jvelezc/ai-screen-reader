using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Egress;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ProxyTraceTests
{
    [Theory]
    [InlineData(4095, true)]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public async Task DeclaredDependencyTransferBudgetEnforcesItsExactBoundary(int bytes, bool allowed)
    {
        using var source = new MemoryStream(new byte[bytes]); using var destination = new MemoryStream();
        if (allowed) await ProxyProtocol.CopyBoundedAsync(source, destination, 4096, default);
        else Assert.Equal("TUNNEL_BYTE_LIMIT", (await Assert.ThrowsAsync<IOException>(() => ProxyProtocol.CopyBoundedAsync(source, destination, 4096, default))).Message);
        Assert.Equal(allowed ? bytes : 0, destination.Length);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ProxyProtocol.CopyBoundedAsync(source, destination, 0, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ProxyProtocol.CopyBoundedAsync(source, destination, 67_108_865, default));
    }

    [Fact]
    public void TraceAuthorityDeniesAbsentWrongAndExpiredLeaseWithoutRenewingIt()
    {
        var time = new FakeTimeProvider(); var token = new string('a', 32); var lease = new ProxyLease(token, time);
        Assert.Equal("LEASE_DENIED", Assert.Throws<TargetDeniedException>(() => ProxyProtocol.ValidateTraceAuthority(null, token)).Code);
        Assert.Equal("LEASE_DENIED", Assert.Throws<TargetDeniedException>(() => ProxyProtocol.ValidateTraceAuthority(lease, new string('b', 32))).Code);
        Assert.True(lease.Touch(token)); time.Advance(TimeSpan.FromSeconds(14));
        ProxyProtocol.ValidateTraceAuthority(lease, token);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("LEASE_EXPIRED", Assert.Throws<TargetDeniedException>(() => ProxyProtocol.ValidateTraceAuthority(lease, token)).Code);
        Assert.False(lease.Touch(token));
    }

    [Fact]
    public async Task ActualProxyTraceIsSessionAuthenticatedAndPreservesTheExactDenialCause()
    {
        var token = Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var proxy = new ConnectProxy(new PublicTargetPolicy(["example.com"]), token);
        var running = proxy.RunAsync(stop.Token);
        var incompleteHeaders = new List<TcpClient>();
        try
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                await client.ConnectAsync(IPAddress.Loopback, 3128, stop.Token);
                await client.GetStream().WriteAsync("CONNECT denied.example.net:443 HTTP/1.1\r\n\r\n"u8.ToArray(), stop.Token);
                Assert.StartsWith("HTTP/1.1 403 ", await ProxyProtocol.ReadHeaderAsync(client.GetStream(), stop.Token), StringComparison.Ordinal);
            }
            await Assert.ThrowsAsync<IOException>(() => ProxyProtocol.ReadTraceAsync(IPAddress.Loopback, new string('a', 32), stop.Token));
            // These clients used to consume all 16 website slots before their headers were read,
            // denying the same worker its own heartbeat/diagnostic control.
            for (var i = 0; i < 24; i++)
            {
                var pending = new TcpClient(AddressFamily.InterNetwork);
                incompleteHeaders.Add(pending); await pending.ConnectAsync(IPAddress.Loopback, 3128, stop.Token);
            }
            var trace = await ProxyProtocol.ReadTraceAsync(IPAddress.Loopback, token, stop.Token); trace.Validate();
            Assert.Equal("ExactHostLegacy", trace.Policy); Assert.Equal(1, trace.Requests); Assert.Equal(1, trace.PolicyDenials);
            Assert.Contains(trace.Events, e => e.Origin == "https://denied.example.net" && e.Code == "TARGET_HOST_NOT_APPROVED");
            Assert.DoesNotContain(token, JsonSerializer.Serialize(trace), StringComparison.Ordinal);
        }
        finally { foreach (var pending in incompleteHeaders) pending.Dispose(); await stop.CancelAsync(); await running; }
    }
}
