namespace HeadlessScreenReader.Domain;

public enum FrameObservationStatus { Observed, Excluded, Unsupported, Unavailable, LimitExceeded }

/// <summary>Observed document coverage, not a claim about every frame or website accessibility.</summary>
public sealed record FrameObservation(
    string FrameId, string? ParentFrameId, string? OwnerNodeId, string? DocumentId,
    string? RootNodeId, FrameObservationStatus Status, string? ReasonCode = null);
