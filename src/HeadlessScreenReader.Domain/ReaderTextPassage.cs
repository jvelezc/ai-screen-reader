using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>One observed paragraph; fragments retain exact per-run UTF-16 offsets and provenance. No separators are inferred.</summary>
public sealed record ReaderTextPassage(
    string DocumentId, string ContainerNodeId, ImmutableArray<ReaderTextRange> Fragments, string? FrameId = null)
{
    public string Text => Fragments.IsDefaultOrEmpty ? "" : string.Concat(Fragments.Select(fragment => fragment.Text));
}
