using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

internal static class ReaderContextBuilder
{
    public static ReaderContext? Build(AccessibilitySnapshot? snapshot, AccessibleNode? node)
    {
        if (snapshot is null || node is null) return null;
        var byId = snapshot.Nodes.ToDictionary(item => item.Id);
        var (parents, complete) = Parents(node, byId);
        var containers = parents.Where(IsContainer).Reverse()
            .Select(item => new AccessibleContainer(item.Id, item.Role, item.Name)).ToImmutableArray();
        var owner = parents.TakeWhile(item => item.Role != "RootWebArea").FirstOrDefault(item => IsPeerContainer(node.Role, item.Role));
        ExposedPeerPosition? position = null;
        if (complete && owner is not null)
        {
            var peers = snapshot.Nodes.Where(item => item.Role == node.Role).Where(item =>
            {
                var (ancestors, valid) = Parents(item, byId);
                return valid && ancestors.TakeWhile(parent => parent.Role != "RootWebArea")
                    .FirstOrDefault(parent => IsPeerContainer(item.Role, parent.Role))?.Id == owner.Id;
            }).ToArray();
            var index = Array.FindIndex(peers, item => item.Id == node.Id);
            if (index >= 0) position = new(owner.Id, index + 1, peers.Length);
        }
        return new(node.Id, containers, complete, position, node.LogicalSet);
    }

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
        _ => false
    };

    private static bool IsContainer(AccessibleNode node) => node.Role is
        "main" or "navigation" or "banner" or "contentinfo" or "complementary" or "search" or "region"
        or "form" or "group" or "radiogroup" or "listbox" or "dialog" or "alertdialog" or "Iframe" or "IframePresentational";
}
