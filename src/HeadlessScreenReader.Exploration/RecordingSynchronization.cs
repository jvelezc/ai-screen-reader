using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>Bounded synthetic synchronization evidence, independent of browser APIs and accessibility conclusions.</summary>
public static class RecordingSynchronization
{
    public const double TargetMilliseconds = 250;

    public static RecordingClockCalibration? Calibrate(ImmutableArray<ClockRoundTrip> samples)
    {
        if (samples.IsDefaultOrEmpty || samples.Length > 32 || samples.Any(s =>
            !Finite(s.HostBeforeMilliseconds, s.BrowserUnixMilliseconds, s.HostAfterMilliseconds)
            || s.HostBeforeMilliseconds < 0 || s.BrowserUnixMilliseconds <= 0 || s.HostAfterMilliseconds < s.HostBeforeMilliseconds)) return null;
        // Browser performance clocks can be quantized to a millisecond. Inconsistent
        // round-trip intervals cannot be repaired by selecting a convenient offset.
        var low = samples.Max(s => s.BrowserUnixMilliseconds - s.HostAfterMilliseconds - 1);
        var high = samples.Min(s => s.BrowserUnixMilliseconds - s.HostBeforeMilliseconds + 1);
        if (low > high) return null;
        var best = samples.MinBy(s => s.HostAfterMilliseconds - s.HostBeforeMilliseconds)!;
        return new(best.BrowserUnixMilliseconds - (best.HostBeforeMilliseconds + best.HostAfterMilliseconds) / 2,
            (best.HostAfterMilliseconds - best.HostBeforeMilliseconds) / 2 + 1);
    }

    public static RecordingSynchronizationResult Analyze(RecordingClockCalibration? clock, double readerOriginUncertaintyMilliseconds,
        ImmutableArray<RecordedActionMarker> actions, ImmutableArray<SourceMarkerFrame> source, ImmutableArray<VideoMarkerFrame> video)
    {
        RecordingSynchronizationResult Unknown(string reason) => new(SynchronizationVerification.Unknown, reason,
            TargetMilliseconds, null, null, []);
        if (clock is null || !Finite(clock.BrowserMinusHostMilliseconds, clock.UncertaintyMilliseconds, readerOriginUncertaintyMilliseconds)
            || clock.UncertaintyMilliseconds < 0 || readerOriginUncertaintyMilliseconds < 0)
            return Unknown("RECORDING_CLOCK_UNVERIFIED");
        if (actions.IsDefault || actions.Length != 20 || !actions.Select(a => a.Objective).SequenceEqual(Enumerable.Range(1, 20))
            || actions.Any(a => !Finite(a.HostAttemptMilliseconds, a.HostCompletionMilliseconds, a.BrowserKeydownUnixMilliseconds)
                || a.HostAttemptMilliseconds < 0 || a.HostCompletionMilliseconds < a.HostAttemptMilliseconds || a.ReaderInputEventSequence < 1)
            || actions.Select(a => a.ReaderInputEventSequence).Distinct().Count() != 20)
            return Unknown("RECORDING_ACTION_EVIDENCE_INVALID");
        if (actions.Zip(actions.Skip(1)).Any(pair => pair.Second.HostAttemptMilliseconds < pair.First.HostCompletionMilliseconds))
            return Unknown("RECORDING_ACTION_EVIDENCE_INVALID");
        if (source.IsDefaultOrEmpty || source.Length > 500 || video.IsDefaultOrEmpty || video.Length > 250
            || source.Any(f => !double.IsFinite(f.PresentationUnixMilliseconds) || f.PresentationUnixMilliseconds <= 0 || !Valid(f.Marker))
            || video.Where((f, i) => f.Index != i || f.PresentationMilliseconds != i * 40 || !Valid(f.Marker)).Any())
            return Unknown("RECORDING_FRAME_EVIDENCE_INVALID");
        if (source.Zip(source.Skip(1)).Any(pair => pair.Second.PresentationUnixMilliseconds < pair.First.PresentationUnixMilliseconds)
            || video.Zip(video.Skip(1)).Any(pair => pair.Second.Marker!.Objective < pair.First.Marker!.Objective))
            return Unknown("RECORDING_FRAME_EVIDENCE_INVALID");
        var first = video[0];
        if (first.Marker!.Objective != 0) return Unknown("RECORDING_BASELINE_NOT_OBSERVED");
        var anchorSources = source.Where(s => s.Marker == first.Marker).ToArray();
        if (anchorSources.Length == 0) return Unknown("RECORDING_ANCHOR_NOT_OBSERVED");
        var low = anchorSources.Min(s => s.PresentationUnixMilliseconds);
        var high = anchorSources.Max(s => s.PresentationUnixMilliseconds);
        var anchor = (low + high) / 2;
        // Includes source ambiguity, one full CFR frame interval, host calibration,
        // reader-origin bracketing and a millisecond of browser timestamp resolution.
        var uncertainty = (high - low) / 2 + 40 + clock.UncertaintyMilliseconds + readerOriginUncertaintyMilliseconds + 1;
        var markers = ImmutableArray.CreateBuilder<MarkerSynchronization>();
        var reason = "RECORDING_SYNCHRONIZATION_VERIFIED";
        foreach (var action in actions)
        {
            var eventLow = action.HostAttemptMilliseconds + clock.BrowserMinusHostMilliseconds - clock.UncertaintyMilliseconds - readerOriginUncertaintyMilliseconds;
            var eventHigh = action.HostCompletionMilliseconds + clock.BrowserMinusHostMilliseconds + clock.UncertaintyMilliseconds + readerOriginUncertaintyMilliseconds;
            if (action.BrowserKeydownUnixMilliseconds < eventLow || action.BrowserKeydownUnixMilliseconds > eventHigh)
                reason = "RECORDING_CLOCK_INTERVAL_CONFLICT";
            if (!action.ReaderGoalVerified) reason = "RECORDING_READER_GOAL_UNVERIFIED";
            var frame = video.FirstOrDefault(f => f.Marker!.Objective == action.Objective);
            if (frame is null)
            {
                markers.Add(new(action.Objective, null, null, null));
                reason = "RECORDING_OBJECTIVE_FRAME_MISSING";
                continue;
            }
            var lag = anchor + frame.PresentationMilliseconds - action.HostAttemptMilliseconds - clock.BrowserMinusHostMilliseconds;
            var error = Math.Abs(lag) + uncertainty;
            markers.Add(new(action.Objective, frame.Index, lag, error));
            if (error > TargetMilliseconds) reason = "RECORDING_SYNCHRONIZATION_EXCEEDS_TARGET";
        }
        return new(reason == "RECORDING_SYNCHRONIZATION_VERIFIED" ? SynchronizationVerification.Verified : SynchronizationVerification.Unknown,
            reason, TargetMilliseconds, anchor, uncertainty, markers.ToImmutable());
    }

    private static bool Finite(params double[] values) => values.All(double.IsFinite);
    private static bool Valid(VisualMarker? marker) => marker is { Objective: >= 0 and <= 20, Tick: >= 0 and <= 255 };
}
