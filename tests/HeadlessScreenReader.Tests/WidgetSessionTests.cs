using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class WidgetSessionTests
{
    [Fact]
    public async Task ActiveDescendant_AnnouncesOptionButActivatesDomOwner()
    {
        var browser = new ControlledBrowser(Snapshot(
            new("combo", "combobox", "Region", IsFocused: true, Expanded: true, ActiveDescendantId: "south"),
            new("south", "option", "South", Selected: true)));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var read = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("South, option, selected", read.Announcement);
        Assert.Equal("combo", read.FocusedNodeId);
        Assert.Equal("south", read.ActiveDescendantNodeId);
        var activation = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(["Enter"], browser.Keys);
        Assert.Equal(ReaderOutcome.Observed, activation.Outcome);
    }

    [Fact]
    public void ActiveDescendant_MissingTarget_FallsBackToDomOwner()
    {
        var snapshot = Snapshot(new AccessibleNode("combo", "combobox", "Region", IsFocused: true,
            ActiveDescendantId: "removed"));
        Assert.Same(snapshot.FocusedNode, snapshot.EffectiveFocusedNode);
        Assert.False(new ObservationExpectation("option", "South", MustBeFocused: true).IsSatisfiedBy(snapshot));
    }

    [Fact]
    public async Task FocusMode_ExternalActiveDescendantChange_SynchronizesAnnouncedCursor()
    {
        var browser = new ControlledBrowser(Snapshot(
            new("combo", "combobox", "Region", IsFocused: true, ActiveDescendantId: "north"),
            new("north", "option", "North", Selected: true),
            new("south", "option", "South")));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        browser.Snapshot = Snapshot(
            new("combo", "combobox", "Region", IsFocused: true, ActiveDescendantId: "south"),
            new("north", "option", "North"),
            new("south", "option", "South", Selected: true));
        var result = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("South, option, selected", result.Announcement);
        Assert.Equal("combo", result.FocusedNodeId);
        Assert.Equal("south", result.ActiveDescendantNodeId);
        Assert.Equal("south", result.CursorNodeId);
        Assert.Equal(0, result.BrowserKeystrokes);
        Assert.Empty(browser.Keys);
        Assert.Equal("south", reader.Timeline.Last(item => item.Kind == ReaderEventKind.Announcement).NodeId);
    }

    [Fact]
    public async Task Wait_DelayedFocus_VerifiesDestinationAndAnnouncesNewHeading()
    {
        var clock = new FakeTimeProvider();
        var browser = new ControlledBrowser();
        using var reader = new ScreenReaderSession(browser, timeProvider: clock);
        var pending = reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("heading", "Saved", MustBeFocused: true));
        Assert.False(pending.IsCompleted);
        browser.Snapshot = Snapshot(new AccessibleNode("saved", "heading", "Saved", IsFocused: true, HeadingLevel: 2));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(VerificationStatus.Satisfied, result.Verification);
        Assert.Equal("Saved, heading level 2", result.Announcement);
        Assert.Equal("saved", result.CursorNodeId);
        Assert.Equal(1, result.BrowserKeystrokes);
        Assert.Equal(["Enter"], browser.Keys);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.GoalVerified);
    }

    [Fact]
    public async Task Wait_Deadline_ReturnsUnknownAndPreservesCompletedInput()
    {
        var clock = new FakeTimeProvider();
        var browser = new ControlledBrowser();
        using var reader = new ScreenReaderSession(browser, timeProvider: clock,
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(100) });
        var pending = reader.ExecuteAsync(ReaderCommand.Activate, expectation: new("heading", "Missing"));
        clock.Advance(TimeSpan.FromMilliseconds(99));
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ReaderOutcome.Unknown, result.Outcome);
        Assert.Equal(VerificationStatus.TimedOut, result.Verification);
        Assert.True(result.InputMayHaveOccurred);
        Assert.Equal(1, result.BrowserKeystrokes);
        Assert.Null(result.BarrierCode);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.GoalVerified);
    }

    [Fact]
    public async Task Wait_PollLimit_StopsWithoutAdditionalInput()
    {
        var clock = new FakeTimeProvider();
        var browser = new ControlledBrowser();
        using var reader = new ScreenReaderSession(browser, timeProvider: clock,
            waitOptions: new() { MaxPolls = 2 });
        var pending = reader.ExecuteAsync(ReaderCommand.Activate, expectation: new("heading", "Missing"));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(VerificationStatus.TimedOut, result.Verification);
        Assert.Equal(3, browser.Captures); // Before input, after input, one poll.
        Assert.Single(browser.Keys);
    }

    [Fact]
    public async Task Wait_CancelledAfterInput_ReportsInterruptedWithoutInventingBarrier()
    {
        var clock = new FakeTimeProvider();
        var browser = new ControlledBrowser();
        using var reader = new ScreenReaderSession(browser, timeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var pending = reader.ExecuteAsync(ReaderCommand.Activate, cancellationToken: cancellation.Token,
            expectation: new("heading", "Missing"));
        cancellation.Cancel();
        var result = await pending;
        Assert.Equal(ReaderOutcome.Unknown, result.Outcome);
        Assert.Equal(VerificationStatus.Interrupted, result.Verification);
        Assert.True(result.InputMayHaveOccurred);
        Assert.Equal(1, result.BrowserKeystrokes);
        Assert.Null(result.BarrierCode);
        Assert.Single(browser.Keys);
    }

    [Fact]
    public async Task Timeline_OrdersInputObservationVerificationAndAnnouncement()
    {
        var clock = new FakeTimeProvider();
        var browser = new ControlledBrowser();
        browser.AfterKey = () =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(10));
            browser.Snapshot = Snapshot(new AccessibleNode("saved", "heading", "Saved", IsFocused: true));
        };
        using var reader = new ScreenReaderSession(browser, timeProvider: clock);
        await reader.ExecuteAsync(ReaderCommand.Activate, expectation: new("heading", "Saved"));
        var timeline = reader.Timeline;
        Assert.Equal(Enumerable.Range(1, timeline.Length).Select(value => (long)value), timeline.Select(item => item.Sequence));
        Assert.All(timeline, item => Assert.Equal(1, item.CommandSequence));
        Assert.Equal(timeline.Select(item => item.ElapsedMilliseconds).Order(), timeline.Select(item => item.ElapsedMilliseconds));
        var kinds = timeline.Select(item => item.Kind).ToArray();
        Assert.Equal(ReaderEventKind.CommandStarted, kinds[0]);
        Assert.True(Array.IndexOf(kinds, ReaderEventKind.InputAttempted) < Array.IndexOf(kinds, ReaderEventKind.InputCompleted));
        var afterInputObservation = Array.LastIndexOf(kinds, ReaderEventKind.Observation);
        Assert.True(Array.IndexOf(kinds, ReaderEventKind.InputCompleted) < afterInputObservation);
        Assert.True(afterInputObservation < Array.IndexOf(kinds, ReaderEventKind.GoalVerified));
        Assert.Equal(ReaderEventKind.Announcement, kinds[^2]);
        Assert.Equal(ReaderEventKind.CommandCompleted, kinds[^1]);
        Assert.Equal(10, timeline[^1].ElapsedMilliseconds);
    }

    [Fact]
    public async Task Timeline_AtCapacity_ReportsTruncationAndKeepsCommandResult()
    {
        using var reader = new ScreenReaderSession(new ControlledBrowser(), maxTimelineEvents: 2);
        var result = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.Observed, result.Outcome);
        Assert.Equal(1, result.BrowserKeystrokes);
        Assert.Equal(2, reader.Timeline.Length);
        Assert.True(reader.TimelineTruncated);
    }

    [Fact]
    public async Task InputFailure_RecordsAttemptWithoutInventingCompletionOrBarrier()
    {
        var browser = new ControlledBrowser();
        browser.AfterKey = () => throw new BrowserOperationException("CONTROLLED_INPUT_FAILURE");
        using var reader = new ScreenReaderSession(browser);
        var result = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: new("heading", "Saved"));
        Assert.Equal(ReaderOutcome.BrowserError, result.Outcome);
        Assert.Equal(0, result.BrowserKeystrokes);
        Assert.True(result.InputMayHaveOccurred);
        Assert.Null(result.BarrierCode);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind is ReaderEventKind.InputCompleted or ReaderEventKind.GoalVerified);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.BrowserError);
    }

    [Fact]
    public void Expectation_RequiresRequestedStateAndEffectiveFocus()
    {
        var snapshot = Snapshot(new AccessibleNode("check", "checkbox", "Terms", Checked: "true"));
        Assert.True(new ObservationExpectation("checkbox", "Terms", Checked: "true").IsSatisfiedBy(snapshot));
        Assert.False(new ObservationExpectation("checkbox", "Terms", Checked: "false").IsSatisfiedBy(snapshot));
        Assert.False(new ObservationExpectation("checkbox", "Terms", MustBeFocused: true).IsSatisfiedBy(snapshot));
    }

    [Fact]
    public async Task WidgetAnnouncement_IncludesValidationAndDescription()
    {
        var browser = new ControlledBrowser(Snapshot(new AccessibleNode("check", "checkbox", "Terms", IsFocused: true,
            Checked: "false", IsRequired: true, Invalid: "true", Description: "Needed to save.")));
        using var reader = new ScreenReaderSession(browser);
        var result = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("Terms, checkbox, not checked, required, invalid. Needed to save.", result.Announcement);
    }

    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) =>
        new("controlled-document", nodes.ToImmutableArray(), []);

    private sealed class ControlledBrowser(AccessibilitySnapshot? snapshot = null) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot
            ?? WidgetSessionTests.Snapshot(new AccessibleNode("save", "button", "Save", IsFocused: true));
        public List<string> Keys { get; } = [];
        public Action? AfterKey { get; set; }
        public int Captures { get; private set; }

        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Captures++;
            return Task.FromResult(Snapshot);
        }

        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            AfterKey?.Invoke();
            return Task.CompletedTask;
        }
    }
}

