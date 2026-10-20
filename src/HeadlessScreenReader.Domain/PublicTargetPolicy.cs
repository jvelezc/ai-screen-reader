using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;

namespace HeadlessScreenReader.Domain;

public sealed record TargetDecision(string? Host, string? DenialCode)
{
    public bool Allowed => DenialCode is null;
}

/// <summary>Pure target validation. Network enforcement belongs outside the browser process.</summary>
public sealed class PublicTargetPolicy
{
    private readonly ImmutableHashSet<string> _hosts;
    private static readonly IPNetwork[] Reserved = new[]
    {
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
        "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15",
        "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
        "2001::/23", "2001:db8::/32", "2002::/16"
    }.Select(IPNetwork.Parse).ToArray();

    public PublicTargetPolicy(IEnumerable<string> allowedHosts)
    {
        var hosts = allowedHosts.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        if (hosts.IsEmpty || hosts.Count > 32 || hosts.Any(host => !ValidHost(host)))
            throw new ArgumentException("Specify 1..32 exact DNS hosts, with no schemes, wildcards, paths or IP literals.", nameof(allowedHosts));
        _hosts = hosts;
    }

    public ImmutableArray<string> AllowedHosts => _hosts.Select(host => host.ToLowerInvariant()).Order(StringComparer.Ordinal).ToImmutableArray();

    public TargetDecision AssessUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 4096 || url.Contains('\\') || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.IsWellFormedOriginalString() || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.HostNameType != UriHostNameType.Dns || !ValidHost(uri.IdnHost))
            return new(null, "TARGET_URL_UNSUPPORTED");
        var host = uri.IdnHost.ToLowerInvariant();
        return _hosts.Contains(host) ? new(host, null) : new(null, "TARGET_HOST_NOT_APPROVED");
    }

    public TargetDecision AssessConnectAuthority(string authority)
    {
        if (string.IsNullOrWhiteSpace(authority) || !authority.EndsWith(":443", StringComparison.Ordinal) || authority.Count(c => c == ':') != 1
            || !ValidHost(authority[..^4])) return new(null, "CONNECT_AUTHORITY_UNSUPPORTED");
        return AssessUrl($"https://{authority}/");
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !Reserved.Any(network => network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address));
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || !IPNetwork.Parse("2000::/3").Contains(address)) return false;
        return !Reserved.Any(network => network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address));
    }

    private static bool ValidHost(string host) => !string.IsNullOrEmpty(host) && host.Length is > 2 and <= 253 && host.Contains('.')
        && !IPAddress.TryParse(host, out _) && host.Split('.').All(label => label.Length is >= 1 and <= 63
            && label[0] != '-' && label[^1] != '-' && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
}
