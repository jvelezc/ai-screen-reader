using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

internal sealed record HostedWorkspace(Guid Id, string Name);
internal sealed record HostedSession(Guid SessionId, Guid OrganizationId, string Fixture, string ReaderProfile,
    string State, DateTimeOffset ExpiresAt, bool ReadOnly);
internal sealed record HostedClosed(Guid SessionId, string State);
internal sealed record HostedSnapshot(Guid SessionId, string ReaderProfile, AccessibilitySnapshot Snapshot,
    int DispatchedInputs, bool GoalVerified);
public sealed record HostedReaderCommand(Guid SessionId, string ReaderProfile, string DocumentId,
    bool RequiresObservation, ReaderStep Step, int DispatchedInputs, bool GoalVerified);
internal sealed record HostedTimeline(Guid SessionId, string ReaderProfile, ImmutableArray<ReaderEvent> Events,
    long NextAfterSequence, bool HasMore, bool TimelineTruncated);
internal sealed record HostedTrace(Guid SessionId, string ReaderProfile, string Schema, string Retention,
    ImmutableArray<ReaderNavigationTraceEntry> Entries, long NextAfterSequence, bool HasMore,
    bool TraceTruncated, string? TruncationReason, int RetainedCommands, long LatestCommandSequence,
    int DispatchedInputs, bool GoalVerified);
internal sealed class HostedReaderFault(string code, object? facts = null, int? statusCode = null) : Exception(code)
{
    public string Code { get; } = code;
    public object? Facts { get; } = facts;
    public int? StatusCode { get; } = statusCode;
}
