using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

internal sealed record HostedScanAdmission(Guid OrganizationId, Guid ScanId, Guid JobId);
internal sealed record HostedScan(Guid OrganizationId, Guid Id, string Fixture, string ReaderProfile,
    string State, DateTimeOffset CreatedAt);
internal sealed record HostedScanList(ImmutableArray<HostedScan> Items, bool HasMore);
internal sealed record HostedScanNode(string Id, string? ParentId, string Role, string Name, bool Focused,
    bool Disabled, int? HeadingLevel, string? Checked, bool? Expanded, bool? Selected, bool? Required,
    string? RequiredSource, string? Description);
internal sealed record HostedScanObservation(string DocumentId, string Digest, string ReaderProfile,
    int DispatchedInputs, bool GoalVerified, ImmutableArray<HostedScanNode> Nodes,
    ImmutableArray<string> Limitations, string EvidenceStorage);
internal sealed record HostedScanReport(HostedScan Scan, string EvidenceState, string? ErrorCode,
    HostedScanObservation? Observation)
{
    public WorkerEvidenceArchive? WorkerArchive { get; init; }
}
