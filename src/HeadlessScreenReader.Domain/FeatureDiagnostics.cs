using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public enum DiagnosticHintState { Absent, Observed, RejectedOversize, Unavailable }
public enum DiagnosticHintSource { DeclaredFeatureAttribute }

/// <summary>A diagnostic hypothesis, never a replacement accessible name or verified objective.</summary>
public sealed record DiagnosticFeatureHint(string Value, DiagnosticHintSource Source);
public sealed record FeatureCandidate(string Id, AccessibleNode ObservedNode,
    DiagnosticFeatureHint? Hint, DiagnosticHintState HintState, bool StableDuringDiscovery);
public sealed record FeatureDiscovery(string DocumentId, long CaptureEpoch,
    ImmutableArray<FeatureCandidate> Candidates, bool Truncated, ImmutableArray<string> Limitations)
{
    public string Schema => "hsr-feature-discovery-v1";
    public string DiscoveryId { get; init; } = Guid.NewGuid().ToString("N");
}

/// <summary>A separate diagnostic surface; ordinary reader snapshots never include these hints.</summary>
public interface IFeatureDiagnostics
{
    Task<FeatureDiscovery> CaptureFeatureDiagnosticsAsync(CancellationToken cancellationToken = default);
}

public enum FeatureSourceBinding { Matched, NoSourceObservation, DocumentMismatch, EpochMismatch, NodeMissing, NodeChanged, DiscoveryUnconfirmed }
public sealed record FeaturePathRelation(string AttemptId, string SourceObservationId, string? ResultObservationId);
public sealed record FeatureEvidence(FeatureCandidate Candidate, FeatureSourceBinding Binding,
    string? SourceObservationId, ImmutableArray<string> ObservationIds, ImmutableArray<FeaturePathRelation> Relations)
{
    public bool HasExposedName => Candidate.ObservedNode.Name.Length > 0;
}
public sealed record FeatureInventory(string RunId, FeatureDiscovery Discovery, ImmutableArray<FeatureEvidence> Features)
{
    public string Schema => "hsr-feature-inventory-v1";
}
