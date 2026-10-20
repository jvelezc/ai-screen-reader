namespace HeadlessScreenReader.Domain;

public enum ReaderEventKind
{
    CommandStarted, InputAttempted, InputCompleted, Observation,
    FocusChanged, ActiveDescendantChanged, WidgetStateChanged,
    DialogOpened, DialogClosed, Announcement, GoalVerified,
    WaitTimedOut, Cancelled, BrowserError, CommandCompleted, LiveRegionChanged, FrameChanged
}

/// <summary>Ordered simulated reader evidence. Editable values are excluded; supported ranges are explicit.</summary>
public sealed record ReaderEvent(
    long Sequence,
    long CommandSequence,
    double ElapsedMilliseconds,
    ReaderEventKind Kind,
    ReaderCommand Command,
    string? DocumentId,
    string? NodeId,
    string Detail,
    string? PreviousNodeId = null,
    LiveRegionObservation? LiveRegion = null,
    FrameObservation? Frame = null,
    RangeObservation? Range = null,
    HierarchyLevelObservation? HierarchyLevel = null,
    TableObservation? Table = null,
    ReaderTextPassage? Passage = null);
