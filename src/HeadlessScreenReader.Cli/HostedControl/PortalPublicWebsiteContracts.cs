using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Admit one canonical credential-free HTTPS source; reuse the request UUID after a lost response.</summary>
internal sealed record PortalCreatePublicWebsiteRequest(Guid RequestId, string Url)
{
    public override string ToString() => $"PublicWebsiteAdmission({RequestId:N}, target omitted)";
}

/// <summary>Durable admission identifies work; it does not acknowledge execution or evidence completeness.</summary>
internal sealed record PortalPublicWebsiteCreatedResponse(Guid OrganizationId, Guid ScanId, Guid JobId, Guid RequestId,
    string TargetUrl, string AllowedHost, string ReaderProfile);

/// <summary>Recent caller-visible source metadata, without originals or private execution capabilities.</summary>
internal sealed record PortalPublicWebsiteSummaryResponse(Guid OrganizationId, Guid Id, Guid RequestId, Guid CreatedBy,
    string TargetUrl, string AllowedHost, string ReaderProfile, string State, DateTimeOffset CreatedAt)
{
    internal PublicReaderSource Source => PortalPublicWebsiteContract.Source(TargetUrl, AllowedHost, ReaderProfile);
    internal void Validate(Guid organization)
    {
        if (OrganizationId != organization || Id == Guid.Empty || RequestId == Guid.Empty || CreatedBy == Guid.Empty
            || CreatedAt == default || State is not ("queued" or "leased" or "observed" or "blocked" or "cancelled"))
            throw PortalPublicWebsiteContract.Invalid();
        _ = Source;
    }
}

/// <summary>At most twenty recent sources; HasMore explicitly reports an omitted older history.</summary>
internal sealed record PortalPublicWebsiteListResponse(PortalPublicWebsiteSummaryResponse[] Items, bool HasMore);

/// <summary>One atomic job/lease metadata snapshot; clock-bound Active never grants execution authority.</summary>
internal sealed record PortalPublicWebsiteStatusResponse(Guid OrganizationId, Guid Id, Guid RequestId, Guid CreatedBy,
    string TargetUrl, string AllowedHost, string ReaderProfile, string State, DateTimeOffset CreatedAt, Guid JobId,
    long Generation, Guid? ProducerId, string? OriginalSha256, string? ErrorCode, ScanWorkerPermissionSnapshot? WorkerPermission)
{
    internal PublicWebsiteJob Job => new(OrganizationId, Id, JobId, Generation,
        PortalPublicWebsiteContract.Source(TargetUrl, AllowedHost, ReaderProfile));

    internal void Validate(Guid organization, Guid scan)
    {
        if (OrganizationId != organization || Id != scan || Id == Guid.Empty || RequestId == Guid.Empty || CreatedBy == Guid.Empty
            || JobId == Guid.Empty || CreatedAt == default || Generation is < 0 or > 3
            || State is not ("queued" or "leased" or "completed" or "blocked" or "cancelled")
            || State == "queued" && Generation != 0 || State is "leased" or "completed" && Generation == 0
            || ErrorCode is not null && (ErrorCode.Length is < 1 or > 128 || !ErrorCode.StartsWith("PUBLIC_WEBSITE_", StringComparison.Ordinal)
                || ErrorCode.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')))
            throw PortalPublicWebsiteContract.Invalid();
        _ = PortalPublicWebsiteContract.Source(TargetUrl, AllowedHost, ReaderProfile);
        if (State == "completed")
        {
            if (ProducerId is null || ProducerId == Guid.Empty || !PortalPublicWebsiteContract.Digest(OriginalSha256) || ErrorCode is not null)
                throw PortalPublicWebsiteContract.Invalid();
        }
        else if (ProducerId is not null || OriginalSha256 is not null) throw PortalPublicWebsiteContract.Invalid();
        if (State == "leased")
        {
            if (WorkerPermission is null) throw PortalPublicWebsiteContract.Invalid();
            try { WorkerPermission.Validate(organization, scan); }
            catch (BrowserOperationException) { throw PortalPublicWebsiteContract.Invalid(); }
            if (WorkerPermission.JobId != JobId || WorkerPermission.Generation != Generation) throw PortalPublicWebsiteContract.Invalid();
        }
        else if (WorkerPermission is not null) throw PortalPublicWebsiteContract.Invalid();
    }
}

/// <summary>Verified passive page observations, separately from an attempted task or completed evidence package.</summary>
internal sealed record PortalPublicWebsiteNodePageResponse(PortalPublicWebsiteStatusResponse Scan, string OriginalSha256,
    DateTimeOffset ReceivedAt, int CaptureIndex, int Captures, string DocumentId, string DocumentUrl, long CaptureEpoch,
    int TotalNodes, int Offset, PortalNodeResponse[] Nodes, int? NextOffset, string Scope, int DispatchedInputs, string GoalVerification,
    string[] Limitations);

/// <summary>Bounded original JSON bytes; reauthorize at exact EOF before presenting a complete saved copy.</summary>
internal sealed record PortalPublicWebsiteOriginalPageResponse(PortalPublicWebsiteStatusResponse Scan, string OriginalSha256,
    int TotalBytes, int Offset, byte[] Data, string PageSha256, int? NextOffset);

internal sealed record PortalPublicWebsiteStoredResult(Guid OrganizationId, Guid ScanId, Guid JobId, long Generation,
    Guid ProducerId, string Sha256, string Text);

internal interface IPortalPublicWebsiteProjectClient
{
    Task<PortalPublicWebsiteCreatedResponse> CreatePublicWebsiteAsync(PortalIdentity actor, Guid organization, Guid request,
        PublicReaderSource source, CancellationToken token);
    Task<PortalPublicWebsiteSummaryResponse[]> PublicWebsitesAsync(PortalIdentity actor, Guid organization, CancellationToken token);
    Task<PortalPublicWebsiteSummaryResponse?> PublicWebsiteRequestAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token);
    Task<PortalPublicWebsiteStatusResponse?> PublicWebsiteStatusAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token);
    Task<PortalPublicWebsiteStoredResult?> PublicWebsiteOriginalAsync(PortalIdentity actor, PortalPublicWebsiteStatusResponse expected, CancellationToken token);
}

internal interface IPortalPublicWebsites
{
    Task<PortalWebsiteScreeningResponse> ScreeningAsync(PortalIdentity actor, Guid organization, Guid scan, string digest, CancellationToken token);
    Task<PortalPublicWebsiteCreatedResponse> AdmitAsync(PortalIdentity actor, Guid organization, PortalCreatePublicWebsiteRequest request, CancellationToken token);
    Task<PortalPublicWebsiteListResponse> ListAsync(PortalIdentity actor, Guid organization, CancellationToken token);
    Task<PortalPublicWebsiteStatusResponse> StatusAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token);
    Task<PortalPublicWebsiteNodePageResponse> NodesAsync(PortalIdentity actor, Guid organization, Guid scan,
        int capture, int offset, int limit, string? digest, CancellationToken token);
    Task<PortalPublicWebsiteOriginalPageResponse> OriginalAsync(PortalIdentity actor, Guid organization, Guid scan,
        int offset, string digest, CancellationToken token);
}

internal static class PortalPublicWebsiteContract
{
    internal const int ListSize = 20, MaximumReplyBytes = 131072, OriginalPageBytes = 65536;
    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    internal static PublicReaderSource Source(string url, string host, string profile)
    {
        try
        {
            var value = new PublicReaderSource(profile, url, host); value.Validate();
            if (PublicReaderSource.FromUrl(url) != value) throw Invalid();
            return value;
        }
        catch (Exception error) when (error is ArgumentException or BrowserOperationException or UriFormatException)
        { throw Invalid(); }
    }
    internal static PortalFault Invalid() => new(503, "CONTROL_PUBLIC_WEBSITE_RESPONSE_UNCONFIRMED");
}
