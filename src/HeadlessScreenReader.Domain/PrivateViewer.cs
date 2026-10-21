namespace HeadlessScreenReader.Domain;

public sealed record ViewerScope(string ScanId, string SessionId);
/// <summary>Last acknowledged local observation, not a fresh capture or goal verification.</summary>
public sealed record RetainedViewerObservation(string ScanId, long Epoch, long Ordinal, AccessibilitySnapshot Snapshot,
    bool PriorSimulatedAssistance)
{
    public string Schema => "hsr-retained-viewer-observation-v1";
    public string ObservationKind => "LastPersistedObservation";
}
/// <summary>Ephemeral credential. Never retain it in evidence or logs.</summary>
public sealed class ViewerSecret(string value)
{
    public string Value { get; } = value;
    public override string ToString() => "[redacted viewer credential]";
}
public sealed record ViewerBootstrap(ViewerSecret Secret, DateTimeOffset ExpiresAt);
public sealed record ViewerConnection(Guid Id, ViewerSecret Secret, DateTimeOffset ExpiresAt);
public enum ViewerConnectionPhase { Active, Revoking, Revoked, Faulted }
public sealed record ViewerBrokerStatus(ViewerScope Scope, int IssuedBootstraps, int ActiveConnections, int ActiveReads, bool Closed);
