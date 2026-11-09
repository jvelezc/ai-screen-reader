using System.Net;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Egress;

namespace HeadlessScreenReader.Tests;

public sealed class PublicDependencyPolicyTests
{
    [Fact]
    public void PublicDependenciesDoNotBroadenTheOriginalNavigationOrLegacyPolicy()
    {
        var legacy = new PublicTargetPolicy(["example.com"]);
        var expanded = PublicTargetPolicy.ForPublicDependencies(["example.com"]);
        Assert.False(legacy.AssessDependencyUrl("https://cdn.example.net/library.js").Allowed);
        Assert.False(legacy.AssessConnectAuthority("cdn.example.net:443").Allowed);
        Assert.True(expanded.AssessDependencyUrl("https://cdn.example.net/library.js").Allowed);
        Assert.True(expanded.AssessConnectAuthority("cdn.example.net:443").Allowed);
        Assert.False(expanded.AssessUrl("https://cdn.example.net/library.js").Allowed);
        Assert.True(expanded.AssessUrl("https://example.com/pricing").Allowed);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void NormalApiMethodsAreAllowedOnlyAsPublicDependencies(string method)
    {
        var policy = PublicTargetPolicy.ForPublicDependencies(["example.com"]);
        Assert.True(PublicTaskRequestPolicy.Assess(policy, "https://api.example.net/data?cursor=opaque", method, false).Allowed);
        Assert.Equal("DOCUMENT_URL_DENIED", PublicTaskRequestPolicy.Assess(policy, "https://api.example.net/pricing", "GET", true).DenialCode);
        Assert.Equal(method is "GET" or "HEAD", PublicTaskRequestPolicy.Assess(policy, "https://example.com/pricing", method, true).Allowed);
    }

    [Theory]
    [InlineData("http://api.example.net")]
    [InlineData("https://api.example.net:444")]
    [InlineData("https://user:secret@api.example.net")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://[::1]")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://metadata.internal")]
    [InlineData("https://unapproved.invalid")]
    public void DependencyExpansionStillRejectsInsecureLiteralAndLocalTargets(string url)
    {
        var policy = PublicTargetPolicy.ForPublicDependencies(["example.com"]);
        Assert.False(policy.AssessDependencyUrl(url).Allowed);
        Assert.False(PublicTaskRequestPolicy.Assess(policy, url, "POST", false).Allowed);
    }

    [Fact]
    public async Task PublicDependencyDnsRejectsRebindingAndPinsTheSingleValidatedPublicResolution()
    {
        var policy = PublicTargetPolicy.ForPublicDependencies(["example.com"]);
        var calls = 0;
        var result = await ProxyProtocol.ResolveAsync(policy, "api.example.net:443", (host, _) =>
        {
            calls++; Assert.Equal("api.example.net", host);
            return Task.FromResult(new[] { IPAddress.Parse(calls == 1 ? "1.1.1.1" : "127.0.0.1") });
        }, default);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), result); Assert.Equal(1, calls);
        var denied = await Assert.ThrowsAsync<TargetDeniedException>(() => ProxyProtocol.ResolveAsync(policy, "api.example.net:443",
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Parse("169.254.169.254") }), default));
        Assert.Equal("DNS_ADDRESS_DENIED", denied.Code);
    }

    [Fact]
    public void SecureWebSocketsUseTheSamePublicDependencyPolicy()
    {
        var policy = PublicTargetPolicy.ForPublicDependencies(["example.com"]);
        Assert.True(PublicTaskRequestPolicy.AssessWebSocket(policy, "wss://api.example.net/realtime").Allowed);
        Assert.False(PublicTaskRequestPolicy.AssessWebSocket(policy, "ws://api.example.net/realtime").Allowed);
        Assert.False(PublicTaskRequestPolicy.AssessWebSocket(policy, "wss://127.0.0.1/realtime").Allowed);
        Assert.False(PublicTaskRequestPolicy.AssessWebSocket(new(["example.com"]), "wss://example.com/realtime").Allowed);
    }
}
