using System.Collections.Immutable;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class FrameSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrameNavigation_BothDirections_DoesNotWrapOrDispatchInput(bool backwards)
    {
        var browser = new FrameBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(backwards ? ReaderCommand.DocumentEnd : ReaderCommand.DocumentStart);
        var command = backwards ? ReaderCommand.PreviousFrame : ReaderCommand.NextFrame;
        var first = await reader.ExecuteAsync(command);
        Assert.Equal(backwards ? "nested-owner" : "owner", first.CursorNodeId);
        var second = await reader.ExecuteAsync(command);
        Assert.Equal(backwards ? "owner" : "nested-owner", second.CursorNodeId);
        var boundary = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.NoMatch, boundary.Outcome);
        Assert.Equal(second.CursorNodeId, boundary.CursorNodeId);
        Assert.Equal(backwards ? "main" : "child", second.Frame?.FrameId);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(ReaderCommand.NextFrame)]
    [InlineData(ReaderCommand.PreviousFrame)]
    public async Task FrameNavigation_FocusMode_IsUnsupported(ReaderCommand command)
    {
        var browser = new FrameBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(ReaderOutcome.Unsupported, (await reader.ExecuteAsync(command)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ChildFocus_ContextAndAnnouncementRetainChildDocumentIdentity()
    {
        var browser = new FrameBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("child-radio", step.CursorNodeId);
        Assert.Equal(step.FocusedNodeId, step.CursorNodeId);
        Assert.Equal("child-document", step.Frame?.DocumentId);
        Assert.Contains(step.Context!.Ancestors, item => item.NodeId == "owner" && item.Role == "Iframe");
        Assert.Null(step.Context.ExposedPeers);
        Assert.Equal(step.Frame, reader.Timeline.Last(item => item.Kind == ReaderEventKind.Announcement).Frame);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task CrossFrameActivation_SameName_RequiresActualTargetFocus()
    {
        var snapshot = Snapshot();
        var browser = new FrameBrowser(snapshot with
        {
            Nodes = snapshot.Nodes
            .Select(node => node with { IsFocused = false }).ToImmutableArray()
            .Insert(1, new("main-button", "button", "Shared action", IsFocused: true, ParentId: "root", FrameId: "main", DocumentId: "main-document"))
            .Add(new("child-button", "button", "Shared action", ParentId: "child-root", FrameId: "child", DocumentId: "child-document"))
        });
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        await reader.ExecuteAsync(ReaderCommand.NextButton);
        var target = await reader.ExecuteAsync(ReaderCommand.NextButton);
        Assert.Equal("child-button", target.CursorNodeId);
        var activated = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.FocusRequired, activated.Outcome);
        Assert.Equal("main-button", activated.FocusedNodeId);
        Assert.Equal("child", activated.Frame?.FrameId);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ChildGenerationChange_InvalidatesTextEvenIfNodeIdsAndContentAreReused()
    {
        var browser = new FrameBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        var selected = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("Child", selected.Announcement);
        Assert.Equal("child-document", selected.TextRange?.DocumentId);
        Assert.Equal("child", selected.TextRange?.FrameId);
        browser.Snapshot = Snapshot("replacement-document");
        var changed = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal(ReaderOutcome.Unknown, changed.Outcome);
        Assert.Equal(TextCursorResetReason.DocumentChanged, changed.TextCursorReset);
        Assert.Null(changed.TextRange);
        Assert.Equal(ReaderOutcome.Unknown, (await reader.ExecuteAsync(ReaderCommand.ReadCurrentText)).Outcome);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        Assert.Equal("replacement-document", (await reader.ExecuteAsync(ReaderCommand.NextWord)).TextRange?.DocumentId);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ChildGenerationChange_PreservesMainDocumentTextCursor()
    {
        var browser = new FrameBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        await reader.ExecuteAsync(ReaderCommand.MoveNext);
        Assert.Equal("Main", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        browser.Snapshot = Snapshot("replacement-document");
        var next = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("text", next.Announcement);
        Assert.Equal("main-document", next.TextRange?.DocumentId);
        Assert.Null(next.TextCursorReset);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.FrameChanged && item.Frame?.DocumentId == "replacement-document");
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ChildLiveChanges_ReplacementSeedsBaselineWithoutSuppressingMainUpdates()
    {
        AccessibilitySnapshot Live(string document, string childText, string mainText)
        {
            var snapshot = Snapshot(document);
            return snapshot with
            {
                Nodes = snapshot.Nodes
                .Add(new("main-status", "status", "", ParentId: "root", LiveRegion: new("polite", true, false, mainText), FrameId: "main", DocumentId: "main-document"))
                .Add(new("child-status", "status", "", ParentId: "child-root", LiveRegion: new("polite", true, false, childText), FrameId: "child", DocumentId: document))
            };
        }
        var browser = new FrameBrowser(Live("child-document", "Child ready", "Main ready"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = Live("child-document", "Child done", "Main ready");
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = Live("replacement-document", "New child initial content", "Main changed");
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = Live("replacement-document", "Child changed", "Main changed");
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        var changes = reader.Timeline.Where(item => item.Kind == ReaderEventKind.LiveRegionChanged).ToArray();
        Assert.Equal(new[] { "Child done", "Main changed", "Child changed" }, changes.Select(item => item.Detail));
        Assert.Equal("replacement-document", changes[^1].Frame?.DocumentId);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(FrameObservationStatus.Unavailable)]
    [InlineData(FrameObservationStatus.Unsupported)]
    [InlineData(FrameObservationStatus.LimitExceeded)]
    [InlineData(FrameObservationStatus.Excluded)]
    public async Task IncompleteFrameCoverage_IsReportedWithoutAnAccessibilityBarrier(FrameObservationStatus status)
    {
        var snapshot = new AccessibilitySnapshot("main-document", [new("root", "RootWebArea", "")], [])
        { Frames = [new("unobserved", "main", null, null, null, status, "TEST_FRAME_NOT_OBSERVED")] };
        var browser = new FrameBrowser(snapshot);
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.NextHeading);
        Assert.Equal(ReaderOutcome.NoMatch, step.Outcome);
        Assert.Equal(status, Assert.Single(step.FrameCoverage).Status);
        Assert.Null(step.BarrierCode);
        Assert.Null(step.Frame);
        Assert.False(new ObservationExpectation("heading", "Unobserved child goal").IsSatisfiedBy(snapshot));
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task FrameDetachment_RetainsHistoricalDocumentAndExplicitEvent()
    {
        var browser = new FrameBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = browser.Snapshot with
        {
            Nodes = browser.Snapshot.Nodes.Where(node => node.FrameId == "main").ToImmutableArray(),
            Frames = [browser.Snapshot.Frames[0]]
        };
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Single(step.FrameCoverage);
        var detached = Assert.Single(reader.Timeline, item => item.Kind == ReaderEventKind.FrameChanged
            && item.Frame?.FrameId == "child" && item.Frame.ReasonCode == "FRAME_DETACHED");
        Assert.Equal("child-document", detached.Frame?.DocumentId);
        Assert.Equal(FrameObservationStatus.Unavailable, detached.Frame?.Status);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(33, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 9)]
    public async Task FrameCapture_InvalidBounds_AreRejectedBeforeBrowserLaunch(int count, int depth)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChromiumFixtureBrowser.LaunchAsync(frameOptions: new(count, depth)));
    }

    private static AccessibilitySnapshot Snapshot(string childDocument = "child-document") => new("main-document", [
        new("root", "RootWebArea", "", FrameId: "main", DocumentId: "main-document"),
        new("main-text", "StaticText", "Main text", ParentId: "root", Text: new("Main text", SemanticSource.ChromiumAccessibility), FrameId: "main", DocumentId: "main-document"),
        new("outer", "group", "Outer group", ParentId: "root", FrameId: "main", DocumentId: "main-document"),
        new("owner", "Iframe", "Child frame", ParentId: "outer", FrameId: "main", DocumentId: "main-document"),
        new("child-root", "RootWebArea", "", ParentId: "owner", FrameId: "child", DocumentId: childDocument),
        new("heading", "heading", "Child heading", HeadingLevel: 2, ParentId: "child-root", FrameId: "child", DocumentId: childDocument),
        new("child-text", "StaticText", "Child text", ParentId: "heading", Text: new("Child text", SemanticSource.ChromiumAccessibility), FrameId: "child", DocumentId: childDocument),
        new("child-radio", "radio", "Child choice", IsFocused: true, ParentId: "child-root", FrameId: "child", DocumentId: childDocument),
        new("nested-owner", "Iframe", "Nested frame", ParentId: "child-root", FrameId: "child", DocumentId: childDocument),
        new("end", "StaticText", "End", ParentId: "root", FrameId: "main", DocumentId: "main-document")
    ], [])
    { Frames = [new("main", null, null, "main-document", "root", FrameObservationStatus.Observed), new("child", "main", "owner", childDocument, "child-root", FrameObservationStatus.Observed)] };

    private sealed class FrameBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public List<string> Keys { get; } = [];
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) { Keys.Add(key); return Task.CompletedTask; }
    }
}
