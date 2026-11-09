using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Egress;

public sealed class TargetDeniedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class ProxyProtocol
{
    public static void ValidateTraceAuthority(ProxyLease? lease, string token)
    {
        if (lease is null || !lease.Matches(token)) throw new TargetDeniedException("LEASE_DENIED");
        if (lease.Expired) throw new TargetDeniedException("LEASE_EXPIRED");
    }

    /// <summary>Session-authenticated diagnostics. Reading cannot renew a lease or expand the egress policy.</summary>
    public static async Task<PublicTaskNetworkTrace> ReadTraceAsync(IPAddress proxy, string leaseToken, CancellationToken token)
    {
        if (leaseToken.Length != 32 || !leaseToken.All(char.IsAsciiHexDigitLower)) throw new ArgumentException("Invalid trace authority.", nameof(leaseToken));
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(proxy, 3128, token).ConfigureAwait(false);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HSR-TRACE {leaseToken} HTTP/1.1\r\n\r\n"), token).ConfigureAwait(false);
        var header = await ReadHeaderAsync(stream, token).ConfigureAwait(false);
        var fields = header.Split("\r\n");
        if (fields[0] != "HTTP/1.1 200 OK" || fields.Length != 4 || !fields[1].StartsWith("Content-Length: ", StringComparison.Ordinal)
            || !int.TryParse(fields[1][16..], out var length) || length is < 2 or > 256 * 1024)
            throw new IOException("EGRESS_TRACE_UNCONFIRMED");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<PublicTaskNetworkTrace>(bytes) ?? throw new IOException("EGRESS_TRACE_UNCONFIRMED");
        value.Validate(); return value;
    }

    public static async Task<string> ReadHeaderAsync(Stream stream, CancellationToken token)
    {
        var bytes = new List<byte>(512);
        var buffer = new byte[1];
        while (bytes.Count < 8192)
        {
            if (await stream.ReadAsync(buffer, token).ConfigureAwait(false) == 0) throw new IOException("HEADER_INCOMPLETE");
            bytes.Add(buffer[0]);
            if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10)
                return Encoding.ASCII.GetString(bytes.ToArray());
        }
        throw new TargetDeniedException("HEADER_LIMIT");
    }

    public static string Authority(string header)
    {
        var parts = header.Split("\r\n", 2)[0].Split(' ');
        if (parts.Length != 3 || parts[0] != "CONNECT" || parts[2] != "HTTP/1.1")
            throw new TargetDeniedException("CONNECT_REQUIRED");
        return parts[1];
    }

    public static async Task<IPAddress> ResolveAsync(PublicTargetPolicy policy, string authority,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve, CancellationToken token)
    {
        var decision = policy.AssessConnectAuthority(authority);
        if (!decision.Allowed) throw new TargetDeniedException(decision.DenialCode!);
        var addresses = await resolve(decision.Host!, token).ConfigureAwait(false);
        // Reject mixed public/private answers. The selected IP is passed directly to ConnectAsync.
        if (addresses.Length == 0 || addresses.Any(address => !PublicTargetPolicy.IsPublicAddress(address)))
            throw new TargetDeniedException("DNS_ADDRESS_DENIED");
        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new TargetDeniedException("DNS_IPV4_REQUIRED");
    }

    public static Task CopyBoundedAsync(Stream source, Stream target, CancellationToken token) =>
        CopyBoundedAsync(source, target, 10_485_760, token);

    public static async Task CopyBoundedAsync(Stream source, Stream target, long maximumBytes, CancellationToken token)
    {
        if (maximumBytes is < 1 or > 67_108_864) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var buffer = new byte[16_384];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > maximumBytes) throw new IOException("TUNNEL_BYTE_LIMIT");
            await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }
}
