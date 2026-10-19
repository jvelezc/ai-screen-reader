namespace HeadlessScreenReader.Domain;

public enum VerificationStatus { NotRequested, Satisfied, TimedOut, Interrupted, Blocked }

/// <summary>An explicit predicate over reader-visible semantics, never a hidden DOM target.</summary>
public sealed record ObservationExpectation(
    string Role,
    string Name,
    bool MustBeFocused = false,
    string? Checked = null,
    bool? Expanded = null,
    bool? Selected = null,
    double? RangeValue = null)
{
    public bool IsSatisfiedBy(AccessibilitySnapshot snapshot) =>
        snapshot.Nodes.Any(node => node.Role == Role && node.Name == Name
            && (!MustBeFocused || snapshot.EffectiveFocusedNode?.Id == node.Id)
            && (Checked is null || node.Checked == Checked)
            && (Expanded is null || node.Expanded == Expanded)
            && (Selected is null || node.Selected == Selected)
            && (RangeValue is null || double.IsFinite(RangeValue.Value)
                && node.Range is { IsConsistent: true, Value: double value } && value == RangeValue.Value));
}

public sealed record ObservationWaitOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(25);
    public int MaxPolls { get; init; } = 50;
}
