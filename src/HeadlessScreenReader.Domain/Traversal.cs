using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public sealed record TraversalInstruction(ReaderCommand Command, string? Key = null);

/// <summary>An authored route and explicit predicates, never a guessed free-text goal.</summary>
public sealed record TraversalPlan(
    string Name, ObservationExpectation Precondition, ObservationExpectation Destination,
    ImmutableArray<TraversalInstruction> Instructions);

public sealed record TraversalLimits
{
    public int MaxCommands { get; init; } = 30;
    public int MaxConsecutiveUnchanged { get; init; } = 3;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(30);
}

public enum TraversalExecution { Completed, Interrupted, Cancelled, SystemError }
public enum TraversalReachability { Reached, NotReached, Unknown }
public enum TraversalBarrier { Observed, NoneObserved, NotAssessed }
public enum TraversalPolicy { Allowed, Denied }
public enum TraversalStopReason
{
    RouteCompleted, PreconditionNotObserved, CommandLimit, DurationElapsed, NoObservedProgress,
    PolicyDenied, ObservedBarrier, ReaderLimitation, BrowserError, Cancelled
}

/// <summary>A retained reader snapshot; URL payloads are omitted from this local evidence schema.</summary>
public sealed record TraversalObservation(
    string Id, AccessibilitySnapshot Snapshot, ReaderMode Mode, string? CursorNodeId,
    string Fingerprint, double ElapsedMilliseconds);

/// <summary>Observed result references never name an unobserved intended destination.</summary>
public sealed record TraversalAttempt(
    string Id, string SourceObservationId, string? ResultObservationId,
    TraversalInstruction Instruction, ReaderStep? Step,
    ImmutableArray<ReaderEvent> Events, bool TimelineTruncated,
    double StartedMilliseconds, double FinishedMilliseconds);

public sealed record TraversalReceipt(
    string Schema, string Profile, TraversalPlan Plan, TraversalLimits Limits,
    TraversalExecution Execution, TraversalReachability Reachability,
    TraversalBarrier Barrier, TraversalPolicy Policy, TraversalStopReason StopReason,
    ImmutableArray<TraversalObservation> Observations, ImmutableArray<TraversalAttempt> Attempts,
    ImmutableArray<ReaderEvent> Timeline, bool TimelineTruncated, int UnexecutedInstructions,
    string? ErrorCode = null)
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAtUtc { get; init; }
    public double DurationMilliseconds { get; init; }
    public bool HasRecording => false;
    public string Assistance => "None; this runner has no human-input channel";
    public int CompletedKeystrokes => Attempts.Sum(a => a.Step?.BrowserKeystrokes ?? 0);
    public int AttemptedKeystrokes => Timeline.Count(e => e.Kind == ReaderEventKind.InputAttempted);
}

public sealed record TraversalGraphNode(string ObservationId, string DocumentId, string Fingerprint)
{
    public string Label { get; init; } = "";
    public ReaderMode Mode { get; init; }
    public string? CursorNodeId { get; init; }
    public string? FocusedNodeId { get; init; }
    public string? ActiveDescendantNodeId { get; init; }
}

/// <summary>Actual browser-key events. A missing completion never proves that input did not occur.</summary>
public sealed record TraversalKeyTrace(string Key, long? AttemptEventSequence, long? CompletionEventSequence);

public sealed record TraversalGraphEdge(
    string AttemptId, string SourceObservationId, string ResultObservationId,
    ReaderOutcome? Outcome, VerificationStatus Verification)
{
    public ReaderCommand Command { get; init; }
    public ImmutableArray<TraversalKeyTrace> KeyboardInputs { get; init; } = [];
    public bool InputEvidenceComplete { get; init; }
    public int CompletedKeystrokes { get; init; }
    public bool InputMayHaveOccurred { get; init; }
}
public sealed record TraversalGraph(
    string Version, ImmutableArray<TraversalGraphNode> Nodes, ImmutableArray<TraversalGraphEdge> Edges)
{
    public ImmutableArray<TraversalAttempt> UnresolvedAttempts { get; init; } = [];
}
