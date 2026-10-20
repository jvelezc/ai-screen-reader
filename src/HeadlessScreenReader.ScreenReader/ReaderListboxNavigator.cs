using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

internal static class ReaderListboxNavigator
{
    public static (ReaderOutcome Outcome, string? TargetId) Move(
        AccessibilitySnapshot snapshot, AccessibleNode? current, ReaderCommand command)
    {
        var context = ListboxSelectionContext.Observe(snapshot, current);
        if (context is null) return (ReaderOutcome.Unsupported, null);
        var index = -1;
        for (var i = 0; i < context.Options.Length; i++)
            if (context.Options[i].NodeId == current?.Id) index = i;
        var reverse = command == ReaderCommand.PreviousOption;
        if (reverse && index < 0) index = context.Options.Length;
        index += reverse ? -1 : 1;
        return index >= 0 && index < context.Options.Length
            ? (ReaderOutcome.CursorMoved, context.Options[index].NodeId) : (ReaderOutcome.NoMatch, null);
    }

    public static string Describe(ListboxSelectionContext? context, AccessibleNode node)
    {
        if (node.MultiSelectable?.Value != true && context?.MultiSelectable?.Value != true) return "";
        if (context is null) return node.Role == "listbox" && node.MultiSelectable?.Value == true
            ? ", multiple selections, option ownership unknown" : "";
        var text = node.Role == "listbox" ? context.MultiSelectable?.Value switch
        { true => ", multiple selections", false => ", single selection", null => ", selection mode unknown" } : "";
        if (node.Role == "option")
        {
            var selection = context.Options.FirstOrDefault(option => option.NodeId == node.Id)?.Selection;
            if (selection is null) text += ", selection unknown";
            else if (!selection.Value) text += ", not selected";
        }
        var selected = context.Options.Count(option => option.Selection?.Value == true);
        var unknown = context.Options.Count(option => option.Selection is null);
        return text + $", {selected} selected among {context.Options.Length} exposed options"
            + (unknown > 0 ? $", {unknown} selection states unknown" : "");
    }
}
