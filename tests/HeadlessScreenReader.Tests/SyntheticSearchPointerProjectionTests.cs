using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchPointerProjectionTests
{
    private static readonly AccessibilitySnapshot Before = new("original-document", [], []) { CaptureEpoch = 1 };
    private static readonly AccessibilitySnapshot Focused = Before with { Limitations = ["focus observation"] };
    private static readonly AccessibilitySnapshot Entered = Before with { Limitations = ["entry observation"] };
    private static readonly SearchFocusReceipt Focus = new(DateTimeOffset.UnixEpoch, Before, Focused, true, true, true, null);
    private static ReaderStep Step(int keys) => new(2, ReaderCommand.EnterSearchText, ReaderMode.Focus, ReaderOutcome.Observed, "", null, null, keys);
    private static ReaderEvent Event(long sequence, ReaderEventKind kind, string key) => new(sequence, 2, sequence, kind,
        ReaderCommand.EnterSearchText, Before.DocumentId, null, key);
    private static SearchPointerPreparation Preparation(ImmutableArray<ReaderEvent> timeline, int keys = 2) =>
        new(Focus, DateTimeOffset.UnixEpoch, Step(keys), timeline, Entered, null);

    [Fact]
    public void MissingPointerFactsDoNotInventNodesOrActions()
    {
        var graph = SyntheticSearchPointerProjection.Build(null, null);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Equal("hsr-synthetic-search-pointer-graph-v1", graph.Schema);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void IncompleteMouseFocusPreservesActualDispatchAndMissingResult(bool attempted, bool completed, bool consistent)
    {
        var preparation = new SearchPointerPreparation(Focus with { After = null, InputAttempted = attempted, InputCompleted = completed }, null, null, [], null, null);
        var graph = SyntheticSearchPointerProjection.Build(preparation, null);
        Assert.Single(graph.Nodes);
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(attempted ? 1 : 0, edge.AttemptedNativeCalls);
        Assert.Equal(completed ? 1 : 0, edge.CompletedNativeCalls);
        Assert.Equal(consistent, edge.InputEvidenceComplete);
        Assert.True(edge.InputUnconfirmed);
        Assert.Null(edge.Target);
    }

    [Fact]
    public void RepeatedCharactersPairWithTheirOwnLaterCompletion()
    {
        var graph = SyntheticSearchPointerProjection.Build(Preparation([
            Event(10, ReaderEventKind.InputAttempted, "s"), Event(11, ReaderEventKind.InputCompleted, "s"),
            Event(20, ReaderEventKind.InputAttempted, "s"), Event(21, ReaderEventKind.InputCompleted, "s")]), null);
        var edge = graph.Edges[1];
        Assert.True(edge.InputEvidenceComplete);
        Assert.False(edge.InputUnconfirmed);
        Assert.Equal(new long?[] { 10, 20 }, edge.KeyboardInputs.Select(k => k.AttemptEventSequence));
        Assert.Equal(new long?[] { 11, 21 }, edge.KeyboardInputs.Select(k => k.CompletionEventSequence));
        Assert.Equal(2, edge.AttemptedNativeCalls);
        Assert.Equal(2, edge.CompletedNativeCalls);
        Assert.Equal("p1", edge.Source);
        Assert.Equal("p2", edge.Target);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("early")]
    [InlineData("default")]
    public void MissingOrInconsistentTextEventsCannotBecomeCompleteInput(string mutation)
    {
        ImmutableArray<ReaderEvent> timeline = [Event(10, ReaderEventKind.InputAttempted, "s"), Event(11, ReaderEventKind.InputCompleted, "s"),
            Event(20, ReaderEventKind.InputAttempted, "s"), Event(21, ReaderEventKind.InputCompleted, "s")];
        timeline = mutation switch
        {
            "missing" => timeline.RemoveAt(3),
            "duplicate" => timeline.Add(Event(22, ReaderEventKind.InputCompleted, "s")),
            "early" => timeline.SetItem(3, Event(19, ReaderEventKind.InputCompleted, "s")),
            _ => default
        };
        var graph = SyntheticSearchPointerProjection.Build(Preparation(timeline), null);
        var edge = graph.Edges[1];
        Assert.False(edge.InputEvidenceComplete);
        Assert.True(edge.InputUnconfirmed);
        Assert.Equal(mutation == "default" ? 0 : 2, edge.AttemptedNativeCalls);
        Assert.Equal(mutation is "missing" or "early" ? 1 : mutation == "default" ? 0 : 2, edge.CompletedNativeCalls);
        if (mutation is "missing" or "early") Assert.Null(edge.KeyboardInputs[1].CompletionEventSequence);
    }

    [Fact]
    public void TruncatedTimelineCannotQualifyEvenWithEveryRetainedInputPair()
    {
        var preparation = Preparation([Event(10, ReaderEventKind.InputAttempted, "s"), Event(11, ReaderEventKind.InputCompleted, "s")], 1)
            with
        { TimelineTruncated = true };
        var graph = SyntheticSearchPointerProjection.Build(preparation, null);
        var edge = graph.Edges[1];
        Assert.Equal(1, edge.AttemptedNativeCalls);
        Assert.Equal(1, edge.CompletedNativeCalls);
        Assert.False(edge.InputEvidenceComplete);
        Assert.True(edge.InputUnconfirmed);
    }

    [Fact]
    public void ChangedActivationDocumentRemainsAnExplicitSeparateObservation()
    {
        var preparation = Preparation([], 0);
        var changed = Entered with { DocumentId = "different-document" };
        var activation = new PointerTaskReceipt("pointer", DateTimeOffset.UnixEpoch, changed, null, true, false, false, "INPUT_UNCONFIRMED");
        var graph = SyntheticSearchPointerProjection.Build(preparation, activation);
        Assert.Equal(4, graph.Nodes.Length);
        Assert.Equal("different-document", graph.Nodes[3].Snapshot.DocumentId);
        Assert.Equal("p3", graph.Edges[2].Source);
        Assert.Null(graph.Edges[2].Target);
        Assert.True(graph.Edges[2].InputUnconfirmed);
        Assert.Equal("INPUT_UNCONFIRMED", graph.Edges[2].ErrorCode);
        Assert.DoesNotContain(graph.Edges, e => e.Source == "p2" && e.Target == "p3");
    }
}
