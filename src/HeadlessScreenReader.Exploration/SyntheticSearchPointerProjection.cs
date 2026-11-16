using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

public static class SyntheticSearchPointerProjection
{
    public static SearchPointerGraph Build(SearchPointerPreparation? preparation, PointerTaskReceipt? activation)
    {
        var nodes = ImmutableArray.CreateBuilder<SearchPointerNode>();
        var edges = ImmutableArray.CreateBuilder<SearchPointerEdge>();
        string? Add(AccessibilitySnapshot? snapshot)
        {
            if (snapshot is null) return null;
            var id = "p" + nodes.Count;
            nodes.Add(new(id, snapshot));
            return id;
        }
        var source = Add(preparation?.Focus?.Before);
        if (preparation?.Focus is { } focus && source is not null)
        {
            var target = Add(focus.After);
            var consistent = !focus.InputCompleted || focus.InputAttempted;
            edges.Add(new("focus", source, target, "NativeMouseFocus", focus.InputAttempted ? 1 : 0,
                focus.InputCompleted ? 1 : 0, consistent, !consistent || focus.InputAttempted && !focus.InputCompleted,
                [], focus.ErrorCode));
            source = target;
        }
        if (preparation?.Entry is { } entry && source is not null)
        {
            var target = Add(preparation.AfterEntry);
            var events = preparation.Timeline.IsDefault ? [] : preparation.Timeline
                .Where(e => e.CommandSequence == entry.Sequence && e.Command == ReaderCommand.EnterSearchText).ToArray();
            var attempts = events.Where(e => e.Kind == ReaderEventKind.InputAttempted).ToArray();
            var inputs = attempts.Select((attempt, i) => new TraversalKeyTrace(attempt.Detail, attempt.Sequence,
                events.FirstOrDefault(e => e.Kind == ReaderEventKind.InputCompleted && e.Detail == attempt.Detail
                    && e.Sequence > attempt.Sequence && (i + 1 == attempts.Length || e.Sequence < attempts[i + 1].Sequence))?.Sequence)).ToImmutableArray();
            var completed = inputs.Count(i => i.CompletionEventSequence is not null);
            var consistent = !preparation.TimelineTruncated && entry.Command == ReaderCommand.EnterSearchText && completed == entry.BrowserKeystrokes
                && events.Count(e => e.Kind == ReaderEventKind.InputCompleted) == completed;
            edges.Add(new("text", source, target, "ReaderKeyboardText", inputs.Length, completed, consistent,
                !consistent || completed != inputs.Length || entry.InputMayHaveOccurred, inputs, entry.ErrorCode));
            source = target;
        }
        if (activation is not null && source is not null)
        {
            // Activation's own before-snapshot remains in its original receipt. A changed
            // document is never silently replaced with the preparation's last state.
            if (nodes.Last().Snapshot != activation.Before)
                source = Add(activation.Before)!;
            var target = Add(activation.After);
            var consistent = !activation.Completed || activation.Attempted;
            edges.Add(new("activate", source, target, "NativeMouseActivation", activation.Attempted ? 1 : 0,
                activation.Completed ? 1 : 0, consistent, !consistent || activation.Attempted && !activation.Completed,
                [], activation.ErrorCode));
        }
        return new(nodes.ToImmutable(), edges.ToImmutable());
    }
}
