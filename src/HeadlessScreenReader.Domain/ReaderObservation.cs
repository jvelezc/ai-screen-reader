using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public sealed record ReaderObservationOptions
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public int MaxCaptures { get; init; } = 50;
}

public enum ObservationStreamStatus
{
    Observed, CaptureLimitReached, DurationElapsed, Cancelled,
    CommandBudgetExceeded, BrowserError, TimelineTruncated
}

/// <summary>Bounded sampled observations. Never a lossless event stream or successful input claim.</summary>
public sealed record ReaderObservationBatch(
    ObservationStreamStatus Status, int Captures, ImmutableArray<ReaderEvent> Events,
    string? ErrorCode = null)
{
    public bool SamplingMayMissChanges => true;
}
