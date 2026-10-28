using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>One real capture state and zero input edges. This is not a keyboard traversal or timing alignment.</summary>
public sealed record PassiveObservationState(string Id, string DocumentId, long CaptureEpoch, string ObservationSha256);
public sealed record PassiveObservationEdge(string Source, string Target, string Command);
/// <summary>Original media decode and acknowledged exclusion facts; no media-to-event synchronization claim.</summary>
public sealed record PassiveObservationRecording(string Schema, FixtureJob Job, string ObservationSha256,
    string VideoSha256, long VideoBytes, int DecodedVideoFrames, int ObservedSourceFrames,
    CaptureStatus Capture, ImmutableArray<PassiveObservationState> States, ImmutableArray<PassiveObservationEdge> Edges)
{
    public const string CurrentSchema = "hsr-passive-observation-recording-v1";
    public const int MaximumBundleBytes = 25 * 1024 * 1024;
    public const string VideoFile = "original.webm";
    public const string CaptureFile = "capture-state.json";
    public string Timing => "NotAlignedToSnapshot";
    public string SourceFrames => "AuxiliaryFramesNotRetained";
    public int DispatchedInputs => 0;
    public bool GoalVerified => false;

    public void Validate(FixtureObservation observation, string observationDigest, string videoDigest, int videoBytes)
    {
        if (Schema != CurrentSchema || Job != observation.Job || !Job.RecordObservation || Job.TraversalRoute != 0
            || ObservationSha256 != observationDigest || VideoSha256 != videoDigest || VideoBytes != videoBytes
            || videoBytes is < 4 or > MaximumBundleBytes || DecodedVideoFrames is < 1 or > 10000
            || ObservedSourceFrames is < 1 or > 2048 || Capture is not { Phase: CapturePhase.Closed, EventsTruncated: false }
            || Capture.Events.IsDefaultOrEmpty || !Capture.Events.Any(e => e.Phase == CapturePhase.Excluded)
            || States.IsDefault || States.Length != 1 || States[0] != new PassiveObservationState("capture-1",
                observation.Snapshot.DocumentId, observation.Snapshot.CaptureEpoch, observationDigest)
            || Edges.IsDefault || !Edges.IsEmpty)
            throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
    }
}
