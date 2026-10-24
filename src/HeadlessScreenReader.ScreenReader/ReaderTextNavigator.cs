using System.Globalization;
using System.Text;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

internal sealed class ReaderTextNavigator
{
    private ReaderTextRange? _range;
    private string? _sourceText;
    private TextCursorResetReason? _resetPending;
    private string? _rootDocumentId;

    internal static bool IsCommand(ReaderCommand command) => command is ReaderCommand.ReadCurrentText
        or ReaderCommand.NextCharacter or ReaderCommand.PreviousCharacter or ReaderCommand.NextWord
        or ReaderCommand.PreviousWord or ReaderCommand.NextLine or ReaderCommand.PreviousLine;

    internal void Clear() { _range = null; _sourceText = null; _resetPending = null; _rootDocumentId = null; }

    internal (ReaderOutcome Outcome, string Announcement, ReaderTextRange? Range, TextCursorResetReason? Reset) Move(
        AccessibilitySnapshot snapshot, string? anchorId, ReaderCommand command)
    {
        if (_resetPending is not null)
            return (ReaderOutcome.Unknown, "Text cursor invalidated; re-anchor before continuing.", null, _resetPending);
        TextCursorResetReason? reset = null;
        if (_range is { } previous)
        {
            var oldNode = snapshot.Nodes.FirstOrDefault(node => node.Id == previous.NodeId);
            var currentDocument = previous.FrameId is null ? snapshot.DocumentId
                : snapshot.Frames.FirstOrDefault(frame => frame.FrameId == previous.FrameId)?.DocumentId ?? oldNode?.DocumentId;
            reset = _rootDocumentId != snapshot.DocumentId
                || currentDocument is not null && previous.DocumentId != currentDocument
                || oldNode is not null && (oldNode.FrameId != previous.FrameId
                    || (oldNode.DocumentId ?? snapshot.DocumentId) != previous.DocumentId) ? TextCursorResetReason.DocumentChanged
                : oldNode?.Text is null || oldNode.Role is not "StaticText" and not "LineBreak" ? TextCursorResetReason.NodeRemoved
                : oldNode.Text.Text != _sourceText || oldNode.Text.Source != previous.Source ? TextCursorResetReason.TextChanged : null;
            if (reset is not null || anchorId != previous.NodeId) Clear();
        }
        if (reset is not null)
        {
            _resetPending = reset;
            return (ReaderOutcome.Unknown, "Text cursor invalidated; re-anchor before continuing.", null, reset);
        }
        var runs = snapshot.Nodes.Where(node => node.Role is "StaticText" or "LineBreak" && node.Text is not null).ToArray();
        var backwards = command is ReaderCommand.PreviousCharacter or ReaderCommand.PreviousWord or ReaderCommand.PreviousLine;
        var granularity = command switch
        {
            ReaderCommand.NextCharacter or ReaderCommand.PreviousCharacter => TextGranularity.Character,
            ReaderCommand.NextWord or ReaderCommand.PreviousWord => TextGranularity.Word,
            ReaderCommand.NextLine or ReaderCommand.PreviousLine => TextGranularity.Line,
            _ => TextGranularity.TextRun
        };
        if (command == ReaderCommand.ReadCurrentText && _range is not null)
            return (ReaderOutcome.Observed, _range.Text, _range, reset);

        var byId = snapshot.Nodes.ToDictionary(node => node.Id);
        bool IsWithin(AccessibleNode node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                if (node.Id == anchorId) return true;
                if (!seen.Add(node.Id) || node.ParentId is null || !byId.TryGetValue(node.ParentId, out node!)) return false;
            }
        }
        var contained = runs.Select((node, index) => (node, index)).Where(item => IsWithin(item.node)).ToArray();
        var index = _range is not null ? Array.FindIndex(runs, node => node.Id == _range.NodeId)
            : contained.Length > 0 ? (backwards ? contained[^1].index : contained[0].index) : -1;
        if (command == ReaderCommand.ReadCurrentText)
        {
            if (index < 0 || runs[index].Text!.Text.Length == 0)
                return (ReaderOutcome.NoMatch, "No current exposed text.", null, reset);
            return Select(runs[index], 0, runs[index].Text!.Text.Length, ReaderOutcome.Observed);
        }
        if (index < 0)
        {
            var anchorIndex = -1;
            for (var i = 0; i < snapshot.Nodes.Length; i++) if (snapshot.Nodes[i].Id == anchorId) anchorIndex = i;
            if (anchorIndex < 0) index = backwards ? runs.Length - 1 : 0;
            else
            {
                var positions = snapshot.Nodes.Select((node, i) => (node.Id, i)).ToDictionary(item => item.Id, item => item.i);
                index = backwards ? Array.FindLastIndex(runs, node => positions[node.Id] < anchorIndex)
                    : Array.FindIndex(runs, node => positions[node.Id] > anchorIndex);
            }
        }
        for (; index >= 0 && index < runs.Length; index += backwards ? -1 : 1)
        {
            var node = runs[index];
            var units = Units(node.Text!.Text, granularity);
            if (backwards) units.Reverse();
            foreach (var unit in units)
            {
                if (_range is { Granularity: not TextGranularity.TextRun } selected && selected.NodeId == node.Id
                    && (backwards ? unit.Start >= selected.Start : unit.Start <= selected.Start)) continue;
                return Select(node, unit.Start, unit.Length, ReaderOutcome.CursorMoved);
            }
        }
        return (ReaderOutcome.NoMatch, "No matching exposed text.", _range, reset);

        (ReaderOutcome, string, ReaderTextRange?, TextCursorResetReason?) Select(
            AccessibleNode node, int start, int length, ReaderOutcome outcome)
        {
            _sourceText = node.Text!.Text;
            _rootDocumentId = snapshot.DocumentId;
            _range = new(node.DocumentId ?? snapshot.DocumentId, node.Id, start, length, _sourceText.Substring(start, length),
                granularity, node.Text.Source, node.FrameId);
            return (outcome, _range.Text, _range, reset);
        }
    }

    private static List<(int Start, int Length)> Units(string text, TextGranularity granularity)
    {
        var result = new List<(int Start, int Length)>();
        if (text.Length == 0) return result;
        if (granularity == TextGranularity.Line)
        {
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] is not '\r' and not '\n') continue;
                result.Add((start, i - start));
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = i + 1;
            }
            result.Add((start, text.Length - start));
            return result;
        }
        var starts = StringInfo.ParseCombiningCharacters(text);
        var elements = starts.Select((start, i) => (Start: start, Length: (i + 1 < starts.Length ? starts[i + 1] : text.Length) - start)).ToArray();
        if (granularity == TextGranularity.Character) return elements.ToList();
        Rune ElementRune(int index) => Rune.TryGetRuneAt(text, elements[index].Start, out var rune) ? rune : Rune.ReplacementChar;
        bool IsWord(int index) => Rune.GetUnicodeCategory(ElementRune(index)) is
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation;
        for (var i = 0; i < elements.Length; i++)
        {
            var unit = elements[i];
            if (Rune.IsWhiteSpace(ElementRune(i))) continue;
            var end = i;
            if (IsWord(i))
                while (end + 1 < elements.Length)
                {
                    if (IsWord(end + 1)) { end++; continue; }
                    var next = elements[end + 1];
                    if (next.Length == 1 && text[next.Start] is '\'' or '’' && end + 2 < elements.Length && IsWord(end + 2))
                    { end += 2; continue; }
                    break;
                }
            result.Add((unit.Start, elements[end].Start + elements[end].Length - unit.Start));
            i = end;
        }
        return result;
    }
}
