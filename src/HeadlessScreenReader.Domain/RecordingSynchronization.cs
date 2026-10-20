using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public sealed record ClockRoundTrip(double HostBeforeMilliseconds, double BrowserUnixMilliseconds, double HostAfterMilliseconds);
public sealed record RecordingClockCalibration(double BrowserMinusHostMilliseconds, double UncertaintyMilliseconds);
public sealed record VisualMarker(int Objective, int Tick);
public sealed record SourceMarkerFrame(string File, double PresentationUnixMilliseconds, VisualMarker? Marker);
public sealed record VideoMarkerFrame(int Index, double PresentationMilliseconds, VisualMarker? Marker);
public sealed record RecordedActionMarker(int Objective, long ReaderInputEventSequence, double HostAttemptMilliseconds,
    double HostCompletionMilliseconds, double BrowserKeydownUnixMilliseconds, bool ReaderGoalVerified);
public sealed record MarkerSynchronization(int Objective, int? VideoFrameIndex, double? MeasuredLagMilliseconds,
    double? ConservativeAbsoluteErrorMilliseconds);
public enum SynchronizationVerification { Verified, Unknown }
public sealed record RecordingSynchronizationResult(SynchronizationVerification Verification, string ReasonCode,
    double TargetMilliseconds, double? AnchorUnixMilliseconds, double? UncertaintyMilliseconds,
    ImmutableArray<MarkerSynchronization> Markers);
