using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Local bearer authority. Keep this token out of evidence and logs; no human input role is enabled.</summary>
public sealed record ControllerLease(string ScanId, Guid Token, long Generation, DateTimeOffset ExpiresAt);
public enum ControllerPhase { Unowned, Acquiring, Active, Revoking, Closing, Closed, Faulted }
public sealed record ControllerEvent(long Sequence, long Generation, ControllerPhase Phase, string Code, double ElapsedMilliseconds);
public sealed record ControllerStatus(string ScanId, ControllerPhase Phase, long Generation, DateTimeOffset? ExpiresAt,
    int ActiveCommands, string? FaultCode, ImmutableArray<ControllerEvent> Events, bool EventsTruncated, OwnedScanStatus Owned)
{
    public string Schema => "hsr-local-controller-v1";
}
