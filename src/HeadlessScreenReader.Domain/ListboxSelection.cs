using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public sealed record MultiSelectableObservation(bool Value, SemanticSource Source);
public sealed record SelectionObservation(bool Value, SemanticSource Source);
public sealed record ExposedOptionSelection(string NodeId, string Name, SelectionObservation? Selection, bool IsDisabled);

/// <summary>Only currently exposed, locally owned options. Never a complete logical selected set.</summary>
public sealed record ListboxSelectionContext(
    string ListboxNodeId, string Name, string DocumentId, string FrameId,
    MultiSelectableObservation? MultiSelectable, ImmutableArray<ExposedOptionSelection> Options)
{
    public bool ExposedOptionsOnly => true;

    public static ListboxSelectionContext? Observe(AccessibilitySnapshot? snapshot, AccessibleNode? node)
    {
        if (snapshot is null || node is null || Owner(snapshot, node) is not { } owner) return null;
        return new(owner.Id, owner.Name, owner.DocumentId!, owner.FrameId!, owner.MultiSelectable,
            snapshot.Nodes.Where(option => option.Role == "option" && Owner(snapshot, option)?.Id == owner.Id)
                .Select(option => new ExposedOptionSelection(option.Id, option.Name,
                    option.Selection is { } selection && option.Selected == selection.Value ? selection : null,
                    option.IsDisabled)).ToImmutableArray());
    }

    private static AccessibleNode? Owner(AccessibilitySnapshot snapshot, AccessibleNode node)
    {
        // Require identities, not flat order, labels or a cross-document active-descendant reference.
        if (node.DocumentId is null || node.FrameId is null || node.Role is not ("option" or "listbox")) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = node;
        while (seen.Add(current.Id))
        {
            if (current.DocumentId != node.DocumentId || current.FrameId != node.FrameId) return null;
            if (current.Role == "listbox") return current;
            if (current.Role == "RootWebArea" || current != node && current.Role == "option") return null;
            current = snapshot.Nodes.FirstOrDefault(parent => parent.Id == current.ParentId);
            if (current is null) return null;
        }
        return null;
    }
}

/// <summary>Exact set among exposed options of one live listbox, not verification of hidden selections.</summary>
public sealed record ExposedSelectionExpectation(
    string ListboxNodeId, string DocumentId, string FrameId, ImmutableArray<string> SelectedOptionIds)
{
    public bool IsSatisfiedBy(AccessibilitySnapshot snapshot, AccessibleNode node)
    {
        var context = ListboxSelectionContext.Observe(snapshot, node);
        if (context is null || context.ListboxNodeId != ListboxNodeId || context.DocumentId != DocumentId
            || context.FrameId != FrameId || context.MultiSelectable?.Value != true || SelectedOptionIds.IsDefault
            || context.Options.Any(option => option.Selection is null)) return false;
        var expected = SelectedOptionIds.ToHashSet(StringComparer.Ordinal);
        return expected.Count == SelectedOptionIds.Length && expected.SetEquals(
            context.Options.Where(option => option.Selection!.Value).Select(option => option.NodeId));
    }
}
