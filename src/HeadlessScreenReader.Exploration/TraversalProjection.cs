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
        return new("observation-graph-v1",
            [.. receipt.Observations.Select(o => new TraversalGraphNode(o.Id, o.Snapshot.DocumentId, o.Fingerprint))],
            [.. receipt.Attempts.Where(a => a.ResultObservationId is not null).Select(a => new TraversalGraphEdge(
                a.Id, a.SourceObservationId, a.ResultObservationId!, a.Step?.Outcome,
                a.Step?.Verification ?? VerificationStatus.NotRequested))]);
    }
}
