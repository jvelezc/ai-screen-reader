using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalRecordingTests
{
    private const double Epoch = 1_800_000_000_000;
    private static readonly string Hash = new('a', 64);
    private static readonly ImmutableArray<SourceMarkerFrame> Source = [new("source.jpg", Epoch, new(0, 7))];
    private static ImmutableArray<VideoMarkerFrame> Video(int count = 30) =>
        Enumerable.Range(0, count).Select(i => new VideoMarkerFrame(i, i * 40, new(0, i == 0 ? 7 : 8))).ToImmutableArray();

    [Fact]
    public async Task PresentationIndexRetainsReversedRawDeliveriesAndCalibratesWithoutChangingTimestampsOrOutcome()
    {
        var receipt = await Receipt();
        ImmutableArray<SourceMarkerFrame> raw = [new("source-0001.jpg", Epoch + 4.113, new(0, 7)),
            new("source-0002.jpg", Epoch, new(0, 7)), new("source-0003.jpg", Epoch + 40, new(0, 8))];
        var media = new HeadlessScreenReader.Browser.Playwright.RecordingProbeMedia(raw, Video(), [], []);
        Assert.Equal("TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID", TraversalRecording.Align(receipt,
            new(Epoch, 0), 100, "doc", Hash, raw, media.Video).ReasonCode);
        Assert.Equal(new[] { 1, 0, 2 }, media.SourcePresentationOrder.ToArray());
        var aligned = TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash, media.SourceInPresentationOrder, media.Video);
        Assert.True(aligned.Calibrated); Assert.InRange(aligned.UncertaintyMilliseconds!.Value, 43, 44);
        Assert.NotEmpty(aligned.Cues); Assert.All(aligned.Cues, cue => Assert.Equal(RecordingCueCoverage.Covered, cue.Coverage));
        Assert.Equal(raw, media.Source); Assert.Equal(Epoch + 4.113, media.Source[0].PresentationUnixMilliseconds);
        Assert.Equal(Epoch, media.Source[1].PresentationUnixMilliseconds);
        Assert.Equal(raw.OrderBy(frame => frame.File), media.SourceInPresentationOrder.OrderBy(frame => frame.File));
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
    }

    [Fact]
    public async Task CaptureWindows_BracketActualAcquisitionOnTheRunnerClock()
    {
        var time = new FakeTimeProvider();
        var start = time.GetTimestamp();
        var browser = new TraversalRunnerTests.ControlledBrowser
        {
            AfterCapture = () => time.Advance(TimeSpan.FromMilliseconds(5)),
            AfterKey = () => time.Advance(TimeSpan.FromMilliseconds(7))
        };
        var receipt = await new TraversalRunner(time).RunAsync(browser,
            TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)), waitOptions: new() { MaxPolls = 1 });
        Assert.Equal(start, receipt.Clock!.OriginTimestamp);
        Assert.Equal(time.TimestampFrequency, receipt.Clock.TimestampFrequency);
        Assert.Equal(new EvidenceInterval(0, 0), receipt.Clock.ReaderOrigin);
        Assert.Equal(new EvidenceInterval(0, 5), receipt.Observations[0].CaptureWindow);
        Assert.All(receipt.Observations, o =>
        {
            Assert.Equal(5, o.CaptureWindow!.EndMilliseconds - o.CaptureWindow.StartMilliseconds);
            Assert.True(o.ElapsedMilliseconds >= o.CaptureWindow.EndMilliseconds);
        });
        Assert.Equal(1, receipt.CompletedKeystrokes);
        Assert.True(receipt.DurationMilliseconds >= 22);
    }

    [Fact]
    public async Task IndependentAnchor_MapsCaptureAttemptAndReaderOriginIntervalsWithoutChangingGoal()
    {
        var receipt = await Receipt();
        var aligned = Align(receipt);
        Assert.True(aligned.Calibrated);
        Assert.Equal(receipt.RunId, aligned.RunId);
        Assert.Equal("doc", aligned.DocumentId);
        Assert.Equal(Hash, aligned.VideoSha256);
        Assert.Equal(Epoch, aligned.AnchorUnixMilliseconds);
        Assert.Equal(41, aligned.UncertaintyMilliseconds);
        var observation = aligned.Cues.Single(c => c.EvidenceId == "o1");
        Assert.Equal(0.159, observation.StartSeconds);
        Assert.Equal(0.261, observation.EndSeconds);
        Assert.Equal(3, observation.FirstFrame);
        Assert.Equal(6, observation.LastFrame);
        var attempt = aligned.Cues.Single(c => c.EvidenceId == "a1");
        Assert.Equal(0.259, attempt.StartSeconds);
        Assert.Equal(0.441, attempt.EndSeconds);
        var item = aligned.Cues.Single(c => c.EvidenceId == "event-1");
        Assert.Equal(0.469, item.StartSeconds);
        Assert.Equal(0.553, item.EndSeconds);
        Assert.All(aligned.Cues, c => Assert.Equal(RecordingCueCoverage.Covered, c.Coverage));
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(29)]
    public async Task MissingLeadingSourceFrames_UseObservedOverlapAndItsVideoPosition(int firstSharedFrame)
    {
        var receipt = await Receipt();
        // Tick values identify pixels; their differences are not a clock or frame count.
        var video = Enumerable.Range(0, 30).Select(i => new VideoMarkerFrame(i, i * 40,
            new(0, i == 0 ? 7 : i + 9))).ToImmutableArray();
        var frame = video[firstSharedFrame];
        var source = ImmutableArray.Create(new SourceMarkerFrame("observed.jpg", Epoch + frame.PresentationMilliseconds, frame.Marker));
        var result = TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash, source, video);
        Assert.True(result.Calibrated, result.ReasonCode);
        Assert.Equal(Epoch, result.AnchorUnixMilliseconds);
        Assert.Equal(41, result.UncertaintyMilliseconds);
        var cue = result.Cues.Single(c => c.EvidenceId == "o1");
        Assert.Equal(0.159, cue.StartSeconds); Assert.Equal(0.261, cue.EndSeconds);
        Assert.Equal(3, cue.FirstFrame); Assert.Equal(6, cue.LastFrame);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
    }

    [Fact]
    public async Task ConflictingLaterOverlap_CannotHideBehindAnAccurateFirstMarker()
    {
        var receipt = await Receipt();
        var video = Enumerable.Range(0, 30).Select(i => new VideoMarkerFrame(i, i * 40, new(0, i + 7))).ToImmutableArray();
        var result = TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash,
            Source.Add(new("conflicting.jpg", Epoch + 1000, new(0, 8))), video);
        Assert.False(result.Calibrated);
        Assert.Equal("TRAVERSAL_RECORDING_UNCERTAINTY_EXCEEDS_TARGET", result.ReasonCode);
        Assert.Empty(result.Cues); Assert.Null(result.AnchorUnixMilliseconds);
    }

    [Fact]
    public async Task HeldLaterMarker_UsesItsVisibleTransitionRatherThanEncoderPadding()
    {
        var receipt = await Receipt();
        var result = TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash,
            [new("observed.jpg", Epoch + 40, new(0, 8))], Video());
        Assert.True(result.Calibrated, result.ReasonCode);
        Assert.Equal(Epoch, result.AnchorUnixMilliseconds);
        Assert.Equal(41, result.UncertaintyMilliseconds);
    }

    [Fact]
    public async Task ReappearingMarker_RetainsEveryPossibleTransitionInItsUncertainty()
    {
        var receipt = await Receipt();
        var result = TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash,
            Source, Video().SetItem(29, new(29, 1160, new(0, 7))));
        Assert.False(result.Calibrated);
        Assert.Equal("TRAVERSAL_RECORDING_UNCERTAINTY_EXCEEDS_TARGET", result.ReasonCode);
        Assert.Empty(result.Cues); Assert.Null(result.UncertaintyMilliseconds);
    }

    [Theory]
    [InlineData(41, 50, RecordingCueCoverage.Covered)]
    [InlineData(40.999, 50, RecordingCueCoverage.Partial)]
    [InlineData(350, 359, RecordingCueCoverage.Covered)]
    [InlineData(350, 359.001, RecordingCueCoverage.Partial)]
    [InlineData(0, 0, RecordingCueCoverage.Partial)]
    [InlineData(1000, 1000, RecordingCueCoverage.OutsideClip)]
    [InlineData(1000, 1000.001, RecordingCueCoverage.Unknown)]
    [InlineData(-1, 5, RecordingCueCoverage.Unknown)]
    [InlineData(50, 49, RecordingCueCoverage.Unknown)]
    public async Task ClipBoundaries_PreservePartialOutsideAndUnknownInsteadOfInventingFrames(double start, double end, RecordingCueCoverage expected)
    {
        var receipt = await Receipt();
        receipt = receipt with { Observations = [receipt.Observations[0] with { CaptureWindow = new(start, end) }] };
        var result = TraversalRecording.Align(receipt, new(Epoch, 0), 0, "doc", Hash, Source, Video(10));
        var cue = result.Cues.Single(c => c.EvidenceId == "o1");
        Assert.Equal(expected, cue.Coverage);
        if (expected is RecordingCueCoverage.Unknown or RecordingCueCoverage.OutsideClip)
        { Assert.Null(cue.StartSeconds); Assert.Null(cue.EndSeconds); Assert.Null(cue.FirstFrame); Assert.Null(cue.LastFrame); }
        else
        { Assert.InRange(cue.StartSeconds!.Value, 0, 0.4); Assert.InRange(cue.EndSeconds!.Value, cue.StartSeconds.Value, 0.4); }
    }

    [Fact]
    public async Task MissingCaptureWindow_DoesNotReconstructAnAtomicFrameFromRetentionTime()
    {
        var receipt = await Receipt();
        var result = Align(receipt with { Observations = [receipt.Observations[0] with { CaptureWindow = null }] });
        Assert.True(result.Calibrated);
        Assert.Equal(RecordingCueCoverage.Unknown, result.Cues.Single(c => c.EvidenceId == "o1").Coverage);
        Assert.Null(result.Cues.Single(c => c.EvidenceId == "o1").FirstFrame);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task InvalidEventElapsedTime_CannotAcquireAFrameThroughReaderOriginOffset(double elapsed)
    {
        var receipt = await Receipt();
        var result = Align(receipt with { Timeline = [receipt.Timeline[0] with { ElapsedMilliseconds = elapsed }] });
        var cue = result.Cues.Single(c => c.EvidenceId == "event-1");
        Assert.Equal(RecordingCueCoverage.Unknown, cue.Coverage);
        Assert.Null(cue.StartSeconds);
        Assert.Null(cue.FirstFrame);
    }

    [Fact]
    public async Task MissingAnchorWrongDocumentHashOrClock_ProducesNoVideoCues()
    {
        var receipt = await Receipt();
        var cases = new[]
        {
            TraversalRecording.Align(receipt, null, 100, "doc", Hash, Source, Video()),
            TraversalRecording.Align(receipt with { Clock = null }, new(Epoch, 0), 100, "doc", Hash, Source, Video()),
            TraversalRecording.Align(receipt, new(Epoch, double.NaN), 100, "doc", Hash, Source, Video()),
            TraversalRecording.Align(receipt, new(Epoch, 0), double.PositiveInfinity, "doc", Hash, Source, Video()),
            TraversalRecording.Align(receipt, new(Epoch, 0), 100, "foreign", Hash, Source, Video()),
            TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", "bad-hash", Source, Video()),
            TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash, [new("other.jpg", Epoch, new(0, 99))], Video())
        };
        Assert.All(cases, c => { Assert.False(c.Calibrated); Assert.Empty(c.Cues); Assert.Null(c.AnchorUnixMilliseconds); });
        Assert.Equal("TRAVERSAL_RECORDING_ANCHOR_MISSING", cases[^1].ReasonCode);
    }

    [Theory]
    [InlineData(418, true)]
    [InlineData(418.001, false)]
    public async Task DuplicateAnchorMatches_RetainAmbiguityAtAndAboveTimingBudget(double separation, bool calibrated)
    {
        var receipt = await Receipt();
        var result = TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash,
            Source.Add(new("duplicate.jpg", Epoch + separation, new(0, 7))), Video());
        Assert.Equal(calibrated, result.Calibrated);
        if (calibrated) { Assert.Equal(250, result.UncertaintyMilliseconds); Assert.Equal(Epoch + 209, result.AnchorUnixMilliseconds); }
        else { Assert.Equal("TRAVERSAL_RECORDING_UNCERTAINTY_EXCEEDS_TARGET", result.ReasonCode); Assert.Empty(result.Cues); }
    }

    [Fact]
    public async Task ReversedMissingNonfiniteOrOversizeFrames_AndDuplicateEvidenceIdsFailClosed()
    {
        var receipt = await Receipt();
        ImmutableArray<SourceMarkerFrame>[] sources = [[], Source.Add(new("earlier", Epoch - 1, new(0, 8))),
            [new("invalid", double.NaN, new(0, 7))], [new("invalid", Epoch, null)],
            [.. Enumerable.Repeat(Source[0], 501)]];
        foreach (var source in sources)
            Assert.Equal("TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID",
                TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash, source, Video()).ReasonCode);
        ImmutableArray<VideoMarkerFrame>[] videos = [[], Video(251), Video().SetItem(1, new(3, 40, new(0, 8))),
            Video().SetItem(1, new(1, 41, new(0, 8))), Video().SetItem(1, new(1, 40, new(0, 256)))];
        foreach (var video in videos)
            Assert.Equal("TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID",
                TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash, Source, video).ReasonCode);
        Assert.Equal("TRAVERSAL_RECORDING_EVIDENCE_IDS_INVALID",
            Align(receipt with { Observations = [receipt.Observations[0], receipt.Observations[0]] }).ReasonCode);
    }

    private static TraversalRecordingAlignment Align(TraversalReceipt receipt) =>
        TraversalRecording.Align(receipt, new(Epoch, 0), 100, "doc", Hash, Source, Video());

    private static async Task<TraversalReceipt> Receipt()
    {
        var receipt = await TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.ReadCurrent)));
        return receipt with
        {
            Clock = new(1000, 1000, new(10, 12)),
            DurationMilliseconds = 1000,
            Observations = [.. receipt.Observations.Select(o => o with { CaptureWindow = new(100, 120) })],
            Attempts = [receipt.Attempts[0] with { StartedMilliseconds = 200, FinishedMilliseconds = 300 }],
            Timeline = [new(1, 1, 400, ReaderEventKind.Observation, ReaderCommand.ReadCurrent, "doc", null, "Retained")]
        };
    }
}
