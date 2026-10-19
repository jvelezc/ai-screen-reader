using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumCompositeTests
{
    [Fact]
    public async Task CompositeCapture_Chromium_ExposesLevelsAndSelection_WithoutHiddenChildrenOrPanels()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Composites);
        var snapshot = await browser.CaptureAsync();
        var projects = Named(snapshot, "treeitem", "Projects");
        Assert.Equal(new(1, SemanticSource.ChromiumAccessibility), projects.HierarchyLevel);
        Assert.Null(projects.HeadingLevel);
        Assert.True(projects.Selected);
        Assert.False(projects.Expanded);
        Assert.Null(projects.LogicalSet);
        Assert.Equal(1, Named(snapshot, "heading", "Composite fixture").HeadingLevel);
        Assert.DoesNotContain(snapshot.Nodes, n => n.Name is "Design" or "Notes" or "Details content");
        Assert.Equal("Overview", Assert.Single(snapshot.Nodes, n => n.Role == "tabpanel").Name);
        Assert.True(Named(snapshot, "tab", "Overview").Selected);
        Assert.False(Named(snapshot, "tab", "Details").Selected);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextTree);
        var selected = await reader.ExecuteAsync(ReaderCommand.NextTreeItem);
        Assert.Equal("Projects, treeitem, collapsed, selected, level 1", selected.Announcement);
        Assert.Equal(new ExposedPeerPosition(Named(snapshot, "tree", "Files").Id, 1, 2), selected.Context?.ExposedPeers);
        Assert.Equal(new[] { "main", "tree" }, selected.Context?.Ancestors.Select(a => a.Role).ToArray());
        Assert.Equal("Files", selected.Context!.Ancestors[^1].Name);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task TreeKeyboard_Chromium_ExpansionFocusAndSelectionRemainSeparate_EnterUsesObservedOwner()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Composites);
        using var reader = new ScreenReaderSession(browser);
        var focused = await reader.ExecuteAsync(ReaderCommand.Tab);
        var initial = await browser.CaptureAsync();
        var owner = Named(initial, "tree", "Files");
        Assert.Equal(ReaderMode.Focus, focused.Mode);
        Assert.Equal(owner.Id, focused.FocusedNodeId);
        Assert.Equal(Named(initial, "treeitem", "Projects").Id, focused.CursorNodeId);
        Assert.Equal(focused.ActiveDescendantNodeId, focused.CursorNodeId);
        var expanded = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowRight",
            expectation: new("treeitem", "Projects", MustBeFocused: true, Expanded: true, Selected: true));
        Assert.Equal(VerificationStatus.Satisfied, expanded.Verification);
        var child = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown",
            expectation: new("treeitem", "Design", MustBeFocused: true, Selected: false));
        Assert.Equal(VerificationStatus.Satisfied, child.Verification);
        Assert.Equal(owner.Id, child.FocusedNodeId);
        Assert.NotEqual(child.FocusedNodeId, child.CursorNodeId);
        Assert.Equal(new(2, SemanticSource.ChromiumAccessibility), child.Context?.HierarchyLevel);
        Assert.Equal(2, child.Context?.ExposedPeers?.Count);
        Assert.Equal(1, child.Context?.ExposedPeers?.Position);
        Assert.Equal(new[] { "main", "tree", "treeitem", "group" }, child.Context?.Ancestors.Select(a => a.Role).ToArray());
        var activated = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("treeitem", "Design", MustBeFocused: true, Selected: true));
        Assert.Equal(VerificationStatus.Satisfied, activated.Verification);
        Assert.Equal(1, activated.BrowserKeystrokes);
        Assert.Equal(owner.Id, activated.FocusedNodeId);
        var final = await browser.CaptureAsync();
        Assert.Contains(final.Nodes, n => n.Role == "StaticText" && n.Name == "Opened Design");
        Assert.False(Named(final, "treeitem", "Projects").Selected);
        Assert.True(Named(final, "treeitem", "Design").Selected);
        var inputs = reader.Timeline.Where(e => e.Kind == ReaderEventKind.InputAttempted && e.Command != ReaderCommand.Tab).ToArray();
        Assert.Equal(new[] { "ArrowRight", "ArrowDown", "Enter" }, inputs.Select(e => e.Detail).ToArray());
        Assert.All(inputs, e => Assert.Equal(owner.Id, e.NodeId));
        Assert.Contains(reader.Timeline, e => e.Kind == ReaderEventKind.ActiveDescendantChanged
            && e.NodeId == child.CursorNodeId && e.HierarchyLevel?.Value == 2);
    }

    [Fact]
    public async Task ExternalCollapse_Chromium_RemovesHiddenItemsWithoutInventingCursorMovement()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Composites);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowRight");
        var child = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        await reader.ExecuteAsync(ReaderCommand.EnterBrowseMode);
        var inputsBefore = reader.Timeline.Count(e => e.Kind == ReaderEventKind.InputAttempted);
        // A separately controlled keyboard action is observed, never silently attributed to the reader.
        await browser.PressKeyAsync("ArrowLeft");
        await browser.PressKeyAsync("ArrowLeft");
        var read = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(ReaderMode.Browse, read.Mode);
        Assert.Null(read.CursorNodeId);
        Assert.NotEqual(child.CursorNodeId, read.Context?.NodeId);
        Assert.Equal(0, read.BrowserKeystrokes);
        Assert.Equal(inputsBefore, reader.Timeline.Count(e => e.Kind == ReaderEventKind.InputAttempted));
        var snapshot = await browser.CaptureAsync();
        Assert.False(Named(snapshot, "treeitem", "Projects").Expanded);
        Assert.DoesNotContain(snapshot.Nodes, n => n.Name is "Design" or "Notes");
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        Assert.Equal("Projects, treeitem, collapsed, selected, level 1", (await reader.ExecuteAsync(ReaderCommand.NextTreeItem)).Announcement);
        Assert.Equal("Archive, treeitem, level 1", (await reader.ExecuteAsync(ReaderCommand.NextTreeItem)).Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextTreeItem)).Outcome);
    }

    [Fact]
    public async Task Tabs_Chromium_RovingFocusDoesNotSelectUntilActivation_VisiblePanelFollowsSelection()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Composites);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var first = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(ReaderMode.Focus, first.Mode);
        Assert.Equal("Overview, tab, selected", first.Announcement);
        var end = await reader.ExecuteAsync(ReaderCommand.PressKey, "End",
            expectation: new("tab", "Details", MustBeFocused: true, Selected: false));
        Assert.Equal(VerificationStatus.Satisfied, end.Verification);
        var home = await reader.ExecuteAsync(ReaderCommand.PressKey, "Home");
        Assert.Equal(first.FocusedNodeId, home.FocusedNodeId);
        var moved = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowRight",
            expectation: new("tab", "Details", MustBeFocused: true, Selected: false));
        Assert.Equal(VerificationStatus.Satisfied, moved.Verification);
        Assert.Equal(moved.CursorNodeId, moved.FocusedNodeId);
        Assert.NotEqual(first.FocusedNodeId, moved.FocusedNodeId);
        Assert.Equal(2, moved.Context?.ExposedPeers?.Position);
        Assert.Equal(2, moved.Context?.ExposedPeers?.Count);
        Assert.Equal(new[] { "main", "tablist" }, moved.Context?.Ancestors.Select(a => a.Role).ToArray());
        Assert.Equal("Views", moved.Context!.Ancestors[^1].Name);
        var before = await browser.CaptureAsync();
        Assert.True(Named(before, "tab", "Overview").Selected);
        Assert.Equal("Overview", Assert.Single(before.Nodes, n => n.Role == "tabpanel").Name);
        var selected = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("tab", "Details", MustBeFocused: true, Selected: true));
        Assert.Equal(VerificationStatus.Satisfied, selected.Verification);
        Assert.Equal(1, selected.BrowserKeystrokes);
        Assert.Equal(moved.FocusedNodeId, selected.FocusedNodeId);
        var after = await browser.CaptureAsync();
        Assert.Equal("Details", Assert.Single(after.Nodes, n => n.Role == "tabpanel").Name);
        Assert.DoesNotContain(after.Nodes, n => n.Name == "Overview content");
        Assert.Contains(after.Nodes, n => n.Name == "Details content");
        await reader.ExecuteAsync(ReaderCommand.EnterBrowseMode);
        var browsed = await reader.ExecuteAsync(ReaderCommand.PreviousTabItem);
        Assert.Equal(first.FocusedNodeId, browsed.CursorNodeId);
        Assert.Equal(moved.FocusedNodeId, browsed.FocusedNodeId);
        var refused = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.FocusRequired, refused.Outcome);
        Assert.Equal(0, refused.BrowserKeystrokes);
    }

    [Fact]
    public async Task CompositeBrowse_Chromium_DoesNotDispatchKeysOrExpandHiddenContent()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Composites);
        using var reader = new ScreenReaderSession(browser);
        var initialFocus = (await browser.CaptureAsync()).FocusedNode?.Id;
        Assert.Equal("Files, tree", (await reader.ExecuteAsync(ReaderCommand.NextTree)).Announcement);
        await reader.ExecuteAsync(ReaderCommand.NextTreeItem);
        Assert.Equal("Archive, treeitem, level 1", (await reader.ExecuteAsync(ReaderCommand.NextTreeItem)).Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextTreeItem)).Outcome);
        Assert.Equal("Overview, tab, selected", (await reader.ExecuteAsync(ReaderCommand.NextTabItem)).Announcement);
        Assert.Equal("Details, tab", (await reader.ExecuteAsync(ReaderCommand.NextTabItem)).Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextTabItem)).Outcome);
        Assert.Equal("Overview, tab, selected", (await reader.ExecuteAsync(ReaderCommand.PreviousTabItem)).Announcement);
        Assert.Equal("Files, tree", (await reader.ExecuteAsync(ReaderCommand.PreviousTree)).Announcement);
        var final = await browser.CaptureAsync();
        Assert.Equal(initialFocus, final.FocusedNode?.Id);
        Assert.False(Named(final, "treeitem", "Projects").Expanded);
        Assert.DoesNotContain(final.Nodes, n => n.Name is "Design" or "Notes" or "Details content");
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    private static AccessibleNode Named(AccessibilitySnapshot snapshot, string role, string name) =>
        Assert.Single(snapshot.Nodes, node => node.Role == role && node.Name == name);

    [Fact]
    public async Task RovingTree_Chromium_RealTreeitemFocusRoutesKeysAndActivation()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Composites);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var button = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal("Use roving tree, button", button.Announcement);
        var switched = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("treeitem", "Projects", MustBeFocused: true));
        Assert.Equal(VerificationStatus.Satisfied, switched.Verification);
        Assert.Equal(switched.CursorNodeId, switched.FocusedNodeId);
        Assert.Null(switched.ActiveDescendantNodeId);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var next = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown",
            expectation: new("treeitem", "Archive", MustBeFocused: true, Selected: false));
        Assert.Equal(VerificationStatus.Satisfied, next.Verification);
        Assert.Equal(next.CursorNodeId, next.FocusedNodeId);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var returned = await reader.ExecuteAsync(ReaderCommand.ReverseTab);
        Assert.Equal(ReaderMode.Focus, returned.Mode);
        Assert.Equal(next.FocusedNodeId, returned.FocusedNodeId);
        Assert.Null(returned.ActiveDescendantNodeId);
        var selected = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("treeitem", "Archive", MustBeFocused: true, Selected: true));
        Assert.Equal(VerificationStatus.Satisfied, selected.Verification);
        Assert.Equal(1, selected.BrowserKeystrokes);
        var activation = reader.Timeline.Last(e => e.Kind == ReaderEventKind.InputAttempted);
        Assert.Equal("Enter", activation.Detail);
        Assert.Equal(next.FocusedNodeId, activation.NodeId);
        Assert.Contains((await browser.CaptureAsync()).Nodes, n => n.Role == "StaticText" && n.Name == "Opened Archive");
    }
}
