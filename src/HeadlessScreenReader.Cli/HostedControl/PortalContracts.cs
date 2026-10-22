using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Operator configuration for this private, fixed-project portal.</summary>
internal sealed record PortalOptions(string PublishableKey, string EvidenceRoot, string ReleaseCommit,
    int Port = 8086, string PublicOrigin = "http://localhost:6086")
{
    public const string ProjectRef = "gpnijaxrgtwzwtrqsbcu";
    public static readonly Uri Project = new("https://" + ProjectRef + ".supabase.co/");

    public void Validate()
    {
        if (Port is < 0 or > 65535 || string.IsNullOrWhiteSpace(ReleaseCommit)
            || !Regex.IsMatch(ReleaseCommit, "\\A[a-f0-9]{40}\\z")
            || string.IsNullOrWhiteSpace(EvidenceRoot) || !Path.IsPathFullyQualified(EvidenceRoot)
            || !Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme != "http" || origin.Host is not ("localhost" or "127.0.0.1")
            || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/"
            || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || PublishableKey is null || !ValidPublicKey(PublishableKey))
            throw new ArgumentException("CONTROL_CONFIGURATION_INVALID");
    }

    internal static bool ValidPublicKey(string key)
    {
        if (key.Length is < 20 or > 4096 || !Regex.IsMatch(key, "\\A[A-Za-z0-9_.-]+\\z")) return false;
        if (key.StartsWith("sb_publishable_", StringComparison.Ordinal)) return true;
        try
        {
            var parts = key.Split('.');
            if (parts.Length != 3) return false;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload), new() { AllowDuplicateProperties = false });
            return json.RootElement.GetProperty("role").GetString() == "anon"
                && json.RootElement.GetProperty("ref").GetString() == ProjectRef;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        { return false; }
    }
    public override string ToString() => "PortalOptions(private loopback portal; public credential omitted)";
}

/// <summary>Verified upstream user; the bearer never enters API output or diagnostics.</summary>
internal sealed class PortalIdentity(Guid userId, string email, string token)
{
    public Guid UserId { get; } = userId;
    public string Email { get; } = email;
    [JsonIgnore] internal string Token { get; } = token;
    public override string ToString() => "PortalIdentity(" + UserId.ToString("N") + ", credential redacted)";
}

/// <summary>Public settings needed to sign in directly with the selected Auth provider.</summary>
internal sealed record PortalConfigurationResponse(string ProjectUrl, string PublishableKey, string Fixture, string ReaderProfile);
/// <summary>Current Auth-server-verified identity, separate from project administration login.</summary>
internal sealed record PortalSessionResponse(Guid UserId, string Email);
/// <summary>A currently visible tenant workspace.</summary>
internal sealed record PortalWorkspaceResponse(Guid Id, string Name);
/// <summary>Idempotent workspace bootstrap receipt.</summary>
internal sealed record PortalWorkspaceBootstrap(Guid Id, bool Created);
/// <summary>Client request identity only; fixture and profile remain server controlled.</summary>
internal sealed record PortalCreateScanRequest(Guid RequestId);
/// <summary>Durable fixture admission receipt; it does not attest to execution.</summary>
internal sealed record PortalScanCreatedResponse(Guid OrganizationId, Guid ScanId, Guid JobId);
/// <summary>Tenant-visible scan state and its database creation time.</summary>
internal sealed record PortalScanResponse(Guid OrganizationId, Guid Id, string Fixture, string ReaderProfile,
    string State, DateTimeOffset CreatedAt);
/// <summary>Bounded recent history; additional results are reported explicitly.</summary>
internal sealed record PortalScanListResponse(PortalScanResponse[] Items, bool HasMore);
/// <summary>Fenced job result metadata; no lease bearer or token hash is exposed.</summary>
internal sealed record PortalJobResult(Guid OrganizationId, Guid ScanId, Guid JobId, string State, long Generation,
    string? Digest, string? Reference, string? ErrorCode);
/// <summary>Stored source semantics. Null required state remains unobserved.</summary>
internal sealed record PortalNodeResponse(string Id, string? ParentId, string Role, string Name,
    bool Focused, bool Disabled, int? HeadingLevel, string? Checked, bool? Expanded, bool? Selected,
    bool? Required, string? RequiredSource, string? Description);
/// <summary>Verified stored observation, without an invented traversal or goal claim.</summary>
internal sealed record PortalObservationResponse(string DocumentId, string Digest, string ReaderProfile,
    int DispatchedInputs, bool GoalVerified, PortalNodeResponse[] Nodes, string[] Limitations, string EvidenceStorage = "Local");
/// <summary>Report metadata and explicit evidence availability are independent facts.</summary>
internal sealed record PortalReportResponse(PortalScanResponse Scan, string EvidenceState,
    string? ErrorCode, PortalObservationResponse? Observation);

internal sealed class PortalFault(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

internal static class PortalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };
}
