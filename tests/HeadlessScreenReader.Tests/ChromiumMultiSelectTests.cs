using System.Collections.Immutable;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumMultiSelectTests
{
    [Fact]
    public async Task MultiSelectCapture_Chromium_PreservesSourcesAndExcludesHiddenSelectedOption()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Multiselect);
        var snapshot = await browser.CaptureAsync();
        var fruit = Named(snapshot, "listbox", "Fruit");
        Assert.Equal(new(true, SemanticSource.ChromiumAccessibility), fruit.MultiSelectable);
        Assert.Equal(new(false, SemanticSource.ChromiumAccessibility), Named(snapshot, "listbox", "Single choice").MultiSelectable);
        Assert.Equal(new(true, SemanticSource.ChromiumAccessibility), Named(snapshot, "option", "Apples").Selection);
        Assert.Equal(new(false, SemanticSource.ChromiumAccessibility), Named(snapshot, "option", "Pears").Selection);
        Assert.DoesNotContain(snapshot.Nodes, n => n.Name == "Hidden selected fruit");
        var context = ListboxSelectionContext.Observe(snapshot, fruit);
        Assert.True(context!.ExposedOptionsOnly);
        Assert.Equal(new[] { "Apples", "Pears", "Plums" }, context.Options.Select(o => o.Name));
        Assert.Equal("Apples", Assert.Single(context.Options.Where(o => o.Selection?.Value == true)).Name);
        Assert.True(context.Options.Single(o => o.Name == "Plums").IsDisabled);
        Assert.Equal(fruit.DocumentId, context.DocumentId);
        Assert.Equal(fruit.FrameId, context.FrameId);
    }

    [Fact]
    public async Task OptionBrowse_Chromium_IsScopedNonwrappingAndLeavesFocusAndSelectionUntouched()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Multiselect);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var before = await browser.CaptureAsync();
        await reader.ExecuteAsync(ReaderCommand.EnterBrowseMode);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var listbox = await reader.ExecuteAsync(ReaderCommand.NextListbox);
        Assert.Equal(Named(before, "listbox", "Fruit").Id, listbox.CursorNodeId);
        var apple = await reader.ExecuteAsync(ReaderCommand.NextOption);
        var pear = await reader.ExecuteAsync(ReaderCommand.NextOption);
        var plum = await reader.ExecuteAsync(ReaderCommand.NextOption);
        var boundary = await reader.ExecuteAsync(ReaderCommand.NextOption);
        Assert.Contains("Pears, option, not selected", pear.Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, boundary.Outcome);
        Assert.Equal(plum.CursorNodeId, boundary.CursorNodeId);
        Assert.Equal(pear.CursorNodeId, (await reader.ExecuteAsync(ReaderCommand.PreviousOption)).CursorNodeId);
        Assert.Equal(apple.CursorNodeId, (await reader.ExecuteAsync(ReaderCommand.PreviousOption)).CursorNodeId);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousOption)).Outcome);
        var after = await browser.CaptureAsync();
        Assert.Equal(before.FocusedNode?.Id, after.FocusedNode?.Id);
        Assert.Equal(before.EffectiveFocusedNode?.Id, after.EffectiveFocusedNode?.Id);
        Assert.Equal(before.Nodes.Where(n => n.Role == "option").Select(n => (n.Id, n.Selected)),
            after.Nodes.Where(n => n.Role == "option").Select(n => (n.Id, n.Selected)));
        Assert.Single(reader.Timeline.Where(e => e.Kind == ReaderEventKind.InputAttempted)); // Initial Tab only.
    }

    [Fact]
    public async Task ActiveDescendantSelection_Chromium_SelectsTwoThenDeselectsOneThroughActualOwner()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Multiselect);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var snapshot = await browser.CaptureAsync();
        var owner = Named(snapshot, "listbox", "Fruit");
        var apple = Named(snapshot, "option", "Apples");
        var pear = Named(snapshot, "option", "Pears");
        var moved = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        Assert.Equal(owner.Id, moved.FocusedNodeId);
        Assert.Equal(pear.Id, moved.ActiveDescendantNodeId);
        Assert.Equal(pear.Id, moved.CursorNodeId);
        Assert.False(moved.ListboxSelection!.Options.Single(o => o.NodeId == pear.Id).Selection!.Value);
        var selected = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: Goal(owner, "Pears", apple.Id, pear.Id));
        Assert.Equal(VerificationStatus.Satisfied, selected.Verification);
        Assert.Equal(new[] { "Apples", "Pears" }, selected.ListboxSelection!.Options.Where(o => o.Selection!.Value).Select(o => o.Name));
        var deselected = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: Goal(owner, "Pears", apple.Id));
        Assert.Equal(VerificationStatus.Satisfied, deselected.Verification);
        Assert.Equal(owner.Id, deselected.FocusedNodeId);
        Assert.Equal(pear.Id, deselected.CursorNodeId);
        Assert.Equal("Apples", Assert.Single(deselected.ListboxSelection!.Options.Where(o => o.Selection!.Value)).Name);
        var attempts = reader.Timeline.Where(e => e.Kind == ReaderEventKind.InputAttempted && e.Command == ReaderCommand.Activate).ToArray();
        Assert.Equal(new[] { "Space", "Space" }, attempts.Select(e => e.Detail));
        Assert.All(attempts, e => Assert.Equal(owner.Id, e.NodeId));
        Assert.Contains(reader.Timeline, e => e.Kind == ReaderEventKind.WidgetStateChanged && e.NodeId == pear.Id
            && e.ListboxSelection!.Options.Single(o => o.NodeId == pear.Id).Selection?.Value == true);
    }

    [Fact]
    public async Task RovingSelection_Chromium_MovesFocusWithoutSelectionThenTogglesFocusedOption()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Multiselect);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var first = await reader.ExecuteAsync(ReaderCommand.Tab);
        var snapshot = await browser.CaptureAsync();
        var owner = Named(snapshot, "listbox", "Vegetables");
        var carrot = Named(snapshot, "option", "Carrots");
        var peas = Named(snapshot, "option", "Peas");
        Assert.Equal(ReaderMode.Focus, first.Mode);
        Assert.Equal(carrot.Id, first.FocusedNodeId);
        Assert.Null(first.ActiveDescendantNodeId);
        var selected = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: Goal(owner, "Carrots", carrot.Id));
        Assert.Equal(VerificationStatus.Satisfied, selected.Verification);
        var moved = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        Assert.Equal(peas.Id, moved.FocusedNodeId);
        Assert.Equal(peas.Id, moved.CursorNodeId);
        Assert.False(moved.ListboxSelection!.Options.Single(o => o.NodeId == peas.Id).Selection!.Value);
        Assert.True(moved.ListboxSelection.Options.Single(o => o.NodeId == carrot.Id).Selection!.Value);
        Assert.Equal(VerificationStatus.Satisfied, (await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: Goal(owner, "Peas", carrot.Id, peas.Id))).Verification);
        var deselected = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: Goal(owner, "Peas", carrot.Id));
        Assert.Equal(VerificationStatus.Satisfied, deselected.Verification);
        Assert.Equal(carrot.Id, Assert.Single(deselected.ListboxSelection!.Options.Where(o => o.Selection!.Value)).NodeId);
    }

    [Fact]
    public async Task DisabledSelection_Chromium_IsBlockedAndBrowseActivationRequiresFocus()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Multiselect);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.EnterBrowseMode);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        await reader.ExecuteAsync(ReaderCommand.NextListbox);
        await reader.ExecuteAsync(ReaderCommand.NextOption);
        await reader.ExecuteAsync(ReaderCommand.NextOption);
        var required = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.FocusRequired, required.Outcome);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        await reader.ExecuteAsync(ReaderCommand.PressKey, "End");
        var blocked = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.Blocked, blocked.Outcome);
        Assert.Equal("OBSERVED_DISABLED_CONTROL", blocked.BarrierCode);
        Assert.Equal(0, blocked.BrowserKeystrokes);
        Assert.DoesNotContain(reader.Timeline, e => e.Command == ReaderCommand.Activate && e.Kind == ReaderEventKind.InputAttempted);
        var snapshot = await browser.CaptureAsync();
        Assert.False(Named(snapshot, "option", "Plums").Selected);
        Assert.True(Named(snapshot, "option", "Apples").Selected);
    }

    [Fact]
    public async Task SingleChoiceActivation_Chromium_RemainsUnsupportedAndDoesNotChangeSelection()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Multiselect);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.False(step.ListboxSelection!.MultiSelectable!.Value);
        Assert.True(Named(await browser.CaptureAsync(), "option", "Red").Selected);
    }

    [Fact]
    public async Task ExternalRemoval_Chromium_DropsStaleOptionAndRefusesToRepairActiveDescendant()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Multiselect);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var pear = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        // External keyboard operation activates the fixture's removal button.
        for (var i = 0; i < 4; i++) await browser.PressKeyAsync("Tab");
        await browser.PressKeyAsync("Enter");
        for (var i = 0; i < 4; i++) await browser.PressKeyAsync("Shift+Tab");
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.DoesNotContain(step.ListboxSelection!.Options, o => o.NodeId == pear.CursorNodeId);
        Assert.DoesNotContain((await browser.CaptureAsync()).Nodes, n => n.Name == "Pears");
        Assert.DoesNotContain(reader.Timeline, e => e.Command == ReaderCommand.Activate && e.Kind == ReaderEventKind.InputAttempted);
    }

    private static ObservationExpectation Goal(AccessibleNode listbox, string name, params string[] ids) =>
        new("option", name, MustBeFocused: true, ExposedSelection: new(listbox.Id, listbox.DocumentId!, listbox.FrameId!, ids.ToImmutableArray()));

    private static AccessibleNode Named(AccessibilitySnapshot snapshot, string role, string name) =>
        Assert.Single(snapshot.Nodes, node => node.Role == role && node.Name == name);
}
