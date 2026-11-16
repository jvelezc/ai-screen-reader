using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public sealed record SearchPointerPreparation(SearchFocusReceipt? Focus, DateTimeOffset? ReaderStartedAtUtc,
    ReaderStep? Entry, ImmutableArray<ReaderEvent> Timeline, AccessibilitySnapshot? AfterEntry, string? ErrorCode)
{
    public bool TimelineTruncated { get; init; }
    public string ReaderClock => "ElapsedMillisecondsFromReaderConstruction;VideoAlignmentUnverified";
}

public sealed record SearchPointerNode(string Id, AccessibilitySnapshot Snapshot);
public sealed record SearchPointerEdge(string Id, string Source, string? Target, string Mechanism,
    int AttemptedNativeCalls, int CompletedNativeCalls, bool InputEvidenceComplete, bool InputUnconfirmed,
    ImmutableArray<TraversalKeyTrace> KeyboardInputs, string? ErrorCode);
public sealed record SearchPointerGraph(ImmutableArray<SearchPointerNode> Nodes, ImmutableArray<SearchPointerEdge> Edges)
{
    public string Schema => "hsr-synthetic-search-pointer-graph-v1";
    public string Scope => "InputActionsOnly;VirtualModeChangesRemainInReaderTimeline";
}
