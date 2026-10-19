using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class CompositeSessionTests
{
    [Theory]
    [InlineData(ReaderCommand.NextTree, "tree", false)]
    [InlineData(ReaderCommand.PreviousTree, "tree", true)]
    [InlineData(ReaderCommand.NextTreeItem, "treeitem", false)]
    [InlineData(ReaderCommand.PreviousTreeItem, "treeitem", true)]
    [InlineData(ReaderCommand.NextTabItem, "tab", false)]
    [InlineData(ReaderCommand.PreviousTabItem, "tab", true)]
    public async Task CompositeNavigation_BothDirections_NoWrapOrInput(ReaderCommand command, string role, bool backwards)
    {
        var browser = new CompositeBrowser(Snapshot(new("start", "heading", "Start"),
            new("first", role, "First"), new("skip", "button", "Skip"), new("last", role, "Last"), new("end", "heading", "End")));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(backwards ? ReaderCommand.DocumentEnd : ReaderCommand.DocumentStart);
        Assert.Equal(backwards ? "last" : "first", (await reader.ExecuteAsync(command)).CursorNodeId);
        Assert.Equal(backwards ? "first" : "last", (await reader.ExecuteAsync(command)).CursorNodeId);
        var boundary = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.NoMatch, boundary.Outcome);
        Assert.Equal(backwards ? "first" : "last", boundary.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(ReaderCommand.NextTree)]
    [InlineData(ReaderCommand.PreviousTree)]
    [InlineData(ReaderCommand.NextTreeItem)]
    [InlineData(ReaderCommand.PreviousTreeItem)]
    [InlineData(ReaderCommand.NextTabItem)]
    [InlineData(ReaderCommand.PreviousTabItem)]
    public async Task CompositeNavigation_EmptyAndFocusMode_RemainExplicit(ReaderCommand command)
    {
        var browser = new CompositeBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(command)).Outcome);
        browser.Snapshot = Snapshot(new AccessibleNode("focus", "tab", "Focus", IsFocused: true));
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(ReaderOutcome.Unsupported, (await reader.ExecuteAsync(command)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task TreeContext_ScopesExposedPeersToKnownParentAndLevel_SeparatesLogicalSet()
    {
        var browser = new CompositeBrowser(Snapshot(
            new("tree", "tree", "Files"),
            new("parent", "treeitem", "Projects", ParentId: "tree", HierarchyLevel: Level(1)),
            new("group", "group", "", ParentId: "parent"),
            new("first", "treeitem", "Design", ParentId: "group", HierarchyLevel: Level(2)),
            new("second", "treeitem", "Notes", IsFocused: true, ParentId: "group", HierarchyLevel: Level(2),
                LogicalSet: new(40, -1, true, SemanticSource.DeclaredAria)),
            new("other", "treeitem", "Archive", ParentId: "tree", HierarchyLevel: Level(1)),
            new("flat", "treeitem", "Unparented child", ParentId: "tree", HierarchyLevel: Level(2)),
            new("other-tree", "tree", "Other files"),
            new("unrelated", "treeitem", "Another root", ParentId: "other-tree", HierarchyLevel: Level(1))));
        using var reader = new ScreenReaderSession(browser);
        var current = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(new ExposedPeerPosition("group", 2, 2), current.Context?.ExposedPeers);
        Assert.Equal(Level(2), current.Context?.HierarchyLevel);
        Assert.Equal(new[] { "tree", "parent", "group" }, current.Context?.Ancestors.Select(a => a.NodeId).ToArray());
        Assert.Equal(new LogicalSetPosition(40, -1, true, SemanticSource.DeclaredAria), current.Context?.LogicalSet);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var root = await reader.ExecuteAsync(ReaderCommand.NextTreeItem);
        Assert.Equal(new ExposedPeerPosition("tree", 1, 2), root.Context?.ExposedPeers);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task TreeContext_UncertainOrCrossDocumentOwnership_DoesNotInventPeers(int scenario)
    {
        var level = scenario == 0 ? null : Level(scenario == 1 ? 0 : scenario is 4 or 6 ? 1 : 2);
        var parent = scenario switch { 2 => "missing", 3 => "item", 4 => "child-root", _ => "tree" };
        var browser = new CompositeBrowser(Snapshot(new("tree", "tree", "Files", DocumentId: "main"),
            new("child-root", "RootWebArea", "Child", ParentId: "tree"),
            new("item", "treeitem", "Current", IsFocused: true, ParentId: parent, HierarchyLevel: level,
                DocumentId: scenario == 6 ? "child" : null)));
        using var reader = new ScreenReaderSession(browser);
        var context = (await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Context;
        Assert.NotNull(context);
        Assert.Null(context.ExposedPeers);
        Assert.Equal(level, context.HierarchyLevel);
        Assert.Equal(scenario is not 2 and not 3, context.AncestryComplete);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task TabContext_IndependentTablistsAndPanels_AreScoped()
    {
        var browser = new CompositeBrowser(Snapshot(new("list", "tablist", "Views"),
            new("first", "tab", "Overview", ParentId: "list"),
            new("last", "tab", "Details", IsFocused: true, ParentId: "list", Selected: false),
            new("other-list", "tablist", "Other views"), new("other", "tab", "Details", ParentId: "other-list"),
            new("panel", "tabpanel", "Details"), new("text", "StaticText", "Content", ParentId: "panel")));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(new ExposedPeerPosition("list", 2, 2), step.Context?.ExposedPeers);
        Assert.Equal("list", Assert.Single(step.Context!.Ancestors).NodeId);
        Assert.Equal("Details, tab", step.Announcement);
        await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        Assert.Equal("tabpanel", Assert.Single((await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Context!.Ancestors).Role);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("treeitem")]
    [InlineData("tab")]
    [InlineData("tablist")]
    public async Task CompositeTabFocus_UsesFocusMode(string role)
    {
        var browser = new CompositeBrowser(Snapshot(new AccessibleNode("focus", role, "Focus", IsFocused: true)));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(ReaderMode.Focus, step.Mode);
        Assert.Equal("focus", step.FocusedNodeId);
        Assert.Equal("focus", step.CursorNodeId);
        Assert.Equal(new[] { "Tab" }, browser.Keys);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("tablist")]
    [InlineData("treeitem")]
    public async Task CompositeActivation_ObservedFocusAndOwnership_DispatchesEnter(string ownerRole)
    {
        var composite = ownerRole is "tree" or "tablist";
        var itemRole = ownerRole == "tablist" ? "tab" : "treeitem";
        var nodes = composite
            ? new[] { new AccessibleNode("owner", ownerRole, "Owner", IsFocused: true, ActiveDescendantId: "item"),
                new AccessibleNode("item", itemRole, "Item", ParentId: "owner", HierarchyLevel: itemRole == "treeitem" ? Level(1) : null) }
            : [new AccessibleNode("item", itemRole, "Item", IsFocused: true, HierarchyLevel: Level(1))];
        var browser = new CompositeBrowser(Snapshot(nodes));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
        Assert.Equal(1, step.BrowserKeystrokes);
        Assert.Equal("item", step.CursorNodeId);
        Assert.Equal(composite ? "owner" : "item", step.FocusedNodeId);
        Assert.Equal(new[] { "Enter" }, browser.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task CompositeActivation_UnresolvedWrongOrCrossDocumentOwner_IsUnsupported(int scenario)
    {
        var parent = scenario switch { 2 => "other", 3 => "missing", 4 => "item", 5 => "child-root", _ => "owner" };
        var browser = new CompositeBrowser(Snapshot(
            new("owner", "tree", "Files", IsFocused: true, ActiveDescendantId: scenario == 0 ? "missing" : "item", DocumentId: "main"),
            new("other", "tree", "Other"), new("child-root", "RootWebArea", "Child", ParentId: "owner"),
            new("item", scenario == 1 ? "button" : "treeitem", "Item", ParentId: parent, HierarchyLevel: Level(1),
                DocumentId: scenario == 6 ? "child" : null)));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Null(step.BarrierCode);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false, ReaderOutcome.FocusRequired)]
    [InlineData(true, ReaderOutcome.Blocked)]
    public async Task CompositeActivation_UnfocusedOrDisabledItem_DoesNotDispatch(bool disabled, ReaderOutcome expected)
    {
        var browser = new CompositeBrowser(Snapshot(new("tree", "tree", "Files"),
            new("item", "treeitem", "Item", IsDisabled: disabled, ParentId: "tree", HierarchyLevel: Level(1))));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextTreeItem);
        Assert.Equal(expected, (await reader.ExecuteAsync(ReaderCommand.Activate)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(-1, "level unknown")]
    [InlineData(0, "level unknown")]
    [InlineData(1, "level 1")]
    public async Task TreeLevel_AnnouncesValidityAndRetainsHistoricalEvent(int level, string description)
    {
        var browser = new CompositeBrowser(Snapshot(new AccessibleNode("item", "treeitem", "Item", HierarchyLevel: Level(level))));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.NextTreeItem);
        Assert.Equal($"Item, treeitem, {description}", step.Announcement);
        Assert.Equal(Level(level), step.Context?.HierarchyLevel);
        var first = Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.Announcement);
        Assert.Equal(Level(level), first.HierarchyLevel);
        browser.Snapshot = Snapshot(new AccessibleNode("item", "treeitem", "Item", HierarchyLevel: Level(2)));
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(Level(2), Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.WidgetStateChanged).HierarchyLevel);
        Assert.Equal(Level(level), first.HierarchyLevel);
        Assert.Empty(browser.Keys);
    }

    private static HierarchyLevelObservation Level(int value) => new(value, SemanticSource.ChromiumAccessibility);
    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) => new("composite-document", nodes.ToImmutableArray(), []);

    private sealed class CompositeBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public List<string> Keys { get; } = [];
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) { Keys.Add(key); return Task.CompletedTask; }
    }
}
