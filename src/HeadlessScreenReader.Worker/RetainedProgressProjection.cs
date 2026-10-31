using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Projects only validated retained facts. An intent with no result remains an unresolved edge.</summary>
public static class RetainedProgressProjection
{
    public static RetainedProgressGraph Graph(CommandProgressSnapshot snapshot, FixtureJob job, Guid worker)
    {
        var progress = CommandProgressProjection.Project(snapshot.Summary, job, worker);
        var files = ImmutableArray.CreateBuilder<RetainedCheckpointDescriptor>();
        var nodes = new Dictionary<string, (TraversalObservation Observation, int Sequence)>(StringComparer.Ordinal);
        var edges = new List<RetainedProgressEdge>();
        void Observe(TraversalObservation observation, int sequence)
        {
            if (nodes.TryGetValue(observation.Id, out var prior))
                FixtureProgressProtocol.Require(FixtureProgressProtocol.Same(prior.Observation, observation));
            else nodes.Add(observation.Id, (observation, sequence));
        }
        foreach (var item in snapshot.Originals)
        {
            var cp = item.Checkpoint; var bytes = FixtureProgressProtocol.Encode(cp);
            files.Add(new(item.Sequence, item.SourceSha256, TraversalArchive.Hash(bytes), bytes.Length, cp.Phase, cp.CommandIndex));
            Observe(cp.Source, item.Sequence);
            if (cp.Result is { } result) Observe(result, item.Sequence);
            if (cp.Phase == TraversalCheckpointPhase.Intent)
                edges.Add(new(cp.CommandIndex, item.Sequence, null, cp.Source.Id, null, cp.Instruction!, null, null, null, false));
            if (cp.Phase == TraversalCheckpointPhase.Observed)
                edges[^1] = edges[^1] with
                {
                    ObservedSequence = item.Sequence,
                    ResultObservationId = cp.Result?.Id,
                    CompletedInputCalls = cp.Attempt!.Step?.BrowserKeystrokes,
                    Verification = cp.Attempt.Step?.Verification,
                    Outcome = cp.Attempt.Step?.Outcome,
                    TimelineTruncated = cp.Attempt.TimelineTruncated
                };
        }
        var graph = new RetainedProgressGraph(RetainedProgressGraph.CurrentSchema, progress, files.ToImmutable(),
            [.. nodes.Values.Select(n => new RetainedProgressNode(n.Observation.Id, n.Observation.Snapshot.DocumentId, n.Observation.Fingerprint, n.Sequence))], [.. edges]);
        graph.Validate(); return graph;
    }

    public static RetainedCheckpointPage Checkpoint(CommandProgressSnapshot snapshot, FixtureJob job, Guid worker,
        string prefixSha256, int sequence, int offset)
    {
        var graph = Graph(snapshot, job, worker);
        FixtureProgressProtocol.Require(graph.Progress.PrefixSha256 == prefixSha256 && sequence >= 1 && sequence <= graph.Checkpoints.Length);
        var file = graph.Checkpoints[sequence - 1]; var bytes = FixtureProgressProtocol.Encode(snapshot.Originals[sequence - 1].Checkpoint);
        FixtureProgressProtocol.Require(offset >= 0 && offset <= bytes.Length && (offset == bytes.Length || offset % RetainedCheckpointPage.PageBytes == 0));
        var data = bytes.AsSpan(offset, Math.Min(RetainedCheckpointPage.PageBytes, bytes.Length - offset)).ToArray();
        var next = offset + data.Length;
        return new(graph.Progress, sequence, file.SourceSha256, file.CheckpointSha256, bytes.Length, offset, data,
            TraversalArchive.Hash(data), next < bytes.Length ? next : null);
    }
}
