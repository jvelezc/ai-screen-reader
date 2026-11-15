namespace HeadlessScreenReader.Domain;

/// <summary>A separate opt-in capability; ordinary browser implementations cannot enter text.</summary>
public interface ISyntheticSearchBrowser
{
    Task PressSearchKeyAsync(SearchKeyInput input, CancellationToken cancellationToken = default);
    Task<bool> SearchValueMatchesAsync(SearchKeyInput scope, string expected, CancellationToken cancellationToken = default);
}

/// <summary>One character in a fixed, nonpersonal synthetic query. It cannot clear or replace existing content.</summary>
public sealed record SearchKeyInput(string Key, string DocumentId, long CaptureEpoch, string NodeId, string ExpectedPrefix)
{
    public bool IsAuthorizedBy(AccessibilitySnapshot snapshot) => Key is { Length: 1 }
        && ExpectedPrefix is not null && SyntheticSearchPolicy.Query.StartsWith(ExpectedPrefix, StringComparison.Ordinal)
        && ExpectedPrefix.Length < SyntheticSearchPolicy.Query.Length
        && Key[0] == SyntheticSearchPolicy.Query[ExpectedPrefix.Length]
        && snapshot.DocumentId == DocumentId && snapshot.CaptureEpoch == CaptureEpoch
        && snapshot.FocusedNode is { Role: "searchbox", IsDisabled: false, ActiveDescendantId: null } focused
        && focused.Id == NodeId && snapshot.EffectiveFocusedNode?.Id == NodeId
        && focused.Editability?.Kind == "plaintext" && focused.Protection?.Value != true;
}

public static class SyntheticSearchPolicy
{
    public const string Profile = "hsr-synthetic-search-v1";
    public const string Query = "sample course";
    public static bool Allows(string? text) => text == Query;

    public static TraversalPlan KeyboardPlan(string activationKey)
    {
        if (activationKey is not ("Enter" or "Space")) throw new ArgumentException("Only declared synthetic activation keys are supported.", nameof(activationKey));
        var initial = new TraversalInstruction[] { new(ReaderCommand.Tab), new(ReaderCommand.EnterSearchText, Query), new(ReaderCommand.Tab) };
        return new("Synthetic course search: " + activationKey, new("heading", "Find a sample course"),
            new("heading", "Sample course result", MustBeFocused: true), activationKey == "Enter"
                ? [.. initial, new(ReaderCommand.Activate)]
                : [.. initial, new(ReaderCommand.EnterFocusMode), new(ReaderCommand.PressKey, "Space")]);
    }
}

/// <summary>Observed entry only. A completed query does not establish search submission or a task goal.</summary>
public sealed record SearchTextObservation(string Profile, string Text, int CompletedCharacters, bool ValueVerified);
