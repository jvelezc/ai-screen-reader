using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderGapTests
{
    [Theory]
    [InlineData(ReaderCommand.PreviousHeading, "h1")]
    [InlineData(ReaderCommand.PreviousLink, "link")]
    [InlineData(ReaderCommand.PreviousButton, "button")]
    [InlineData(ReaderCommand.PreviousLandmark, "form")]
    public async Task BackwardNavigation_FromEnd_FindsRoleWithoutInput(ReaderCommand command, string expectedId)
    {
        var browser = new GapBrowser(Snapshot(
            new("h1", "heading", "First", HeadingLevel: 1), new("link", "link", "Details"),
            new("button", "button", "Open"), new("form", "form", "Preferences"), new("end", "StaticText", "End")));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        var step = await reader.ExecuteAsync(command);
        Assert.Equal(expectedId, step.CursorNodeId);
        Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
        var atStart = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.NoMatch, atStart.Outcome);
        Assert.Equal(expectedId, atStart.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task DocumentBoundaries_EmptyAndFocusMode_DoNotInventMovement()
    {
        var browser = new GapBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.DocumentStart)).Outcome);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.DocumentEnd)).Outcome);
        browser.Snapshot = Snapshot(new AccessibleNode("focus", "button", "Focused", IsFocused: true));
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(ReaderOutcome.Unsupported, (await reader.ExecuteAsync(ReaderCommand.DocumentStart)).Outcome);
        Assert.Equal(ReaderOutcome.Unsupported, (await reader.ExecuteAsync(ReaderCommand.PreviousButton)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task BackwardNavigation_UnsetCursor_StartsAtLastMatchWithoutWrapping()
    {
        var browser = new GapBrowser(Snapshot(new("first", "heading", "First"), new("last", "heading", "Last")));
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal("last", (await reader.ExecuteAsync(ReaderCommand.PreviousHeading)).CursorNodeId);
        Assert.Equal("first", (await reader.ExecuteAsync(ReaderCommand.PreviousHeading)).CursorNodeId);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousHeading)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task RequiredObservation_ExplicitFalseOverridesLegacyAndMissingRemainsUnknown()
    {
        var browser = new GapBrowser(Snapshot(new AccessibleNode("choice", "checkbox", "Choice", IsFocused: true,
            IsRequired: true, Required: new(false, SemanticSource.NativeHtmlProperty))));
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal("Choice, checkbox", (await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Announcement);
        browser.Snapshot = Snapshot(new AccessibleNode("choice", "checkbox", "Choice", IsFocused: true,
            Required: new(true, SemanticSource.NativeHtmlProperty)));
        Assert.Equal("Choice, checkbox, required", (await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Announcement);
        browser.Snapshot = Snapshot(new AccessibleNode("choice", "checkbox", "Choice", IsFocused: true));
        Assert.Null(browser.Snapshot.FocusedNode?.Required);
        Assert.Equal("Choice, checkbox", (await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Announcement);
    }

    [Theory]
    [InlineData(20, true, "Fifth, option, item 5 of 20")]
    [InlineData(-1, true, "Fifth, option, item 5, set size unknown")]
    [InlineData(2, false, "Fifth, option")]
    public async Task LogicalSet_DeclaredValidityControlsAnnouncement(int size, bool valid, string expected)
    {
        var declared = new LogicalSetPosition(5, size, valid, SemanticSource.DeclaredAria);
        var browser = new GapBrowser(Snapshot(new AccessibleNode("fifth", "option", "Fifth", IsFocused: true, LogicalSet: declared)));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(expected, step.Announcement);
        Assert.Equal(declared, step.Context?.LogicalSet);
        Assert.Null(step.Context?.ExposedPeers);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task IdleObservation_ChangedStatus_StreamsWithoutCommandsOrInputAndStopsAtLimit()
    {
        var clock = new FakeTimeProvider();
        var browser = new GapBrowser(Live("Ready"));
        using var reader = new ScreenReaderSession(browser, timeProvider: clock);
        await using var stream = reader.ObserveChangesAsync(new() { MaxCaptures = 2 }).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(ObservationStreamStatus.Observed, stream.Current.Status);
        Assert.DoesNotContain(stream.Current.Events, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        browser.Snapshot = Live("Ready for review");
        var pending = stream.MoveNextAsync().AsTask();
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(await pending);
        Assert.Equal("Ready for review", Assert.Single(stream.Current.Events,
            item => item.Kind == ReaderEventKind.LiveRegionChanged).Detail);
        Assert.True(stream.Current.SamplingMayMissChanges);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(ObservationStreamStatus.CaptureLimitReached, stream.Current.Status);
        Assert.Equal(2, stream.Current.Captures);
        Assert.False(await stream.MoveNextAsync());
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.GoalVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleObservation_CancellationOrDeadline_ReturnsExplicitTerminalStatus(bool cancelCaller)
    {
        var clock = new FakeTimeProvider();
        var browser = new GapBrowser(Live("Ready"));
        using var cancellation = new CancellationTokenSource();
        using var reader = new ScreenReaderSession(browser, timeProvider: clock);
        await using var stream = reader.ObserveChangesAsync(new() { Duration = TimeSpan.FromMilliseconds(50) },
            cancellation.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var pending = stream.MoveNextAsync().AsTask();
        if (cancelCaller) cancellation.Cancel(); else clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.True(await pending);
        Assert.Equal(cancelCaller ? ObservationStreamStatus.Cancelled : ObservationStreamStatus.DurationElapsed, stream.Current.Status);
        Assert.False(await stream.MoveNextAsync());
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleObservation_BudgetOrTruncation_IsExplicitAndStops(bool truncate)
    {
        var browser = new GapBrowser(Live("Ready"));
        using var reader = new ScreenReaderSession(browser, maxCommands: 1, maxTimelineEvents: truncate ? 1 : 100);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        // Truncation has priority only when command budget remains available.
        using var observed = truncate ? new ScreenReaderSession(browser, maxTimelineEvents: 1) : null;
        await using var stream = (observed ?? reader).ObserveChangesAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(truncate ? ObservationStreamStatus.TimelineTruncated : ObservationStreamStatus.CommandBudgetExceeded, stream.Current.Status);
        Assert.False(await stream.MoveNextAsync());
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task IdleObservation_BrowserFailure_RecordsErrorAndClosesCommandWithoutBarrier()
    {
        var browser = new GapBrowser(Live("Ready")) { FailCapture = true };
        using var reader = new ScreenReaderSession(browser);
        await using var stream = reader.ObserveChangesAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(ObservationStreamStatus.BrowserError, stream.Current.Status);
        Assert.Equal("TEST_CAPTURE_FAILED", stream.Current.ErrorCode);
        Assert.Contains(stream.Current.Events, item => item.Kind == ReaderEventKind.BrowserError);
        Assert.Equal(ReaderEventKind.CommandCompleted, stream.Current.Events[^1].Kind);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
        Assert.False(await stream.MoveNextAsync());
    }

    [Fact]
    public async Task IdleObservation_YieldsControllerGate_ForInterleavedCommands()
    {
        var clock = new FakeTimeProvider();
        var browser = new GapBrowser(Live("Ready"));
        using var reader = new ScreenReaderSession(browser, timeProvider: clock);
        await using var stream = reader.ObserveChangesAsync(new() { MaxCaptures = 2 }).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var command = await reader.ExecuteAsync(ReaderCommand.ReadCurrent).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ReaderOutcome.Observed, command.Outcome);
        browser.Snapshot = Live("Changed between commands");
        var pending = stream.MoveNextAsync().AsTask();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(await pending);
        var changed = Assert.Single(stream.Current.Events, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        Assert.True(changed.CommandSequence > command.Sequence);
        Assert.Equal(ReaderCommand.ObserveChanges, changed.Command);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task IdleObservation_CancelledDuringCapture_ReleasesControllerAndRecordsNoInput()
    {
        using var cancellation = new CancellationTokenSource();
        var pendingCapture = new TaskCompletionSource<AccessibilitySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new GapBrowser(Live("Ready")) { PendingCapture = pendingCapture.Task };
        using var reader = new ScreenReaderSession(browser);
        await using var stream = reader.ObserveChangesAsync(cancellationToken: cancellation.Token).GetAsyncEnumerator();
        var pending = stream.MoveNextAsync().AsTask();
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();
        Assert.True(await pending);
        Assert.Equal(ObservationStreamStatus.Cancelled, stream.Current.Status);
        Assert.Equal(ReaderEventKind.CommandCompleted, stream.Current.Events[^1].Kind);
        Assert.Contains(stream.Current.Events, item => item.Kind == ReaderEventKind.Cancelled);
        pendingCapture.SetResult(Live("Recovered"));
        Assert.Equal(ReaderOutcome.Observed,
            (await reader.ExecuteAsync(ReaderCommand.ReadCurrent).WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.Empty(browser.Keys);
        Assert.False(await stream.MoveNextAsync());
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public async Task IdleObservation_InvalidBounds_RejectsBeforeCapture(int duration, int interval, int captures)
    {
        var browser = new GapBrowser(Live("Ready"));
        using var reader = new ScreenReaderSession(browser);
        await using var stream = reader.ObserveChangesAsync(new()
        { Duration = TimeSpan.FromMilliseconds(duration), PollInterval = TimeSpan.FromMilliseconds(interval), MaxCaptures = captures }).GetAsyncEnumerator();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => stream.MoveNextAsync().AsTask());
        Assert.Equal(0, browser.Captures);
    }

    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) => new("gap-document", nodes.ToImmutableArray(), []);
    private static AccessibilitySnapshot Live(string text) => Snapshot(new AccessibleNode("live", "status", "",
        LiveRegion: new("polite", true, false, text)));

    private sealed class GapBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public List<string> Keys { get; } = [];
        public bool FailCapture { get; init; }
        public Task<AccessibilitySnapshot>? PendingCapture { get; init; }
        public int Captures { get; private set; }
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Captures++;
            return FailCapture ? throw new BrowserOperationException("TEST_CAPTURE_FAILED")
                : PendingCapture?.WaitAsync(cancellationToken) ?? Task.FromResult(Snapshot);
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) { Keys.Add(key); return Task.CompletedTask; }
    }
}
