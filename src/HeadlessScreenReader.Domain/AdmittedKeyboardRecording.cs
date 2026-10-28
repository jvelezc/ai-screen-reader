using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public sealed record RecordedSourceFile(string Path, long Bytes, string Sha256);

/// <summary>One fixed authored keyboard route. Its input and goal facts never come from the zero-input baseline.</summary>
public sealed record AdmittedKeyboardRecording(string Schema, FixtureJob Job, string BaselineSha256,
    string VideoSha256, long VideoBytes, int DecodedVideoFrames, string SourceFramesSha256,
    ImmutableArray<RecordedSourceFile> SourceFiles, TraversalReceipt Receipt, TraversalGraph Graph,
    TraversalRecordingAlignment Alignment, CaptureStatus Capture, ImmutableArray<ClockRoundTrip> ClockSamples,
    double RunnerOriginHostMilliseconds, ImmutableArray<SourceMarkerFrame> SourceFrames, ImmutableArray<VideoMarkerFrame> VideoFrames,
    string CollectorSha256, string CollectorVersion, string Stage, string? ErrorCode)
{
    public const string CurrentSchema = "hsr-admitted-keyboard-recording-v1";
    public const string RecordingFile = "traversal.json";
    public const string FramesFile = "source-frames.zip";
    public string Scope => "ControlledSyntheticAuthoredKeyboardRoute";
    public string Assistance => "None";
    public string NvdaComparison => "NotPerformedForThisRecording";
    public string TimingSourceClock => "ChromiumCdpJsonDouble";
    public string FixtureSha256 => FixtureObservation.RecordingContentSha256;
    public ImmutableArray<int> SourcePresentationOrder { get; init; } = [];
    public RecordedCollectorEnvironment? Environment { get; init; }

    public void Validate(FixtureObservation baseline, string baselineDigest, string videoDigest, long videoBytes, string framesDigest)
    {
        void Require(bool value) { if (!value) throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_UNCONFIRMED"); }
        Require(Schema == CurrentSchema && Job == baseline.Job && Job.RecordObservation && Job.TraversalRoute is >= 1 and <= 4);
        Require(Stage == "Complete" && ErrorCode is null);
        Require(Environment is null || new[] { Environment.Browser, Environment.Playwright, Environment.OperatingSystem, Environment.Viewport }
            .All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 500 && !value.Any(char.IsControl)));
        Require(BaselineSha256 == baselineDigest && VideoSha256 == videoDigest && VideoBytes == videoBytes
            && videoBytes is >= 4 and <= PassiveObservationRecording.MaximumBundleBytes && SourceFramesSha256 == framesDigest
            && DecodedVideoFrames is >= 1 and <= 10000);
        Require(Capture is { Phase: CapturePhase.Closed, EventsTruncated: false } && !Capture.Events.IsDefaultOrEmpty
            && Capture.Events.Any(e => e.Phase == CapturePhase.Excluded));
        Require(Receipt is not null && Receipt.Schema == "hsr-traversal-v1" && Receipt.Profile == FixtureObservation.Profile
            && Receipt.HasRecording && Receipt.Policy == TraversalPolicy.Allowed && Receipt.Clock is not null
            && !Receipt.Observations.IsDefaultOrEmpty && Receipt.Observations.Length <= 32
            && !Receipt.Attempts.IsDefault && Receipt.Attempts.Length <= 30 && !Receipt.Timeline.IsDefault
            && Receipt.Timeline.Length <= 4096 && Receipt.UnexecutedInstructions >= 0);
        Require(Receipt!.Observations[0].Snapshot == baseline.Snapshot || Receipt.Observations[0].Snapshot.DocumentId == baseline.Snapshot.DocumentId);
        var plan = AuthoredKeyboardRoutes.Plans[Job.TraversalRoute - 1];
        Require(Receipt.Plan.Name == plan.Name && Receipt.Plan.Precondition == plan.Precondition && Receipt.Plan.Destination == plan.Destination
            && Receipt.Plan.Instructions.SequenceEqual(plan.Instructions));
        var observations = Receipt.Observations.ToDictionary(o => o.Id, StringComparer.Ordinal);
        Require(observations.Count == Receipt.Observations.Length && Receipt.Observations.All(o => o.Snapshot is not null
            && o.Snapshot.DocumentId == baseline.Snapshot.DocumentId && o.Snapshot.CaptureEpoch == baseline.Snapshot.CaptureEpoch
            && !o.Snapshot.Nodes.IsDefaultOrEmpty && o.Snapshot.Nodes.Length <= 2048
            && !o.Snapshot.Nodes.Any(n => n.Role is "textbox" or "code")));
        Require(Graph is not null && Graph.Version == "execution-trace-v2" && !Graph.Nodes.IsDefault && !Graph.Edges.IsDefault
            && Graph.Nodes.Length == observations.Count && Graph.Nodes.Select(n => n.ObservationId).Distinct(StringComparer.Ordinal).Count() == observations.Count
            && Graph.Nodes.All(n => observations.TryGetValue(n.ObservationId, out var o) && n.DocumentId == o.Snapshot.DocumentId && n.Fingerprint == o.Fingerprint));
        Require(Receipt.Attempts.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() == Receipt.Attempts.Length
            && Receipt.Timeline.Select(e => e.Sequence).Distinct().Count() == Receipt.Timeline.Length);
        Require(Graph!.Edges.Length == Receipt.Attempts.Count(a => a.ResultObservationId is not null)
            && Graph.Edges.Select(e => e.AttemptId).Distinct(StringComparer.Ordinal).Count() == Graph.Edges.Length
            && !Graph.UnresolvedAttempts.IsDefault
            && Graph.UnresolvedAttempts.SequenceEqual(Receipt.Attempts.Where(a => a.ResultObservationId is null)));
        foreach (var edge in Graph.Edges)
        {
            var attempt = Receipt.Attempts.SingleOrDefault(a => a.Id == edge.AttemptId);
            Require(attempt is not null && observations.ContainsKey(edge.SourceObservationId) && observations.ContainsKey(edge.ResultObservationId)
                && attempt.SourceObservationId == edge.SourceObservationId && attempt.ResultObservationId == edge.ResultObservationId
                && edge.Command == attempt.Instruction.Command && edge.CompletedKeystrokes == (attempt.Step?.BrowserKeystrokes ?? 0)
                && edge.Outcome == attempt.Step?.Outcome && edge.Verification == (attempt.Step?.Verification ?? VerificationStatus.NotRequested));
            Require(!edge.KeyboardInputs.IsDefault && edge.KeyboardInputs.Length <= 16
                && edge.KeyboardInputs.Count(k => k.CompletionEventSequence is not null) == edge.CompletedKeystrokes
                && edge.KeyboardInputs.Select(k => k.AttemptEventSequence).Distinct().Count() == edge.KeyboardInputs.Length);
            foreach (var key in edge.KeyboardInputs)
            {
                Require(key.Key is "Tab" or "Enter" && key.AttemptEventSequence is { } sequence
                    && attempt!.Events.Any(e => e.Sequence == sequence && e.Kind == ReaderEventKind.InputAttempted && e.Detail == key.Key)
                    && Receipt.Timeline.Any(e => e.Sequence == sequence && e.Kind == ReaderEventKind.InputAttempted && e.Detail == key.Key));
                Require(key.CompletionEventSequence is null || attempt!.Events.Any(e => e.Sequence == key.CompletionEventSequence && e.Kind == ReaderEventKind.InputCompleted && e.Detail == key.Key)
                    && Receipt.Timeline.Any(e => e.Sequence == key.CompletionEventSequence && e.Kind == ReaderEventKind.InputCompleted && e.Detail == key.Key));
            }
        }
        Require(Alignment is not null && Alignment.RunId == Receipt.RunId && Alignment.DocumentId == baseline.Snapshot.DocumentId
            && Alignment.VideoSha256 == videoDigest && !Alignment.Cues.IsDefault && Alignment.Cues.Length <= 8192);
        Require(!ClockSamples.IsDefault && ClockSamples.Length is >= 1 and <= 32 && double.IsFinite(RunnerOriginHostMilliseconds)
            && RunnerOriginHostMilliseconds >= 0);
        Require(!SourceFiles.IsDefaultOrEmpty && SourceFiles.Length <= 2048 && SourceFiles.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() == SourceFiles.Length
            && SourceFiles.All(f => f.Path.Length is > 0 and <= 100 && !f.Path.Contains('/') && !f.Path.Contains('\\')
                && f.Path.EndsWith(".jpg", StringComparison.Ordinal) && f.Bytes is > 0 and <= 2 * 1024 * 1024
                && f.Sha256.Length == 64 && f.Sha256.All(char.IsAsciiHexDigitLower)));
        Require(!SourceFrames.IsDefault && SourceFrames.Length == SourceFiles.Length
            && SourceFrames.Select(frame => frame.File).Distinct(StringComparer.Ordinal).Count() == SourceFiles.Length
            && SourceFrames.All(f => SourceFiles.Any(s => s.Path == f.File)
            && double.IsFinite(f.PresentationUnixMilliseconds)) && !VideoFrames.IsDefault && VideoFrames.Length == DecodedVideoFrames
            && VideoFrames.All(f => f.Index >= 0 && double.IsFinite(f.PresentationMilliseconds) && f.PresentationMilliseconds >= 0)
            && VideoFrames.Select(f => f.Index).Distinct().Count() == VideoFrames.Length);
        Require(!SourcePresentationOrder.IsDefault && SourcePresentationOrder.Length == SourceFrames.Length
            && SourcePresentationOrder.Distinct().Count() == SourceFrames.Length
            && SourcePresentationOrder.All(index => index >= 0 && index < SourceFrames.Length));
        Require(!SourcePresentationOrder.Zip(SourcePresentationOrder.Skip(1)).Any(pair =>
            SourceFrames[pair.Second].PresentationUnixMilliseconds < SourceFrames[pair.First].PresentationUnixMilliseconds));
        Require(CollectorSha256 is { Length: 64 } && CollectorSha256.All(char.IsAsciiHexDigitLower) && CollectorVersion is { Length: > 0 and <= 120 });
    }
}
