using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public enum CapturePhase { Permitted, Draining, Excluded, Resuming, Faulted, Closed }

/// <summary>Payload-free lifecycle evidence. An exclusion is a gap, never an observation.</summary>
public sealed record CaptureLifecycleEvent(long Sequence, CapturePhase Phase, long Epoch, double ElapsedMilliseconds);

public sealed record CaptureStatus(CapturePhase Phase, long Epoch,
    ImmutableArray<CaptureLifecycleEvent> Events, bool EventsTruncated);

/// <summary>Opaque authority to resume this exclusion only. Never a human input lease.</summary>
public sealed record CaptureExclusionLease(Guid Token, long Epoch);
