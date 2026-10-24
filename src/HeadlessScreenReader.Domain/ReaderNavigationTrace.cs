namespace HeadlessScreenReader.Domain;

/// <summary>A reader state tied to an actual capture; virtual cursor and browser focus stay separate.</summary>
public sealed record ReaderTraceState(string Id, string CaptureId, string DocumentId, long CaptureEpoch,
    ReaderMode Mode, string? CursorNodeId, string? FocusedNodeId, string? ActiveDescendantNodeId);

/// <summary>A read-only cursor command, never a keyboard path or a verified objective.</summary>
public sealed record ReaderTraceEdge(string Id, string SourceStateId, string ResultStateId,
    ReaderCommand Command, string ExpectedDocumentId, ReaderOutcome Outcome,
    VerificationStatus Verification, int BrowserKeystrokes, bool InputMayHaveOccurred)
{
    public string Mechanism => "ReaderCursor";
}

/// <summary>
/// Source/result share one fresh semantic capture because the command sends no browser input.
/// RefreshedFromStateId links the preceding retained result to this fresh source by observation,
/// not by a claimed action. CaptureWindow uses milliseconds from session admission.
/// </summary>
public sealed record ReaderNavigationTraceEntry(long Sequence, int CaptureSequence, string CaptureId,
    AccessibilitySnapshot Snapshot, EvidenceInterval CaptureWindow, string? RefreshedFromStateId,
    ReaderTraceState SourceState, ReaderTraceState ResultState, ReaderTraceEdge Edge, ReaderStep Step);
