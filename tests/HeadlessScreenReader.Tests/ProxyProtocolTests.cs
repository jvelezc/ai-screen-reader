using System.Net;
using System.Text;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Egress;

namespace HeadlessScreenReader.Tests;

public sealed class ProxyProtocolTests
{
    [Theory]
    [InlineData("get")]
    [InlineData("http10")]
    [InlineData("double-space")]
    public void ProxyProtocol_RejectsNonConnectAndAmbiguousRequestLines(string caseId)
    {
        var header = caseId switch
        {
            "get" => "GET https://example.com HTTP/1.1\r\n\r\n",
            "http10" => "CONNECT example.com:443 HTTP/1.0\r\n\r\n",
            _ => "CONNECT  example.com:443 HTTP/1.1\r\n\r\n"
        };
        Assert.Throws<TargetDeniedException>(() => ProxyProtocol.Authority(header));
    }

    [Fact]
    public async Task HeaderReader_StopsAtExactBoundaryAndNeverConsumesTunnelBytes()
    {
        var header = "CONNECT example.com:443 HTTP/1.1\r\n\r\n";
        using var source = new MemoryStream(Encoding.ASCII.GetBytes(header + "TLS"));
        Assert.Equal(header, await ProxyProtocol.ReadHeaderAsync(source, CancellationToken.None));
        Assert.Equal((byte)'T', source.ReadByte());
        var exact = new string('x', 8188) + "\r\n\r\n";
        using var at = new MemoryStream(Encoding.ASCII.GetBytes(exact));
        Assert.Equal(exact, await ProxyProtocol.ReadHeaderAsync(at, CancellationToken.None));
        using var over = new MemoryStream(Encoding.ASCII.GetBytes("x" + exact));
        await Assert.ThrowsAsync<TargetDeniedException>(() => ProxyProtocol.ReadHeaderAsync(over, CancellationToken.None));
        using var incomplete = new MemoryStream("CONNECT"u8.ToArray());
        await Assert.ThrowsAsync<IOException>(() => ProxyProtocol.ReadHeaderAsync(incomplete, CancellationToken.None));
    }

    [Fact]
    public async Task DnsResolution_RejectsMixedPrivateAnswersAndPinsOnePublicAddressWithoutResolvingAgain()
    {
        var calls = 0;
        Task<IPAddress[]> Resolve(string host, CancellationToken token)
        {
            Assert.Equal("example.com", host);
            calls++;
            return Task.FromResult(new[] { IPAddress.Parse(calls == 1 ? "1.1.1.1" : "127.0.0.1") });
        }
        var policy = new PublicTargetPolicy(["example.com"]);
        var pinned = await ProxyProtocol.ResolveAsync(policy, "example.com:443", Resolve, CancellationToken.None);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), pinned);
        Assert.Equal(1, calls);
        await Assert.ThrowsAsync<TargetDeniedException>(() => ProxyProtocol.ResolveAsync(policy, "example.com:443", Resolve, CancellationToken.None));
        await Assert.ThrowsAsync<TargetDeniedException>(() => ProxyProtocol.ResolveAsync(policy, "example.com:443",
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Parse("10.0.0.1") }), CancellationToken.None));
        await Assert.ThrowsAsync<TargetDeniedException>(() => ProxyProtocol.ResolveAsync(policy, "example.com:443",
            (_, _) => Task.FromResult(Array.Empty<IPAddress>()), CancellationToken.None));
    }

    [Theory]
    [InlineData(10485759)]
    [InlineData(10485760)]
    [InlineData(10485761)]
    public async Task TunnelCopy_EnforcesExactByteLimit(int length)
    {
        using var source = new MemoryStream(new byte[length]);
        using var target = new MemoryStream();
        if (length > 10_485_760)
            await Assert.ThrowsAsync<IOException>(() => ProxyProtocol.CopyBoundedAsync(source, target, CancellationToken.None));
        else await ProxyProtocol.CopyBoundedAsync(source, target, CancellationToken.None);
        Assert.Equal(Math.Min(length, 10_485_760), target.Length);
    }
}
