using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>Links diagnostic hypotheses to observed trace facts without assigning feature success.</summary>
public static class FeatureProvenance
{
    public static FeatureInventory Build(FeatureDiscovery discovery, TraversalReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(receipt);
        if (string.IsNullOrWhiteSpace(discovery.DocumentId) || string.IsNullOrWhiteSpace(discovery.DiscoveryId)
            || discovery.CaptureEpoch < 1 || discovery.Candidates.IsDefault || discovery.Limitations.IsDefault || discovery.Candidates.Length > 64
            || discovery.Candidates.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || c.ObservedNode is null
                || string.IsNullOrWhiteSpace(c.ObservedNode.Id) || c.ObservedNode.Name is null
                || c.ObservedNode.DocumentId != discovery.DocumentId || c.ObservedNode.Role is not ("button" or "link")
                || c.ObservedNode.Editability is not null || c.ObservedNode.Protection?.Value == true
                || !Enum.IsDefined(c.HintState) || c.Hint is { } hint
                    && (string.IsNullOrWhiteSpace(hint.Value) || hint.Value.Length > 128 || !Enum.IsDefined(hint.Source))
                || (c.HintState == DiagnosticHintState.Observed) != (c.Hint is not null))
            || discovery.Candidates.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != discovery.Candidates.Length
            || discovery.Candidates.Select(c => c.ObservedNode.Id).Distinct(StringComparer.Ordinal).Count() != discovery.Candidates.Length)
            throw new ArgumentException("FEATURE_DISCOVERY_INVALID", nameof(discovery));
        _ = TraversalProjection.Build(receipt);
        var source = receipt.Observations.FirstOrDefault();
        bool Same(AccessibleNode? node, AccessibleNode expected) => node is not null
            && JsonSerializer.Serialize(node) == JsonSerializer.Serialize(expected);
        var features = discovery.Candidates.Select(candidate =>
        {
            var node = source?.Snapshot.Nodes.FirstOrDefault(n => n.Id == candidate.ObservedNode.Id);
            var binding = !candidate.StableDuringDiscovery ? FeatureSourceBinding.DiscoveryUnconfirmed
                : source is null ? FeatureSourceBinding.NoSourceObservation
                : source.Snapshot.DocumentId != discovery.DocumentId ? FeatureSourceBinding.DocumentMismatch
                : source.Snapshot.CaptureEpoch != discovery.CaptureEpoch ? FeatureSourceBinding.EpochMismatch
                : node is null ? FeatureSourceBinding.NodeMissing
                : !Same(node, candidate.ObservedNode) ? FeatureSourceBinding.NodeChanged : FeatureSourceBinding.Matched;
            if (binding != FeatureSourceBinding.Matched)
                return new FeatureEvidence(candidate, binding, null, [], []);
            var observations = receipt.Observations.Where(o => o.Snapshot.DocumentId == discovery.DocumentId
                && o.Snapshot.CaptureEpoch == discovery.CaptureEpoch
                && o.Snapshot.Nodes.Any(n => n.Id == candidate.ObservedNode.Id
                    && n.DocumentId == candidate.ObservedNode.DocumentId && n.FrameId == candidate.ObservedNode.FrameId
                    && n.Role == candidate.ObservedNode.Role)).ToArray();
            var observedIds = observations.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
            var encountered = observations.Where(o => o.CursorNodeId == candidate.ObservedNode.Id
                || o.Snapshot.FocusedNode?.Id == candidate.ObservedNode.Id
                || o.Snapshot.EffectiveFocusedNode?.Id == candidate.ObservedNode.Id).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
            var relations = receipt.Attempts.Where(a => observedIds.Contains(a.SourceObservationId)
                && (encountered.Contains(a.SourceObservationId) || a.ResultObservationId is { } result && encountered.Contains(result)))
                .Select(a => new FeaturePathRelation(a.Id, a.SourceObservationId, a.ResultObservationId)).ToImmutableArray();
            return new FeatureEvidence(candidate, binding, source!.Id, [.. observations.Select(o => o.Id)], relations);
        }).ToImmutableArray();
        return new(receipt.RunId, discovery, features);
    }
}
