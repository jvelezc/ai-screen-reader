namespace HeadlessScreenReader.Domain;

/// <summary>Native mouse focus preparation, separate from text entry and the eventual search goal.</summary>
public sealed record SearchFocusReceipt(DateTimeOffset StartedAtUtc, AccessibilitySnapshot Before,
    AccessibilitySnapshot? After, bool InputAttempted, bool InputCompleted, bool FocusObserved, string? ErrorCode)
{
    public string Schema => "hsr-synthetic-search-focus-v1";
    public string Mechanism => "NativeMouseFocus";
}
