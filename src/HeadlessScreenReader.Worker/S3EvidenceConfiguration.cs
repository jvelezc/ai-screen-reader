using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Signing;

namespace HeadlessScreenReader.Worker;

/// <summary>Server-only S3 authority. Production credentials cannot select another endpoint.</summary>
public sealed class S3EvidenceConfiguration
{
    private readonly string _access;
    private readonly string _secret;
    public Uri Endpoint { get; }
    public string Bucket { get; }
    private S3EvidenceConfiguration(Uri endpoint, string bucket, string access, string secret)
    { Endpoint = endpoint; Bucket = bucket; _access = access; _secret = secret; }

    public static S3EvidenceConfiguration? Optional(string? access, string? secret)
    {
        if (access is null && secret is null) return null;
        if (access is null || secret is null || !Regex.IsMatch(access, "\\A[A-Za-z0-9_-]{16,128}\\z")
            || !Regex.IsMatch(secret, "\\A[A-Za-z0-9_+/=-]{32,256}\\z")
            || new[] { access, secret }.Any(value => value.StartsWith("sb_", StringComparison.Ordinal)
                || value.StartsWith("sbp_", StringComparison.Ordinal)))
            throw new ArgumentException("EVIDENCE_S3_CONFIGURATION_INVALID");
        return new(new("https://gpnijaxrgtwzwtrqsbcu.storage.supabase.co/storage/v1/s3/"),
            "hsr-evidence", access, secret);
    }

    /// <summary>Only fixed disposable credentials can use the loopback or isolated private IPv4 proof endpoint.</summary>
    public static S3EvidenceConfiguration LocalProof(int port, Guid owner, string host = "127.0.0.1")
    {
        if (port is < 1 or > 65535 || owner == Guid.Empty || !IPAddress.TryParse(host, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != host)
            throw new ArgumentException("EVIDENCE_S3_PROOF_CONFIGURATION_INVALID");
        var octets = address.GetAddressBytes();
        if (!(host == "127.0.0.1" || octets[0] == 10 || octets[0] == 172 && octets[1] is >= 16 and <= 31
            || octets[0] == 192 && octets[1] == 168))
            throw new ArgumentException("EVIDENCE_S3_PROOF_CONFIGURATION_INVALID");
        return new(new($"http://{host}:{port}/"), "hsr-proof-" + owner.ToString("N"),
            "hsr-local-proof-access", "hsr-local-proof-secret-synthetic-only");
    }

    internal HttpClient CreateClient(HttpMessageHandler? transport = null)
    {
        transport ??= new SocketsHttpHandler
        { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        var signer = new SigV4SigningHandler(new BasicAWSCredentials(_access, _secret), RegionEndpoint.USEast1, "s3", transport);
        return new HttpClient(signer) { Timeout = TimeSpan.FromSeconds(20) };
    }
    public override string ToString() => "S3EvidenceConfiguration(credentials redacted)";
}
