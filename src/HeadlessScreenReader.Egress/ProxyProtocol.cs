using System.Net;
using System.Net.Sockets;
using System.Text;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Egress;

public sealed class TargetDeniedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class ProxyProtocol
{
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

    public static async Task CopyBoundedAsync(Stream source, Stream target, CancellationToken token)
    {
        var buffer = new byte[16_384];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > 10_485_760) throw new IOException("TUNNEL_BYTE_LIMIT");
            await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }
}
