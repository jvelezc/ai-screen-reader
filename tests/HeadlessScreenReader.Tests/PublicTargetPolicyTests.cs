using System.Net;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTargetPolicyTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com:444")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://2130706433")]
    [InlineData("https://0x7f000001")]
    [InlineData("https://[::1]")]
    [InlineData("https://example.com.")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://sub.example.com")]
    [InlineData("https://example.com.evil.test")]
    public void TargetPolicy_RejectsUnsupportedOrUnapprovedUrls(string url) =>
        Assert.False(new PublicTargetPolicy(["example.com"]).AssessUrl(url).Allowed);

    [Fact]
    public void TargetPolicy_AllowsExactHttpsHostWithCaseAndDefaultPort()
    {
        var policy = new PublicTargetPolicy(["EXAMPLE.com"]);
        Assert.Equal(new("example.com", null), policy.AssessUrl("https://example.com:443/path?q=value#section"));
        Assert.Equal(new("example.com", null), policy.AssessConnectAuthority("EXAMPLE.COM:443"));
        Assert.Equal(new[] { "example.com" }, policy.AllowedHosts.ToArray());
        Assert.False(policy.AssessConnectAuthority("example.com:80").Allowed);
        Assert.False(policy.AssessConnectAuthority("example.com:443/path").Allowed);
        Assert.False(policy.AssessConnectAuthority("example.com:443@evil.test").Allowed);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void AddressPolicy_RejectsPrivateSpecialAndMappedAddresses(string address) =>
        Assert.False(PublicTargetPolicy.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("2606:4700:4700::1111")]
    public void AddressPolicy_AllowsObservedPublicAddresses(string address) =>
        Assert.True(PublicTargetPolicy.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("*.example.com")]
    [InlineData("https://example.com")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("example.com/path")]
    [InlineData("example..com")]
    public void PolicyConfiguration_RejectsAmbiguousHosts(string host) =>
        Assert.Throws<ArgumentException>(() => new PublicTargetPolicy([host]));
}
