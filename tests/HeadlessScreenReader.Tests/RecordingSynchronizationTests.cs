using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class RecordingSynchronizationTests
{
    private const double Epoch = 1_800_000_000_000;

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
