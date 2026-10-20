namespace HeadlessScreenReader.Domain;

/// <summary>One scoped character, never a general text-entry capability.</summary>
public sealed record ListboxKeyInput(string Key, string SnapshotDocumentId, string ListboxNodeId, string DocumentId, string FrameId)
{
    public bool IsAuthorizedBy(AccessibilitySnapshot snapshot)
    {
        var context = ListboxTypeaheadPolicy.FocusContext(snapshot);
        return ListboxTypeaheadPolicy.AllowsPrefix(Key) && Key.Length == 1
            && snapshot.DocumentId == SnapshotDocumentId && context is not null && context.ListboxNodeId == ListboxNodeId
            && context.DocumentId == DocumentId && context.FrameId == FrameId
            && snapshot.FocusedNode?.IsDisabled != true && snapshot.EffectiveFocusedNode?.IsDisabled != true
            && snapshot.Nodes.First(node => node.Id == context.ListboxNodeId).IsDisabled == false;
    }
}

public static class ListboxTypeaheadPolicy
{
    public const int MaximumPrefixLength = 16;
    public const int ResetIdleMilliseconds = 1100;

    public static bool AllowsPrefix(string? prefix) => prefix is { Length: >= 1 and <= MaximumPrefixLength }
        && prefix.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9');

    public static ListboxSelectionContext? FocusContext(AccessibilitySnapshot snapshot)
    {
        var focused = snapshot.FocusedNode;
        var effective = snapshot.EffectiveFocusedNode;
        if (focused?.Role is not ("listbox" or "option") || effective?.Role != "option"
            || focused.Editability is not null || effective.Editability is not null) return null;
        var context = ListboxSelectionContext.Observe(snapshot, effective);
        if (context is null || !(focused.Id == effective.Id && focused.ActiveDescendantId is null
            || focused.Id == context.ListboxNodeId && focused.ActiveDescendantId == effective.Id)) return null;
        var node = snapshot.Nodes.First(n => n.Id == context.ListboxNodeId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(node.Id))
        {
            if (node.DocumentId != context.DocumentId || node.FrameId != context.FrameId
                || node.Editability is not null || node.Role == "combobox") return null;
            if (node.Role == "RootWebArea") return context;
            node = snapshot.Nodes.FirstOrDefault(n => n.Id == node.ParentId);
            if (node is null) return null;
        }
        return null;
    }
}

/// <summary>Observed focus after attempted characters; not the widget's internal buffer or selected set.</summary>
public sealed record TypeaheadObservation(string RequestedPrefix, int CompletedCharacters, string ListboxNodeId,
    string? ObservedOptionId, bool? PrefixMatchesObservedFocus)
{
    public int ResetIdleMilliseconds => ListboxTypeaheadPolicy.ResetIdleMilliseconds;
}
