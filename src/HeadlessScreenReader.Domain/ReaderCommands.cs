namespace HeadlessScreenReader.Domain;

public enum ReaderMode { Browse, Focus }

public enum ReaderCommand
{
    ReadCurrent,
    MoveNext,
    MovePrevious,
    NextHeading,
    NextLink,
    NextButton,
    NextLandmark,
    Tab,
    ReverseTab,
    Activate,
    EnterFocusMode,
    EnterBrowseMode,
    PressKey,
    Escape,
    PreviousHeading,
    PreviousLink,
    PreviousButton,
    PreviousLandmark,
    DocumentStart,
    DocumentEnd,
    ObserveChanges,
    ReadCurrentText,
    NextCharacter,
    PreviousCharacter,
    NextWord,
    PreviousWord,
    NextLine,
    PreviousLine,
    NextFrame,
    PreviousFrame,
    NextRange,
    PreviousRange,
    NextTree,
    PreviousTree,
    NextTreeItem,
    PreviousTreeItem,
    NextTabItem,
    PreviousTabItem,
    NextTable,
    PreviousTable,
    NextCell,
    PreviousCell,
    NextRow,
    PreviousRow,
    NextColumn,
    PreviousColumn
}

public enum ReaderOutcome
{
    Observed,
    CursorMoved,
    NoMatch,
    FocusRequired,
    Denied,
    BudgetExceeded,
    Unsupported,
    BrowserError,
    Unknown,
    Blocked
}

/// <summary>A local observation, not a claim of goal completion or accessibility conformance.</summary>
public sealed record ReaderStep(
    long Sequence,
    ReaderCommand Command,
    ReaderMode Mode,
    ReaderOutcome Outcome,
    string Announcement,
    string? CursorNodeId,
    string? FocusedNodeId,
    int BrowserKeystrokes,
    string? ErrorCode = null,
    bool InputMayHaveOccurred = false,
    string? ActiveDescendantNodeId = null,
    VerificationStatus Verification = VerificationStatus.NotRequested,
    string? BarrierCode = null)
{
    public ReaderContext? Context { get; init; }
    public RequiredObservation? Required { get; init; }
    public ProtectedObservation? Protection { get; init; }
    public ReaderTextRange? TextRange { get; init; }
    public TextCursorResetReason? TextCursorReset { get; init; }
    public FrameObservation? Frame { get; init; }
    public RangeObservation? Range { get; init; }
    public ReaderTableContext? Table { get; init; }
    public System.Collections.Immutable.ImmutableArray<FrameObservation> FrameCoverage { get; init; } = [];
}

public static class BrowserKeyPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "Tab", "Shift+Tab", "Enter", "Space", "Escape",
        "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End",
        "h", "k", "b", "d"
    };

    public static bool Allows(string? key) => key is not null && Allowed.Contains(key);
}
