using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class MultiSelectSessionTests
{
    [Fact]
    public async Task SelectionContext_PreservesSourcesUnknownsAndExposedOwnerScope()
    {
        var browser = new ListBrowser(Snapshot(Owner(), Option("a", true), Option("b", null),
            Owner("other"), Option("foreign", true, "other")));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextListbox);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        var context = Assert.IsType<ListboxSelectionContext>(step.ListboxSelection);
        Assert.True(context.ExposedOptionsOnly);
        Assert.Equal(new(true, SemanticSource.ChromiumAccessibility), context.MultiSelectable);
        Assert.Equal(new[] { "a", "b" }, context.Options.Select(option => option.NodeId));
        Assert.Equal(new(true, SemanticSource.ChromiumAccessibility), context.Options[0].Selection);
        Assert.Null(context.Options[1].Selection);
        Assert.Contains("1 selected among 2 exposed options, 1 selection states unknown", step.Announcement);
        var announcement = reader.Timeline.Last(e => e.Kind == ReaderEventKind.Announcement);
        Assert.Equal(context.ListboxNodeId, announcement.ListboxSelection?.ListboxNodeId);
        Assert.Equal(context.Options.ToArray(), announcement.ListboxSelection!.Options.ToArray());
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionBrowse_IsScopedNonwrappingAndReadOnly(bool reverse)
    {
        var browser = new ListBrowser(Snapshot(Owner(), Option("a", true),
            new("group", "group", "Group", ParentId: "list", DocumentId: "doc", FrameId: "frame"),
            Option("b", false, "group"), Owner("other"), Option("foreign", true, "other")));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextListbox);
        var command = reverse ? ReaderCommand.PreviousOption : ReaderCommand.NextOption;
        Assert.Equal(reverse ? "b" : "a", (await reader.ExecuteAsync(command)).CursorNodeId);
        Assert.Equal(reverse ? "a" : "b", (await reader.ExecuteAsync(command)).CursorNodeId);
        var boundary = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.NoMatch, boundary.Outcome);
        Assert.Equal(reverse ? "a" : "b", boundary.CursorNodeId);
        Assert.Equal("list", browser.Snapshot.FocusedNode?.Id);
        Assert.True(browser.Snapshot.Nodes.Single(n => n.Id == "a").Selected);
        Assert.False(browser.Snapshot.Nodes.Single(n => n.Id == "b").Selected);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListboxBrowse_TraversesBothDirectionsWithoutWrapping(bool reverse)
    {
        var browser = new ListBrowser(Snapshot(Owner(), Option("a", true), Owner("other")));
        using var reader = new ScreenReaderSession(browser);
        var command = reverse ? ReaderCommand.PreviousListbox : ReaderCommand.NextListbox;
        Assert.Equal(reverse ? "other" : "list", (await reader.ExecuteAsync(command)).CursorNodeId);
        Assert.Equal(reverse ? "list" : "other", (await reader.ExecuteAsync(command)).CursorNodeId);
        var boundary = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.NoMatch, boundary.Outcome);
        Assert.Equal(reverse ? "list" : "other", boundary.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(ReaderCommand.NextOption)]
    [InlineData(ReaderCommand.PreviousOption)]
    [InlineData(ReaderCommand.NextListbox)]
    [InlineData(ReaderCommand.PreviousListbox)]
    public async Task ListboxBrowse_EmptyAndFocusModeAreExplicit(ReaderCommand command)
    {
        var browser = new ListBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal(command is ReaderCommand.NextOption or ReaderCommand.PreviousOption
            ? ReaderOutcome.Unsupported : ReaderOutcome.NoMatch, (await reader.ExecuteAsync(command)).Outcome);
        browser.Snapshot = Snapshot(Owner(), Option("a", true));
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(ReaderOutcome.Unsupported, (await reader.ExecuteAsync(command)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FocusedActivation_SelectsAndDeselects_WithoutRepairingFocus(bool roving)
    {
        var browser = new ListBrowser(Snapshot(Owner() with { IsFocused = !roving },
            Option("a", true), Option("b", false) with { IsFocused = roving }));
        if (!roving) browser.Replace("list", n => n with { ActiveDescendantId = "b" });
        browser.OnKey = key =>
        {
            if (key == "Space") browser.Replace("b", n => n with
            { Selected = !n.Selected, Selection = new(!n.Selected!.Value, SemanticSource.ChromiumAccessibility) });
        };
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var selected = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: Goal("b", "a", "b"));
        Assert.Equal(VerificationStatus.Satisfied, selected.Verification);
        Assert.Equal(1, selected.BrowserKeystrokes);
        Assert.Equal(roving ? "b" : "list", selected.FocusedNodeId);
        Assert.Equal("b", selected.CursorNodeId);
        Assert.Equal(roving ? null : "b", selected.ActiveDescendantNodeId);
        Assert.Equal(new[] { "a", "b" }, selected.ListboxSelection!.Options.Where(o => o.Selection!.Value).Select(o => o.NodeId));
        var deselected = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: Goal("b", "a"));
        Assert.True(selected.ListboxSelection.Options.Single(o => o.NodeId == "b").Selection!.Value);
        Assert.Equal(VerificationStatus.Satisfied, deselected.Verification);
        Assert.Contains(", not selected", deselected.Announcement);
        Assert.True(browser.Snapshot.Nodes.Single(n => n.Id == "a").Selected);
        Assert.False(browser.Snapshot.Nodes.Single(n => n.Id == "b").Selected);
        Assert.Equal(new[] { "Space", "Space" }, browser.Keys);
        Assert.Contains(reader.Timeline, e => e.Kind == ReaderEventKind.WidgetStateChanged && e.NodeId == "b"
            && e.ListboxSelection!.Options.Single(o => o.NodeId == "b").Selection!.Value);
    }

    [Fact]
    public async Task BrowseActivation_RequiresActualFocusAndNeverRepairsIt()
    {
        var browser = new ListBrowser(Snapshot(Owner(), Option("a", true), Option("b", false)));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextListbox);
        await reader.ExecuteAsync(ReaderCommand.NextOption);
        await reader.ExecuteAsync(ReaderCommand.NextOption);
        Assert.Equal(ReaderOutcome.FocusRequired, (await reader.ExecuteAsync(ReaderCommand.Activate)).Outcome);
        Assert.Equal("a", browser.Snapshot.EffectiveFocusedNode?.Id);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledOptionOrOwner_IsBlockedWithoutInput(bool ownerDisabled)
    {
        var browser = new ListBrowser(Snapshot(Owner() with { IsDisabled = ownerDisabled },
            Option("a", false) with { IsDisabled = !ownerDisabled }));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: Goal("a", "a"));
        Assert.Equal(ReaderOutcome.Blocked, step.Outcome);
        Assert.Equal(VerificationStatus.Blocked, step.Verification);
        Assert.Equal("OBSERVED_DISABLED_CONTROL", step.BarrierCode);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cycle")]
    [InlineData("foreign-owner")]
    [InlineData("foreign-document")]
    [InlineData("foreign-frame")]
    [InlineData("unknown-document")]
    [InlineData("unknown-frame")]
    [InlineData("root-boundary")]
    [InlineData("single")]
    [InlineData("unknown-mode")]
    [InlineData("unknown-selection")]
    [InlineData("inconsistent-selection")]
    [InlineData("stale-descendant")]
    [InlineData("absent-descendant")]
    public async Task Activation_UncertainStaleOrForeignOwnership_IsUnsupportedWithoutInput(string scenario)
    {
        var owner = Owner();
        var option = Option("a", false);
        owner = scenario switch
        {
            "single" => owner with { MultiSelectable = new(false, SemanticSource.ChromiumAccessibility) },
            "unknown-mode" => owner with { MultiSelectable = null },
            "stale-descendant" => owner with { ActiveDescendantId = "removed" },
            "absent-descendant" => owner with { ActiveDescendantId = null },
            _ => owner
        };
        option = scenario switch
        {
            "missing" => option with { ParentId = "missing" },
            "cycle" => option with { ParentId = "a" },
            "foreign-owner" => option with { ParentId = "other" },
            "foreign-document" => option with { DocumentId = "other" },
            "foreign-frame" => option with { FrameId = "other" },
            "unknown-document" => option with { DocumentId = null },
            "unknown-frame" => option with { FrameId = null },
            "root-boundary" => option with { ParentId = "root" },
            "unknown-selection" => option with { Selected = null, Selection = null },
            "inconsistent-selection" => option with { Selection = new(true, SemanticSource.ChromiumAccessibility) },
            _ => option
        };
        var browser = new ListBrowser(Snapshot(owner, option, Owner("other"),
            new("root", "RootWebArea", "Child", ParentId: "list", DocumentId: "doc", FrameId: "frame")));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.False(step.InputMayHaveOccurred);
        Assert.Null(step.BarrierCode);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cycle")]
    [InlineData("foreign-document")]
    [InlineData("foreign-frame")]
    public async Task SelectionContext_ExcludesOptionsWithoutKnownLocalOwnership(string scenario)
    {
        var invalid = Option("bad", true) with
        {
            ParentId = scenario == "missing" ? "absent" : scenario == "cycle" ? "bad" : "list",
            DocumentId = scenario == "foreign-document" ? "foreign" : "doc",
            FrameId = scenario == "foreign-frame" ? "foreign" : "frame"
        };
        var snapshot = Snapshot(Owner(), Option("a", true), invalid);
        var context = ListboxSelectionContext.Observe(snapshot, snapshot.Nodes[0]);
        Assert.Equal("a", Assert.Single(context!.Options).NodeId);
    }

    [Theory]
    [InlineData("foreign-group")]
    [InlineData("group-cycle")]
    [InlineData("nested-option")]
    [InlineData("root-boundary")]
    public void SelectionContext_RejectsAmbiguousIntermediateAncestors(string scenario)
    {
        var group = new AccessibleNode("group", scenario == "nested-option" ? "option"
            : scenario == "root-boundary" ? "RootWebArea" : "group", "",
            ParentId: scenario == "group-cycle" ? "group" : "list",
            DocumentId: scenario == "foreign-group" ? "foreign" : "doc", FrameId: "frame");
        var snapshot = Snapshot(Owner(), Option("a", true), group, Option("bad", true, "group"));
        var context = ListboxSelectionContext.Observe(snapshot, snapshot.Nodes[0]);
        Assert.DoesNotContain(context!.Options, option => option.NodeId == "bad");
        Assert.Null(ListboxSelectionContext.Observe(snapshot, snapshot.Nodes[^1]));
    }

    [Fact]
    public async Task FreshFocusedRead_EmitsSelectionContextWithoutInventingBrowseCursor()
    {
        var browser = new ListBrowser(Snapshot(Owner(), Option("a", true)));
        using var reader = new ScreenReaderSession(browser);
        var read = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Null(read.CursorNodeId);
        Assert.Equal("list", read.ListboxSelection?.ListboxNodeId);
        var announcement = reader.Timeline.Last(e => e.Kind == ReaderEventKind.Announcement);
        Assert.Null(announcement.NodeId);
        Assert.Equal("a", Assert.Single(announcement.ListboxSelection!.Options).NodeId);
    }

    [Theory]
    [InlineData("extra-selected")]
    [InlineData("missing-selected")]
    [InlineData("duplicate-goal")]
    [InlineData("unknown-selection")]
    [InlineData("new-document")]
    [InlineData("new-frame")]
    [InlineData("other-owner")]
    public void ExposedSetGoal_RequiresExactKnownStateAndLiveIdentity(string scenario)
    {
        var owner = Owner();
        var option = Option("a", true);
        var ids = ImmutableArray.Create("a");
        if (scenario == "missing-selected") ids = ["absent"];
        if (scenario == "duplicate-goal") ids = ["a", "a"];
        if (scenario == "unknown-selection") option = option with { Selection = null };
        if (scenario == "new-document") owner = owner with { DocumentId = "new" };
        if (scenario == "new-frame") owner = owner with { FrameId = "new" };
        var snapshot = Snapshot(owner, option, Option("b", scenario == "extra-selected"));
        var expected = new ExposedSelectionExpectation(scenario == "other-owner" ? "other" : "list", "doc", "frame", ids);
        Assert.False(new ObservationExpectation("listbox", "list", ExposedSelection: expected).IsSatisfiedBy(snapshot));
    }

    [Fact]
    public void ExposedSetGoal_EmptyAndNonemptySetsVerifyWithoutClaimingHiddenSelection()
    {
        var snapshot = Snapshot(Owner(), Option("a", true), Option("b", false));
        Assert.True(Goal("a", "a").IsSatisfiedBy(snapshot));
        snapshot = snapshot with { Nodes = snapshot.Nodes.Select(n => n.Id == "a" ? Option("a", false) : n).ToImmutableArray() };
        Assert.True(Goal("a").IsSatisfiedBy(snapshot));
        Assert.False(Goal("a", "hidden").IsSatisfiedBy(snapshot));
    }

    [Fact]
    public async Task UnchangedSelection_TimesOutAsUnknownWithoutRepeatingSpace()
    {
        var browser = new ListBrowser(Snapshot(Owner(), Option("a", false)));
        using var reader = new ScreenReaderSession(browser, waitOptions: new() { MaxPolls = 1 });
        var result = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: Goal("a", "a"));
        Assert.Equal(ReaderOutcome.Unknown, result.Outcome);
        Assert.Equal(VerificationStatus.TimedOut, result.Verification);
        Assert.True(result.InputMayHaveOccurred);
        Assert.Equal(1, result.BrowserKeystrokes);
        Assert.Equal(new[] { "Space" }, browser.Keys);
        Assert.Null(result.BarrierCode);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
    }

    [Fact]
    public async Task RemovedOption_ClearsBrowseCursorAndCannotBeActivatedByStaleIdentity()
    {
        var browser = new ListBrowser(Snapshot(Owner(), Option("a", true), Option("b", false)));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextListbox);
        await reader.ExecuteAsync(ReaderCommand.NextOption);
        browser.Snapshot = Snapshot(Owner() with { ActiveDescendantId = "a" }, Option("b", false));
        var read = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Null(read.CursorNodeId);
        Assert.Equal("b", Assert.Single(read.ListboxSelection!.Options).NodeId);
        Assert.Equal(ReaderOutcome.Unsupported, (await reader.ExecuteAsync(ReaderCommand.Activate)).Outcome);
        Assert.Empty(browser.Keys);
    }

    private static ObservationExpectation Goal(string name, params string[] ids) =>
        new("option", name, MustBeFocused: true, ExposedSelection: new("list", "doc", "frame", ids.ToImmutableArray()));

    private static AccessibleNode Owner(string id = "list") => new(id, "listbox", id, IsFocused: id == "list",
        ActiveDescendantId: id == "list" ? "a" : null, DocumentId: "doc", FrameId: "frame",
        MultiSelectable: new(true, SemanticSource.ChromiumAccessibility));

    private static AccessibleNode Option(string id, bool? selected, string parent = "list") =>
        new(id, "option", id, Selected: selected, ParentId: parent, DocumentId: "doc", FrameId: "frame",
            Selection: selected is bool value ? new(value, SemanticSource.ChromiumAccessibility) : null);

    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) => new("doc", nodes.ToImmutableArray(), []);

    private sealed class ListBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public List<string> Keys { get; } = [];
        public Action<string>? OnKey { get; set; }
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            OnKey?.Invoke(key);
            return Task.CompletedTask;
        }
        public void Replace(string id, Func<AccessibleNode, AccessibleNode> replace) =>
            Snapshot = Snapshot with { Nodes = Snapshot.Nodes.Select(node => node.Id == id ? replace(node) : node).ToImmutableArray() };
    }
}
