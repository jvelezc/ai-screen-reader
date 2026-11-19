using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli;

/// <summary>Tracks actual reader focus states instead of guessing reachability from an input count.</summary>
internal sealed class PublicTaskKeyboardPath(PublicTaskGoal goal)
{
    private readonly Dictionary<(string Document, string Node), int> _visited = [];
    private string _reason = "NotStarted";
    private int _tabs;
    private int? _cycle;

    internal bool Continue(string? documentId, PublicTaskFocus? focus, int completedTabs)
    {
        _tabs = completedTabs;
        if (focus?.AnchorUrl == goal.DestinationUrl) { _reason = "GoalFocused"; return false; }
        if (documentId is null || focus is null) { _reason = "FocusUnobserved"; return false; }
        var state = (documentId, focus.NodeId);
        if (_visited.TryGetValue(state, out var first))
        { _reason = "FocusCycle"; _cycle = first; return false; }
        _visited.Add(state, completedTabs);
        return true;
    }

    internal void Unconfirmed(int tabs) { _tabs = tabs; _reason = "InputUnconfirmed"; _cycle = null; }
    internal PublicTaskNavigation Record() => new(PublicTaskNavigation.CurrentStrategy, _reason, _tabs, _visited.Count, _cycle);
}
