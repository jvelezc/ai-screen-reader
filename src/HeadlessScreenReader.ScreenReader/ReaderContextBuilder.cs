using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

internal static class ReaderContextBuilder
{
    public static bool IsLocalOwner(AccessibilitySnapshot snapshot, AccessibleNode node, string role, string ownerId)
    {
        var (parents, complete) = Parents(node, snapshot.Nodes.ToDictionary(item => item.Id));
        var owner = parents.TakeWhile(item => item.Role != "RootWebArea").FirstOrDefault(item => item.Role == role);
        return complete && owner is not null && owner.Id == ownerId && CompatibleDocument(node, owner);
    }

    public static ReaderContext? Build(AccessibilitySnapshot? snapshot, AccessibleNode? node)
    {
        if (snapshot is null || node is null) return null;
        var byId = snapshot.Nodes.ToDictionary(item => item.Id);
        var (parents, complete) = Parents(node, byId);
        var containers = parents.Where(IsContainer).Reverse()
            .Select(item => new AccessibleContainer(item.Id, item.Role, item.Name)).ToImmutableArray();
        var localParents = parents.TakeWhile(item => item.Role != "RootWebArea").ToArray();
        var owner = localParents.FirstOrDefault(item => IsPeerContainer(node.Role, item.Role));
        ExposedPeerPosition? position = null;
        // A flat level does not establish a nested parent. Count only a known
        // local owner and level, never all items appearing earlier in the tree.
        var treeScopeKnown = node.Role != "treeitem" || node.HierarchyLevel is { IsValid: true }
            && localParents.Any(item => item.Role == "tree")
            && (owner?.Role != "tree" || node.HierarchyLevel.Value == 1);
        if (complete && owner is not null && treeScopeKnown && CompatibleDocument(node, owner))
        {
            var peers = snapshot.Nodes.Where(item => item.Role == node.Role).Where(item =>
            {
                var (ancestors, valid) = Parents(item, byId);
                return valid && CompatibleDocument(node, item) && ancestors.TakeWhile(parent => parent.Role != "RootWebArea")
                    .FirstOrDefault(parent => IsPeerContainer(item.Role, parent.Role))?.Id == owner.Id
                    && (node.Role != "treeitem" || item.HierarchyLevel?.IsValid == true
                        && item.HierarchyLevel.Value == node.HierarchyLevel!.Value);
            }).ToArray();
            var index = Array.FindIndex(peers, item => item.Id == node.Id);
            if (index >= 0) position = new(owner.Id, index + 1, peers.Length);
        }
        return new(node.Id, containers, complete, position, node.LogicalSet, node.HierarchyLevel);
    }

    private static bool CompatibleDocument(AccessibleNode node, AccessibleNode other) =>
        node.DocumentId is null || other.DocumentId is null || node.DocumentId == other.DocumentId;

    private static (List<AccessibleNode> Parents, bool Complete) Parents(
        AccessibleNode node, Dictionary<string, AccessibleNode> byId)
    {
        var result = new List<AccessibleNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
        var parentId = node.ParentId;
        while (parentId is not null)
        {
            if (!seen.Add(parentId) || !byId.TryGetValue(parentId, out var parent)) return (result, false);
            result.Add(parent);
            parentId = parent.ParentId;
        }
        return (result, true);
    }

    private static bool IsPeerContainer(string role, string parentRole) => role switch
    {
        "radio" => parentRole is "group" or "radiogroup",
        "option" => parentRole == "listbox",
        "tab" => parentRole == "tablist",
        "treeitem" => parentRole is "tree" or "group" or "treeitem",
        _ => false
    };

    private static bool IsContainer(AccessibleNode node) => node.Role is
        "main" or "navigation" or "banner" or "contentinfo" or "complementary" or "search" or "region"
        or "form" or "group" or "radiogroup" or "listbox" or "dialog" or "alertdialog" or "Iframe" or "IframePresentational"
        or "tree" or "treeitem" or "tablist" or "tabpanel";
}
