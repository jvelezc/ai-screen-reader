namespace HeadlessScreenReader.Domain;

/// <summary>One fresh pointer experiment. Dispatch, completed input and observed goal are separate facts.</summary>
public sealed record PointerTaskReceipt(string RunId, DateTimeOffset StartedAtUtc,
    AccessibilitySnapshot Before, AccessibilitySnapshot? After,
    bool Attempted, bool Completed, bool GoalObserved, string? ErrorCode)
{
    public string Mechanism => "Pointer";
    public bool InputMayHaveOccurred => Attempted;
    public string GoalObservation => After is null ? "Unavailable" : GoalObserved ? "Observed" : "NotObserved";
    public int KeyboardInputs => 0;
}
