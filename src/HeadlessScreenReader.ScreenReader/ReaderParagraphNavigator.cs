using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

internal static class ReaderParagraphNavigator
{
    internal const int MaxRuns = 128;
    internal const int MaxUtf16Length = 32768;

    public static bool IsCommand(ReaderCommand command) => command is ReaderCommand.ReadCurrentParagraph
        or ReaderCommand.NextParagraph or ReaderCommand.PreviousParagraph;

    public static (ReaderOutcome Outcome, string? TargetId, string Announcement, ReaderTextPassage? Passage, string? ErrorCode) Move(
        AccessibilitySnapshot snapshot, string? currentId, ReaderCommand command)
    {
        var byId = new Dictionary<string, AccessibleNode>(StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes)
            if (!byId.TryAdd(node.Id, node)) return Unknown(null, "PARAGRAPH_NODE_ID_CONFLICT");

        List<AccessibleNode>? Path(AccessibleNode node)
        {
            var result = new List<AccessibleNode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (seen.Add(node.Id))
            {
                result.Add(node);
                if (node.Role == "RootWebArea") return result;
                if (node.ParentId is null || !byId.TryGetValue(node.ParentId, out node!)) return null;
            }
            return null;
        }

        var current = currentId is not null ? byId.GetValueOrDefault(currentId) : null;
        var currentPath = current is not null ? Path(current) : null;
        AccessibleNode? paragraph = currentPath?.FirstOrDefault(node => node.Role == "paragraph");
        if (command != ReaderCommand.ReadCurrentParagraph)
        {
            var backwards = command == ReaderCommand.PreviousParagraph;
            var index = current is null ? backwards ? snapshot.Nodes.Length : -1 : snapshot.Nodes.IndexOf(current);
            var enclosingId = paragraph?.Id;
            paragraph = null;
            for (var i = index + (backwards ? -1 : 1); i >= 0 && i < snapshot.Nodes.Length; i += backwards ? -1 : 1)
            {
                var candidate = snapshot.Nodes[i];
                if (candidate.Role != "paragraph" || backwards && candidate.Id == enclosingId) continue;
                paragraph = candidate;
                break;
            }
        }
        else if (current is not null && currentPath is null)
            return Unknown(null, "PARAGRAPH_ANCESTRY_UNKNOWN");
        if (paragraph is null) return (ReaderOutcome.NoMatch, null, "No matching exposed paragraph.", null, null);
        var ancestry = Path(paragraph);
        if (ancestry is null || paragraph.DocumentId is null
            || ancestry.Any(node => node.DocumentId != paragraph.DocumentId || node.FrameId != paragraph.FrameId))
            return Unknown(paragraph.Id, "PARAGRAPH_OWNERSHIP_UNKNOWN");
        if (command == ReaderCommand.ReadCurrentParagraph && currentPath!.Any(node =>
            node.DocumentId != paragraph.DocumentId || node.FrameId != paragraph.FrameId))
            return Unknown(paragraph.Id, "PARAGRAPH_OWNERSHIP_UNKNOWN");
        var owned = new List<AccessibleNode>();
        foreach (var node in snapshot.Nodes)
        {
            if (node.Id == paragraph.Id) continue;
            var path = Path(node);
            if (path is null || !path.Any(parent => parent.Id == paragraph.Id)) continue;
            if (path.Any(parent => parent.DocumentId != paragraph.DocumentId || parent.FrameId != paragraph.FrameId))
                return Unknown(paragraph.Id, "PARAGRAPH_OWNERSHIP_UNKNOWN");
            if (!IsTextual(node.Role))
                return (ReaderOutcome.Unsupported, paragraph.Id, "Paragraph contains an unsupported content boundary.", null, "PARAGRAPH_CONTENT_UNSUPPORTED");
            if (node.Role is "StaticText" or "LineBreak")
            {
                if (node.Text is null) return Unknown(paragraph.Id, "PARAGRAPH_TEXT_UNOBSERVED");
                if (path.Skip(1).TakeWhile(parent => parent.Id != paragraph.Id).Any(parent => parent.Role is "StaticText" or "LineBreak" or "InlineTextBox"))
                    return Unknown(paragraph.Id, "PARAGRAPH_OVERLAPPING_TEXT");
                owned.Add(node);
            }
        }
        foreach (var link in snapshot.Nodes.Where(node => node.Role == "link" && node.Name.Length > 0))
        {
            var path = Path(link);
            if (path?.Any(node => node.Id == paragraph.Id) == true
                && !owned.Any(run => run.Text!.Text.Length > 0 && Path(run)!.Any(node => node.Id == link.Id)))
                return Unknown(paragraph.Id, "PARAGRAPH_LINK_TEXT_UNOBSERVED");
        }
        if (owned.Count > MaxRuns || owned.Sum(node => (long)node.Text!.Text.Length) > MaxUtf16Length)
            return (ReaderOutcome.Unsupported, paragraph.Id, "Paragraph exceeds supported text bounds.", null, "PARAGRAPH_TEXT_LIMIT");
        var fragments = owned.Select(node => new ReaderTextRange(paragraph.DocumentId, node.Id, 0, node.Text!.Text.Length,
            node.Text.Text, TextGranularity.TextRun, node.Text.Source, node.FrameId)).ToImmutableArray();
        var passage = new ReaderTextPassage(paragraph.DocumentId, paragraph.Id, fragments, paragraph.FrameId);
        return passage.Text.Length == 0 ? (ReaderOutcome.NoMatch, paragraph.Id, "Paragraph has no exposed text.", null, null)
            : (command == ReaderCommand.ReadCurrentParagraph ? ReaderOutcome.Observed : ReaderOutcome.CursorMoved,
                paragraph.Id, passage.Text, passage, null);
    }

    private static bool IsTextual(string role) => role is "StaticText" or "LineBreak" or "InlineTextBox"
        or "generic" or "link" or "emphasis" or "strong" or "mark" or "code" or "insertion" or "deletion"
        or "superscript" or "subscript";

    private static (ReaderOutcome, string?, string, ReaderTextPassage?, string?) Unknown(string? targetId, string code) =>
        (ReaderOutcome.Unknown, targetId, "Paragraph text or ownership is not fully observed.", null, code);
}
