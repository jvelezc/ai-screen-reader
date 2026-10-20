using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public enum RecordingCueCoverage { Covered, Partial, OutsideClip, Unknown }
public sealed record TraversalRecordingCue(string EvidenceId, RecordingCueCoverage Coverage,
    double? StartSeconds, double? EndSeconds, int? FirstFrame, int? LastFrame);

/// <summary>Timing evidence attached to a run; never changes its destination or barrier assessment.</summary>
public sealed record TraversalRecordingAlignment(string RunId, string DocumentId, string VideoSha256,
    string ReasonCode, double? AnchorUnixMilliseconds, double? UncertaintyMilliseconds,
    ImmutableArray<TraversalRecordingCue> Cues)
{
    public bool Calibrated => ReasonCode == "TRAVERSAL_RECORDING_CALIBRATED";
}
