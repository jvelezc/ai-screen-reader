using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class RecordingSynchronizationTests
{
    private const double Epoch = 1_800_000_000_000;

    [Fact]
    public void LaterSharedBaselineAnchorsFrameZeroWithoutFittingActionMarkers()
    {
        var input = Inputs(20);
        var source = ImmutableArray.Create(new SourceMarkerFrame("later-baseline.jpg", Epoch + 40, new(0, 1)));
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions, source, input.Video);
        Assert.Equal(SynchronizationVerification.Verified, result.Verification);
        Assert.Equal(Epoch, result.AnchorUnixMilliseconds); Assert.Equal(41, result.UncertaintyMilliseconds);
        Assert.Equal(20, result.Markers.Length);
        Assert.All(result.Markers, marker => Assert.Equal(61, marker.ConservativeAbsoluteErrorMilliseconds));
    }

    [Fact]
    public void ActionMarkerMatchesCannotReplaceAMissingSharedBaseline()
    {
        var input = Inputs(20);
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions,
            [new("action-only.jpg", Epoch + 440, new(1, 11))], input.Video);
        Assert.Equal(SynchronizationVerification.Unknown, result.Verification);
        Assert.Equal("RECORDING_ANCHOR_NOT_OBSERVED", result.ReasonCode);
        Assert.Null(result.AnchorUnixMilliseconds); Assert.Empty(result.Markers);
    }

    [Fact]
    public void EarliestSharedBaselinePreservesAmbiguityInsteadOfChoosingASharperLaterMatch()
    {
        var input = Inputs(20);
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions,
            [new("first.jpg", Epoch + 40, new(0, 1)), new("sharper.jpg", Epoch + 80, new(0, 2)),
                new("ambiguous.jpg", Epoch + 440, new(0, 1))], input.Video);
        Assert.Equal(SynchronizationVerification.Unknown, result.Verification);
        Assert.Equal(Epoch + 200, result.AnchorUnixMilliseconds); Assert.Equal(241, result.UncertaintyMilliseconds);
        Assert.Equal(250, result.TargetMilliseconds);
    }

    [Fact]
    public void FailedWindowsCiMetadataUsesItsIndependentLaterBaselineWithinTheUnchangedTarget()
    {
        var assembly = typeof(RecordingSynchronizationTests).Assembly;
        using var stream = assembly.GetManifestResourceStream("HeadlessScreenReader.Tests.Fixtures.recording-baseline-windows-ci-41534998348.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var source = root.GetProperty("Source").Deserialize<ImmutableArray<SourceMarkerFrame>>();
        var video = root.GetProperty("Video").Deserialize<ImmutableArray<VideoMarkerFrame>>();
        Assert.Equal(new VisualMarker(0, 58), video[0].Marker); Assert.Equal(new VisualMarker(0, 59), video[1].Marker);
        Assert.DoesNotContain(source, frame => frame.Marker == video[0].Marker);
        var result = RecordingSynchronization.Analyze(
            RecordingSynchronization.Calibrate(root.GetProperty("ClockSamples").Deserialize<ImmutableArray<ClockRoundTrip>>()),
            root.GetProperty("ReaderOriginUncertaintyMilliseconds").GetDouble(),
            root.GetProperty("Actions").Deserialize<ImmutableArray<RecordedActionMarker>>(), source, video);
        Assert.Equal(SynchronizationVerification.Verified, result.Verification);
        Assert.Equal(20, result.Markers.Length); Assert.Equal(250, result.TargetMilliseconds);
        Assert.All(result.Markers, marker => Assert.InRange(marker.ConservativeAbsoluteErrorMilliseconds!.Value, 0, 250));
    }

    [Theory]
    [InlineData(251)]
    [InlineData(282)]
    [InlineData(10000)]
    public void LongCompleteVideoRetainsTheLateObjectiveWithinTheExistingDecodedFrameContract(int frames)
    {
        var input = LongInputs(frames);
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions, input.Source, input.Video);
        Assert.Equal(SynchronizationVerification.Verified, result.Verification);
        Assert.Equal("RECORDING_SYNCHRONIZATION_VERIFIED", result.ReasonCode);
        Assert.Equal(20, result.Markers.Length);
        Assert.Equal(Epoch, result.AnchorUnixMilliseconds); Assert.Equal(41, result.UncertaintyMilliseconds);
        Assert.Equal(250, result.TargetMilliseconds);
        var late = result.Markers.Single(m => m.Objective == 20);
        Assert.Equal(frames - 1, late.VideoFrameIndex);
        Assert.Equal(20, late.MeasuredLagMilliseconds); Assert.Equal(61, late.ConservativeAbsoluteErrorMilliseconds);
        Assert.All(result.Markers, marker => Assert.NotNull(marker.VideoFrameIndex));
    }

    [Theory]
    [InlineData("index")]
    [InlineData("time")]
    [InlineData("tick")]
    [InlineData("objective")]
    public void InvalidSuffixBeyondLegacyVideoBoundaryCannotVerifySynchronization(string invalid)
    {
        var input = LongInputs(282);
        var last = input.Video[^1];
        var video = input.Video.SetItem(281, invalid switch
        {
            "index" => last with { Index = 282 },
            "time" => last with { PresentationMilliseconds = double.NaN },
            "tick" => last with { Marker = new(20, 256) },
            _ => last with { Marker = new(21, 31) }
        });
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions, input.Source, video);
        Assert.Equal(SynchronizationVerification.Unknown, result.Verification);
        Assert.Equal("RECORDING_FRAME_EVIDENCE_INVALID", result.ReasonCode);
        Assert.Empty(result.Markers); Assert.Null(result.AnchorUnixMilliseconds);
        Assert.Null(result.UncertaintyMilliseconds);
    }

    [Fact]
    public void DecodedVideoImmediatelyBeyondTheExistingContractCannotVerifySynchronization()
    {
        var input = LongInputs(10001);
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions, input.Source, input.Video);
        Assert.Equal(SynchronizationVerification.Unknown, result.Verification);
        Assert.Equal("RECORDING_FRAME_EVIDENCE_INVALID", result.ReasonCode);
        Assert.Empty(result.Markers); Assert.Null(result.AnchorUnixMilliseconds);
        Assert.Null(result.UncertaintyMilliseconds);
    }

    [Fact]
    public void ReversedSourceClockOverlappingActionsOrInvalidMarkerCode_RemainsUnknown()
    {
        var input = Inputs(20);
        var reversed = input.Source.Add(new("earlier.jpg", Epoch - 1, new(0, 0)));
        Assert.Equal("RECORDING_FRAME_EVIDENCE_INVALID", RecordingSynchronization.Analyze(new(Epoch, 0), 0,
            input.Actions, reversed, input.Video).ReasonCode);
        var overlapping = input.Actions.SetItem(1, input.Actions[1] with { HostAttemptMilliseconds = input.Actions[0].HostAttemptMilliseconds });
        Assert.Equal("RECORDING_ACTION_EVIDENCE_INVALID", RecordingSynchronization.Analyze(new(Epoch, 0), 0,
            overlapping, input.Source, input.Video).ReasonCode);
        var invalid = input.Video.SetItem(1, input.Video[1] with { Marker = new(0, 256) });
        Assert.Equal("RECORDING_FRAME_EVIDENCE_INVALID", RecordingSynchronization.Analyze(new(Epoch, 0), 0,
            input.Actions, input.Source, invalid).ReasonCode);
    }

    [Fact]
    public void Calibration_UsesTightestRoundTripAndRejectsContradictoryClockEvidence()
    {
        var clock = RecordingSynchronization.Calibrate([new(10, Epoch + 20, 30), new(40, Epoch + 41, 42)]);
        Assert.Equal(new RecordingClockCalibration(Epoch, 2), clock);
        Assert.Null(RecordingSynchronization.Calibrate([new(10, Epoch, 12), new(20, Epoch + 50, 22)]));
        Assert.Null(RecordingSynchronization.Calibrate([]));
        Assert.Null(RecordingSynchronization.Calibrate([new(2, Epoch, 1)]));
        Assert.Null(RecordingSynchronization.Calibrate([new(0, double.NaN, 1)]));
    }

    [Theory]
    [InlineData(209, SynchronizationVerification.Verified)]
    [InlineData(209.001, SynchronizationVerification.Unknown)]
    public void ConservativeBound_AtTargetPassesAndImmediatelyAboveRemainsUnknown(double lag, SynchronizationVerification expected)
    {
        var input = Inputs(lag);
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions, input.Source, input.Video);
        Assert.Equal(expected, result.Verification);
        Assert.Equal(20, result.Markers.Length);
        Assert.Equal(41, result.UncertaintyMilliseconds);
        Assert.Equal(Epoch, result.AnchorUnixMilliseconds);
        Assert.All(result.Markers, m => Assert.InRange(m.MeasuredLagMilliseconds!.Value, lag - 0.001, lag + 0.001));
    }

    [Fact]
    public void MissingFrameOrUnverifiedGoal_DoesNotBecomeAccessibilityBarrierOrInventedFrame()
    {
        var input = Inputs(20);
        var missing = input.Video.Select(f => f.Marker!.Objective == 10 ? f with { Marker = new(9, 40) } : f).ToImmutableArray();
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions, input.Source, missing);
        Assert.Equal(SynchronizationVerification.Unknown, result.Verification);
        Assert.Equal("RECORDING_OBJECTIVE_FRAME_MISSING", result.ReasonCode);
        Assert.Null(result.Markers.Single(m => m.Objective == 10).VideoFrameIndex);
        var actions = input.Actions.SetItem(0, input.Actions[0] with { ReaderGoalVerified = false });
        Assert.Equal("RECORDING_READER_GOAL_UNVERIFIED", RecordingSynchronization.Analyze(new(Epoch, 0), 0,
            actions, input.Source, input.Video).ReasonCode);
    }

    [Fact]
    public void BaselineIsIndependent_AndWideDuplicateAnchorRetainsItsUncertainty()
    {
        var input = Inputs(20);
        var source = input.Source.Add(new("duplicate.jpg", Epoch + 400, new(0, 0)));
        var result = RecordingSynchronization.Analyze(new(Epoch, 0), 0, input.Actions, source, input.Video);
        Assert.Equal(SynchronizationVerification.Unknown, result.Verification);
        Assert.Equal(Epoch + 200, result.AnchorUnixMilliseconds);
        Assert.Equal(241, result.UncertaintyMilliseconds);
        Assert.Equal("RECORDING_ANCHOR_NOT_OBSERVED", RecordingSynchronization.Analyze(new(Epoch, 0), 0,
            input.Actions, [new("other.jpg", Epoch, new(0, 99))], input.Video).ReasonCode);
    }

    [Fact]
    public void InvalidClockActionsFramesOrEventInterval_CannotProduceVerifiedTiming()
    {
        var input = Inputs(20);
        Assert.Equal("RECORDING_CLOCK_UNVERIFIED", RecordingSynchronization.Analyze(null, 0, input.Actions, input.Source, input.Video).ReasonCode);
        Assert.Equal("RECORDING_ACTION_EVIDENCE_INVALID", RecordingSynchronization.Analyze(new(Epoch, 0), 0,
            input.Actions.RemoveAt(0), input.Source, input.Video).ReasonCode);
        Assert.Equal("RECORDING_FRAME_EVIDENCE_INVALID", RecordingSynchronization.Analyze(new(Epoch, 0), 0,
            input.Actions, input.Source, input.Video.SetItem(1, input.Video[1] with { PresentationMilliseconds = double.NaN })).ReasonCode);
        var conflict = input.Actions.SetItem(0, input.Actions[0] with { BrowserKeydownUnixMilliseconds = Epoch + 100_000 });
        Assert.Equal("RECORDING_CLOCK_INTERVAL_CONFLICT", RecordingSynchronization.Analyze(new(Epoch, 0), 0,
            conflict, input.Source, input.Video).ReasonCode);
    }

    private static (ImmutableArray<RecordedActionMarker> Actions, ImmutableArray<SourceMarkerFrame> Source, ImmutableArray<VideoMarkerFrame> Video) LongInputs(int frames)
    {
        var input = Inputs(20);
        var at = (frames - 1) * 40 - 20;
        var actions = input.Actions.SetItem(19, input.Actions[19] with
        {
            HostAttemptMilliseconds = at,
            HostCompletionMilliseconds = at + 10,
            BrowserKeydownUnixMilliseconds = Epoch + at + 5
        });
        var video = input.Video.Take(30).Concat(Enumerable.Range(30, frames - 30).Select(i =>
            new VideoMarkerFrame(i, i * 40, new(i == frames - 1 ? 20 : 19, 31)))).ToImmutableArray();
        return (actions, input.Source, video);
    }

    private static (ImmutableArray<RecordedActionMarker> Actions, ImmutableArray<SourceMarkerFrame> Source, ImmutableArray<VideoMarkerFrame> Video) Inputs(double lag)
    {
        var actions = Enumerable.Range(1, 20).Select(i =>
            new RecordedActionMarker(i, i, (i + 10) * 40 - lag, (i + 10) * 40 - lag + 10,
                Epoch + (i + 10) * 40 - lag + 5, true)).ToImmutableArray();
        var video = Enumerable.Range(0, 31).Select(i =>
            new VideoMarkerFrame(i, i * 40, new(i <= 10 ? 0 : i - 10, i))).ToImmutableArray();
        return (actions, [new("anchor.jpg", Epoch, new(0, 0))], video);
    }
}
