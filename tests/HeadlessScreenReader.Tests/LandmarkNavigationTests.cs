using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class LandmarkNavigationTests
{
    [Theory]
    [InlineData("leaf")]
    [InlineData("current")]
    public async Task PreviousLandmark_NestedContext_ReachesPreviousSibling(string cursorId)
    {
        var browser = new LandmarkBrowser([
            new("primary", "navigation", "Primary"),
            new("main", "main", ""),
            new("earlier", "region", "Earlier", ParentId: "main"),
            new("earlier-text", "StaticText", "Earlier content", ParentId: "earlier"),
            new("current", "region", "Current", ParentId: "main"),
            new("leaf", "StaticText", "End", IsFocused: cursorId == "leaf", ParentId: "current")
        ]);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        if (cursorId == "current") await reader.ExecuteAsync(ReaderCommand.MovePrevious);

        var sibling = await reader.ExecuteAsync(ReaderCommand.PreviousLandmark);
        Assert.Equal("earlier", sibling.CursorNodeId);
        Assert.Equal(ReaderOutcome.CursorMoved, sibling.Outcome);
        Assert.Equal("Earlier, region", sibling.Announcement);
        var previous = await reader.ExecuteAsync(ReaderCommand.PreviousLandmark);
        Assert.Equal("primary", previous.CursorNodeId);
        Assert.Equal(ReaderOutcome.CursorMoved, previous.Outcome);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousLandmark)).Outcome);
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task PreviousLandmark_OnlyEnclosingLandmarks_PreservesCursorOnNoMatch()
    {
        var browser = new LandmarkBrowser([
            new("main", "main", ""), new("region", "region", "Inner", ParentId: "main"),
            new("leaf", "StaticText", "End", ParentId: "region")
        ]);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        var step = await reader.ExecuteAsync(ReaderCommand.PreviousLandmark);
        Assert.Equal(ReaderOutcome.NoMatch, step.Outcome);
        Assert.Equal("leaf", step.CursorNodeId);
        Assert.Equal("End, StaticText", (await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Announcement);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviousLandmark_IncompleteAncestry_SkipsKnownParentsWithoutGuessing(bool cycle)
    {
        var browser = new LandmarkBrowser([
            new("primary", "navigation", "Primary"),
            new("main", "main", "", ParentId: cycle ? "leaf" : "missing"),
            new("leaf", "StaticText", "End", ParentId: "main")
        ]);
        using var reader = new ScreenReaderSession(browser);
        var boundary = await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        Assert.False(boundary.Context?.AncestryComplete);
        var step = await reader.ExecuteAsync(ReaderCommand.PreviousLandmark);
        Assert.Equal("primary", step.CursorNodeId);
        Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task PreviousLandmark_UnobservedParentage_DoesNotInferEnclosureFromOrder()
    {
        var browser = new LandmarkBrowser([
            new("main", "main", ""), new("leaf", "StaticText", "End")
        ]);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        var step = await reader.ExecuteAsync(ReaderCommand.PreviousLandmark);
        Assert.Equal("main", step.CursorNodeId);
        Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task LandmarkNavigation_UnsetCursorAndForwardTraversal_RetainNodeOrder()
    {
        var browser = new LandmarkBrowser([
            new("main", "main", ""), new("region", "region", "Inner", ParentId: "main"),
            new("leaf", "StaticText", "End", ParentId: "region")
        ]);
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal("region", (await reader.ExecuteAsync(ReaderCommand.PreviousLandmark)).CursorNodeId);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        Assert.Equal("region", (await reader.ExecuteAsync(ReaderCommand.NextLandmark)).CursorNodeId);
        Assert.Equal("main", (await reader.ExecuteAsync(ReaderCommand.MovePrevious)).CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    private sealed class LandmarkBrowser(ImmutableArray<AccessibleNode> nodes) : IScreenReaderBrowser
    {
        public List<string> Keys { get; } = [];
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccessibilitySnapshot("landmarks", nodes, []));
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            return Task.CompletedTask;
        }
    }
}
