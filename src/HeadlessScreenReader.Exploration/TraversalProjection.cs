using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>Lossless local projection: one node per observation, without speculative state clustering.</summary>
public static class TraversalProjection
{
    public static TraversalGraph Build(TraversalReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var ids = receipt.Observations.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != receipt.Observations.Length
            || receipt.Attempts.Any(a => !ids.Contains(a.SourceObservationId)
                || a.ResultObservationId is not null && !ids.Contains(a.ResultObservationId))
            || receipt.Attempts.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != receipt.Attempts.Length)
            throw new ArgumentException("Traversal contains duplicate or missing evidence references.", nameof(receipt));
        return new("execution-trace-v2",
            [.. receipt.Observations.Select(o => new TraversalGraphNode(o.Id, o.Snapshot.DocumentId, o.Fingerprint)
            {
                Label = Label(o), Mode = o.Mode, CursorNodeId = o.CursorNodeId,
                FocusedNodeId = o.Snapshot.FocusedNode?.Id,
                ActiveDescendantNodeId = o.Snapshot.FocusedNode?.ActiveDescendantId
            })],
            [.. receipt.Attempts.Where(a => a.ResultObservationId is not null).Select(a => Edge(a, receipt.TimelineTruncated))])
        { UnresolvedAttempts = [.. receipt.Attempts.Where(a => a.ResultObservationId is null)] };
    }

    private static string Label(TraversalObservation observation)
    {
        var snapshot = observation.Snapshot;
        var node = snapshot.Nodes.FirstOrDefault(n => n.Id == observation.CursorNodeId)
            ?? (snapshot.EffectiveFocusedNode?.Role != "RootWebArea" ? snapshot.EffectiveFocusedNode : null)
            ?? snapshot.Nodes.FirstOrDefault(n => n.Role == "heading") ?? snapshot.Nodes.FirstOrDefault();
        return node is null ? "No exposed node" : $"{node.Role}: {(node.Name.Length == 0 ? "(unnamed)" : node.Name)}";
    }

    private static TraversalGraphEdge Edge(TraversalAttempt attempt, bool timelineTruncated)
    {
        var keys = new List<TraversalKeyTrace>();
        var unmatchedCompletion = false;
        foreach (var item in attempt.Events)
        {
            if (item.Kind == ReaderEventKind.InputAttempted)
                keys.Add(new(item.Detail, item.Sequence, null));
            else if (item.Kind == ReaderEventKind.InputCompleted)
            {
                var index = keys.FindIndex(k => k.Key == item.Detail && k.CompletionEventSequence is null);
                if (index < 0)
                {
                    unmatchedCompletion = true;
                    keys.Add(new(item.Detail, null, item.Sequence));
                }
                else keys[index] = keys[index] with { CompletionEventSequence = item.Sequence };
            }
        }
        return new(attempt.Id, attempt.SourceObservationId, attempt.ResultObservationId!, attempt.Step?.Outcome,
            attempt.Step?.Verification ?? VerificationStatus.NotRequested)
        {
            Command = attempt.Instruction.Command,
            KeyboardInputs = [.. keys],
            InputEvidenceComplete = !timelineTruncated && !attempt.TimelineTruncated && !unmatchedCompletion
                && keys.Count(k => k.CompletionEventSequence is not null) == (attempt.Step?.BrowserKeystrokes ?? 0),
            CompletedKeystrokes = attempt.Step?.BrowserKeystrokes ?? 0,
            InputMayHaveOccurred = attempt.Step?.InputMayHaveOccurred ?? false
        };
    }
}
