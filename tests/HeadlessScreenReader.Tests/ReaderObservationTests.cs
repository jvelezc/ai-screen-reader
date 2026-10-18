using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderObservationTests
{
    [Fact]
    public async Task Context_ExposedRadioPeers_PreservesGroupAndScopesCount()
    {
        var browser = new ObservedBrowser(Snapshot(
            new("main", "main", ""),
            new("speed", "group", "Delivery speed", ParentId: "main"),
            new("label", "generic", "", ParentId: "speed"),
            new("standard", "radio", "Standard", ParentId: "label"),
            new("express", "radio", "Express", IsFocused: true, Checked: "true", ParentId: "speed"),
            new("other", "group", "Other choices", ParentId: "speed"),
            new("other-radio", "radio", "Other", ParentId: "other")));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        var context = Assert.IsType<ReaderContext>(step.Context);
        Assert.Equal("express", context.NodeId);
        Assert.Equal(["main", "speed"], context.Ancestors.Select(item => item.NodeId));
        Assert.Equal("Delivery speed", context.Ancestors[^1].Name);
        Assert.True(context.AncestryComplete);
        Assert.Equal(new ExposedPeerPosition("speed", 2, 2), context.ExposedPeers);
        Assert.Equal("Express, radio, checked", step.Announcement);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task Context_ActiveOption_UsesListboxInsteadOfDomOwnerGroup()
    {
        var browser = new ObservedBrowser(Snapshot(
            new("main", "main", ""),
            new("combo", "combobox", "Region", IsFocused: true, ParentId: "main", ActiveDescendantId: "south"),
            new("list", "listbox", "Delivery regions", ParentId: "main"),
            new("north", "option", "North", ParentId: "list"),
            new("south", "option", "South", ParentId: "list", Selected: true)));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("combo", step.FocusedNodeId);
        Assert.Equal("south", step.Context?.NodeId);
        Assert.Equal("Delivery regions", step.Context?.Ancestors[^1].Name);
        Assert.Equal(new ExposedPeerPosition("list", 2, 2), step.Context?.ExposedPeers);
        Assert.Equal("South, option, selected", step.Announcement);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_BrokenOrCyclicAncestry_DoesNotInventCompletePeerSet(bool cycle)
    {
        var browser = new ObservedBrowser(Snapshot(
            new("group", "group", "Choices", ParentId: cycle ? "current" : "missing"),
            new("current", "radio", "Current", IsFocused: true, ParentId: "group")));
        using var reader = new ScreenReaderSession(browser);
        var context = (await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Context;
        Assert.NotNull(context);
        Assert.False(context.AncestryComplete);
        Assert.Null(context.ExposedPeers);
        Assert.Equal("group", Assert.Single(context.Ancestors).NodeId);
    }

    [Fact]
    public async Task Context_UngroupedControlAndEmptySnapshot_DoNotInventContext()
    {
        var browser = new ObservedBrowser(Snapshot(new AccessibleNode("check", "checkbox", "Terms", IsFocused: true)));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Empty(Assert.IsType<ReaderContext>(step.Context).Ancestors);
        Assert.Null(step.Context.ExposedPeers);
        browser.Snapshot = Snapshot();
        Assert.Null((await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Context);
    }

    [Fact]
    public async Task Context_SingleExposedOption_ReportsOnlyOneObservedPeer()
    {
        var browser = new ObservedBrowser(Snapshot(new("list", "listbox", "Loaded choices"),
            new("only", "option", "Visible item", IsFocused: true, ParentId: "list")));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(new ExposedPeerPosition("list", 1, 1), step.Context?.ExposedPeers);
        Assert.Equal("Visible item, option", step.Announcement);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task Context_VerifiedDelayedFocus_ReturnsDestinationAncestry()
    {
        var clock = new FakeTimeProvider();
        var browser = new ObservedBrowser(Snapshot(new AccessibleNode("open", "button", "Open", IsFocused: true)));
        using var reader = new ScreenReaderSession(browser, timeProvider: clock);
        var pending = reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("button", "Close", MustBeFocused: true));
        Assert.False(pending.IsCompleted);
        browser.Snapshot = Snapshot(new AccessibleNode("dialog", "dialog", "Review"),
            new("close", "button", "Close", IsFocused: true, ParentId: "dialog"));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var step = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.Equal("close", step.Context?.NodeId);
        Assert.Equal("Review", Assert.Single(step.Context!.Ancestors).Name);
        Assert.Equal(["Enter"], browser.Keys);
    }

    [Fact]
    public async Task LiveChanges_ChangedStatus_IsSeparateFromFocusedAnnouncementAndDeduplicated()
    {
        var browser = new ObservedBrowser(LiveSnapshot("Ready"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        browser.Snapshot = LiveSnapshot("Delivery region: South");
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        var change = Assert.Single(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        Assert.Equal("status", change.NodeId);
        Assert.Equal("Delivery region: South", change.Detail);
        Assert.Equal(new LiveRegionObservation("polite", true, false, change.Detail), change.LiveRegion);
        Assert.Equal(step.Sequence, change.CommandSequence);
        Assert.Equal("Save, button", step.Announcement);
        Assert.Empty(browser.Keys);
        Assert.True(change.Sequence < reader.Timeline.First(item => item.CommandSequence == step.Sequence
            && item.Kind == ReaderEventKind.Announcement).Sequence);
    }

    [Fact]
    public async Task LiveChanges_NewAssertiveAlert_RecordsOneObservation()
    {
        var browser = new ObservedBrowser(Snapshot(new AccessibleNode("save", "button", "Save", IsFocused: true)));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = Snapshot(new AccessibleNode("error", "alert", "Accept terms", IsFocused: true,
            LiveRegion: new("assertive", true, false, "Accept terms")));
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("Accept terms, alert", step.Announcement);
        Assert.Equal("assertive", Assert.Single(reader.Timeline,
            item => item.Kind == ReaderEventKind.LiveRegionChanged).LiveRegion?.Politeness);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData("off", false, "Changed")]
    [InlineData("polite", true, "Changed")]
    [InlineData("polite", false, "")]
    [InlineData("polite", false, "Ready")]
    public async Task LiveChanges_OffBusyEmptyOrUnchanged_DoesNotEmit(string politeness, bool busy, string text)
    {
        var browser = new ObservedBrowser(LiveSnapshot("Ready"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = LiveSnapshot(text, politeness, busy);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task LiveChanges_BusyRegionSettles_EmitsLatestTextOnce()
    {
        var browser = new ObservedBrowser(LiveSnapshot("Ready"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = LiveSnapshot("Half done", busy: true);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = LiveSnapshot("Done", busy: true);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = LiveSnapshot("Done");
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("Done", Assert.Single(reader.Timeline,
            item => item.Kind == ReaderEventKind.LiveRegionChanged).Detail);
    }

    [Fact]
    public async Task LiveChanges_OffThenPolite_DoesNotReplayExistingText()
    {
        var browser = new ObservedBrowser(LiveSnapshot("Ready"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = LiveSnapshot("Quiet change", "off");
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = LiveSnapshot("Quiet change");
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        browser.Snapshot = LiveSnapshot("New change");
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("New change", Assert.Single(reader.Timeline,
            item => item.Kind == ReaderEventKind.LiveRegionChanged).Detail);
    }

    [Fact]
    public async Task LiveChanges_RemovedThenReinserted_ObservesInsertionButNotDocumentReplacement()
    {
        var browser = new ObservedBrowser(LiveSnapshot("Ready"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = Snapshot();
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = LiveSnapshot("Ready");
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        browser.Snapshot = LiveSnapshot("Another page") with { DocumentId = "next-document" };
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        var change = Assert.Single(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        Assert.Equal("Ready", change.Detail);
        Assert.Equal("document", change.DocumentId);
    }

    private static AccessibilitySnapshot LiveSnapshot(string text, string politeness = "polite", bool busy = false) =>
        Snapshot(new AccessibleNode("save", "button", "Save", IsFocused: true),
            new("status", "status", "", LiveRegion: new(politeness, true, busy, text)));

    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) => new("document", nodes.ToImmutableArray(), []);

    private sealed class ObservedBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public List<string> Keys { get; } = [];
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            return Task.CompletedTask;
        }
    }
}
