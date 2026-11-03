using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

internal sealed record HostedPublicWebsiteAdmission(Guid OrganizationId, Guid ScanId, Guid JobId, Guid RequestId,
    string TargetUrl, string AllowedHost, string ReaderProfile);
internal sealed record HostedPublicWebsiteSummary(Guid OrganizationId, Guid Id, Guid RequestId, Guid CreatedBy,
    string TargetUrl, string AllowedHost, string ReaderProfile, string State, DateTimeOffset CreatedAt);
internal sealed record HostedPublicWebsiteList(ImmutableArray<HostedPublicWebsiteSummary> Items, bool HasMore);
internal sealed record HostedPublicWebsiteStatus(Guid OrganizationId, Guid Id, Guid RequestId, Guid CreatedBy,
    string TargetUrl, string AllowedHost, string ReaderProfile, string State, DateTimeOffset CreatedAt, Guid JobId,
    long Generation, Guid? ProducerId, string? OriginalSha256, string? ErrorCode, ScanWorkerPermissionSnapshot? WorkerPermission);
internal sealed record HostedPublicWebsiteNodes(HostedPublicWebsiteStatus Scan, string OriginalSha256, DateTimeOffset ReceivedAt,
    int CaptureIndex, int Captures, string DocumentId, string DocumentUrl, long CaptureEpoch, int TotalNodes,
    int Offset, ImmutableArray<HostedScanNode> Nodes, int? NextOffset, string Scope, int DispatchedInputs,
    string GoalVerification, ImmutableArray<string> Limitations);
internal sealed record HostedPublicWebsiteOriginal(HostedPublicWebsiteStatus Scan, string OriginalSha256, int TotalBytes,
    int Offset, byte[] Data, string PageSha256, int? NextOffset);
