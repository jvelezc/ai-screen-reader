using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>Maps capture and event intervals to original media. Pixels never verify an accessibility goal.</summary>
public static class TraversalRecording
{
    public static TraversalRecordingAlignment Align(TraversalReceipt receipt, RecordingClockCalibration? clock,
        double runnerOriginHostMilliseconds, string documentId, string videoSha256,
        ImmutableArray<SourceMarkerFrame> source, ImmutableArray<VideoMarkerFrame> video)
    {
        TraversalRecordingAlignment Unknown(string code) => new(receipt.RunId, documentId, videoSha256, code, null, null, []);
        static bool Interval(EvidenceInterval? interval) => interval is not null
            && double.IsFinite(interval.StartMilliseconds) && double.IsFinite(interval.EndMilliseconds)
            && interval.StartMilliseconds >= 0 && interval.EndMilliseconds >= interval.StartMilliseconds;
        if (clock is null || !double.IsFinite(clock.BrowserMinusHostMilliseconds)
            || !double.IsFinite(clock.UncertaintyMilliseconds) || clock.UncertaintyMilliseconds < 0
            || !double.IsFinite(runnerOriginHostMilliseconds) || runnerOriginHostMilliseconds < 0
            || receipt.Clock is not { TimestampFrequency: > 0 } runClock || !Interval(runClock.ReaderOrigin)
            || !double.IsFinite(receipt.DurationMilliseconds) || receipt.DurationMilliseconds < runClock.ReaderOrigin.EndMilliseconds)
            return Unknown("TRAVERSAL_RECORDING_CLOCK_UNVERIFIED");
        if (string.IsNullOrWhiteSpace(documentId) || receipt.Observations.IsDefaultOrEmpty
            || receipt.Observations.Any(o => o.Snapshot.DocumentId != documentId)
            || videoSha256.Length != 64 || videoSha256.Any(c => !char.IsAsciiHexDigit(c)))
            return Unknown("TRAVERSAL_RECORDING_IDENTITY_UNVERIFIED");
        if (source.IsDefaultOrEmpty || source.Length > 500 || video.IsDefaultOrEmpty || video.Length > 250
            || source.Any(f => !double.IsFinite(f.PresentationUnixMilliseconds) || f.PresentationUnixMilliseconds <= 0
                || f.Marker is not { Objective: 0, Tick: >= 0 and <= 255 })
            || source.Zip(source.Skip(1)).Any(p => p.Second.PresentationUnixMilliseconds < p.First.PresentationUnixMilliseconds)
            || video.Where((f, i) => f.Index != i || f.PresentationMilliseconds != i * 40
                || f.Marker is not { Objective: 0, Tick: >= 0 and <= 255 }).Any())
            return Unknown("TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID");
        var matches = source.Where(f => f.Marker == video[0].Marker).ToArray();
        if (matches.Length == 0) return Unknown("TRAVERSAL_RECORDING_ANCHOR_MISSING");
        var low = matches.Min(f => f.PresentationUnixMilliseconds);
        var high = matches.Max(f => f.PresentationUnixMilliseconds);
        var anchor = (low + high) / 2;
        var uncertainty = (high - low) / 2 + 40 + clock.UncertaintyMilliseconds + 1;
        if (!double.IsFinite(anchor) || !double.IsFinite(uncertainty) || uncertainty > RecordingSynchronization.TargetMilliseconds)
            return Unknown("TRAVERSAL_RECORDING_UNCERTAINTY_EXCEEDS_TARGET");
        var duration = video.Length * 40d;
        var cues = ImmutableArray.CreateBuilder<TraversalRecordingCue>();
        void Add(string id, EvidenceInterval? interval)
        {
            if (!Interval(interval) || interval!.EndMilliseconds > receipt.DurationMilliseconds)
            { cues.Add(new(id, RecordingCueCoverage.Unknown, null, null, null, null)); return; }
            var start = runnerOriginHostMilliseconds + interval.StartMilliseconds + clock.BrowserMinusHostMilliseconds - anchor - uncertainty;
            var end = runnerOriginHostMilliseconds + interval.EndMilliseconds + clock.BrowserMinusHostMilliseconds - anchor + uncertainty;
            if (!double.IsFinite(start) || !double.IsFinite(end))
            { cues.Add(new(id, RecordingCueCoverage.Unknown, null, null, null, null)); return; }
            if (end <= 0 || start >= duration)
            { cues.Add(new(id, RecordingCueCoverage.OutsideClip, null, null, null, null)); return; }
            var coverage = start < 0 || end > duration ? RecordingCueCoverage.Partial : RecordingCueCoverage.Covered;
            start = Math.Max(0, start); end = Math.Min(duration, end);
            cues.Add(new(id, coverage, start / 1000, end / 1000,
                Math.Clamp((int)Math.Floor(start / 40), 0, video.Length - 1),
                Math.Clamp((int)Math.Ceiling(end / 40) - 1, 0, video.Length - 1)));
        }
        foreach (var observation in receipt.Observations) Add(observation.Id, observation.CaptureWindow);
        foreach (var attempt in receipt.Attempts) Add(attempt.Id, new(attempt.StartedMilliseconds, attempt.FinishedMilliseconds));
        foreach (var item in receipt.Timeline)
            Add($"event-{item.Sequence}", !double.IsFinite(item.ElapsedMilliseconds) || item.ElapsedMilliseconds < 0 ? null
                : new(runClock.ReaderOrigin.StartMilliseconds + item.ElapsedMilliseconds,
                    runClock.ReaderOrigin.EndMilliseconds + item.ElapsedMilliseconds));
        if (cues.Select(c => c.EvidenceId).Distinct(StringComparer.Ordinal).Count() != cues.Count)
            return Unknown("TRAVERSAL_RECORDING_EVIDENCE_IDS_INVALID");
        return new(receipt.RunId, documentId, videoSha256, "TRAVERSAL_RECORDING_CALIBRATED", anchor, uncertainty, cues.ToImmutable());
    }
}
