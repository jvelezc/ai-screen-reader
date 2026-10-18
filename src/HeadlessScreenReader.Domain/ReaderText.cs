namespace HeadlessScreenReader.Domain;

/// <summary>Exact, non-editable exposed text; distinct from a control's accessible name.</summary>
public sealed record ExposedText(string Text, SemanticSource Source);

public enum TextGranularity { TextRun, Character, Word, Line }
public enum TextCursorResetReason { DocumentChanged, NodeRemoved, TextChanged }

/// <summary>UTF-16 offsets into one observed text run. Lines are newline-delimited, not visual wraps.</summary>
public sealed record ReaderTextRange(
    string DocumentId, string NodeId, int Start, int Length, string Text,
    TextGranularity Granularity, SemanticSource Source, string? FrameId = null);
