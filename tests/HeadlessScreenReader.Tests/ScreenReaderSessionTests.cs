using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class ScreenReaderSessionTests
{
    [Fact]
    public async Task BrowseShortcut_MovesVirtualCursorWithoutSendingBrowserKey()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.NextButton);
        Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
        Assert.Equal("button", step.CursorNodeId);
        Assert.Equal("link", step.FocusedNodeId);
        Assert.Equal("Open details, button", step.Announcement);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task FocusShortcut_ForwardsBrowserKeyInsteadOfMovingVirtualCursor()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await reader.ExecuteAsync(ReaderCommand.NextButton);
        Assert.Equal(ReaderMode.Focus, step.Mode);
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
        Assert.Equal(["b"], browser.Keys);
        Assert.Equal("link", step.CursorNodeId);
        Assert.Equal(1, step.BrowserKeystrokes);
    }

    [Fact]
    public async Task Activate_CursorAndFocusDiffer_RequiresFocusWithoutInput()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextButton);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.FocusRequired, step.Outcome);
        Assert.Equal("button", step.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task Activate_FocusedCheckbox_SendsSpaceAndObservesChangedState()
    {
        var browser = new FakeBrowser(Snapshot(new AccessibleNode("check", "checkbox", "Enable summaries",
            IsFocused: true, Checked: "false")));
        browser.AfterKey = () => browser.Snapshot = Snapshot(new AccessibleNode("check", "checkbox",
            "Enable summaries", IsFocused: true, Checked: "true"));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(["Space"], browser.Keys);
        Assert.Equal("Enable summaries, checkbox, checked", step.Announcement);
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
    }

    [Fact]
    public async Task Activate_DisabledControl_IsBlockedWithoutInput()
    {
        var browser = new FakeBrowser(Snapshot(new AccessibleNode("disabled", "button", "Unavailable",
            IsFocused: true, IsDisabled: true)));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(ReaderOutcome.Blocked, step.Outcome);
        Assert.Equal("OBSERVED_DISABLED_CONTROL", step.BarrierCode);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task HeadingNavigation_AtEnd_PreservesCursorAndDoesNotWrap()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        var step = await reader.ExecuteAsync(ReaderCommand.NextHeading);
        Assert.Equal(ReaderOutcome.NoMatch, step.Outcome);
        Assert.Equal("heading", step.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task DocumentReplacement_InvalidatesCursorAndRestoresBrowseMode()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextButton);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        browser.Snapshot = browser.Snapshot with { DocumentId = "replacement-document" };
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Null(step.CursorNodeId);
        Assert.Equal(ReaderMode.Browse, step.Mode);
        Assert.Equal("Read details, link", step.Announcement);
    }

    [Fact]
    public async Task RemovedCursorNode_DoesNotKeepStaleAnnouncement()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextButton);
        browser.Snapshot = Snapshot(new AccessibleNode("link", "link", "Read details", IsFocused: true));
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Null(step.CursorNodeId);
        Assert.Equal("Read details, link", step.Announcement);
    }

    [Fact]
    public async Task EmptySnapshot_ReportsNoMatchWithoutInventingControl()
    {
        var browser = new FakeBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.MoveNext)).Outcome);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.Activate)).Outcome);
        Assert.Equal(ReaderOutcome.FocusRequired,
            (await reader.ExecuteAsync(ReaderCommand.EnterFocusMode)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task CommandBudget_AtLimit_PreventsFurtherObservationAndInput()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser, maxCommands: 2);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        var step = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(ReaderOutcome.BudgetExceeded, step.Outcome);
        Assert.Equal(2, browser.Captures);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task Cancellation_BeforeDispatch_PerformsNoObservationOrInput()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ExecuteAsync(ReaderCommand.Tab, cancellationToken: cancellation.Token));
        Assert.Equal(0, browser.Captures);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ConcurrentCommands_SerializeBrowserInputAndSequences()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => reader.ExecuteAsync(ReaderCommand.Tab)));
        Assert.Equal(1, browser.MaxConcurrentInputs);
        Assert.Equal(10, browser.Keys.Count);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), results.Select(step => step.Sequence).Order());
    }

    [Theory]
    [InlineData("Control+l")]
    [InlineData("Alt+F4")]
    [InlineData("")]
    [InlineData(null)]
    public async Task RawKey_OutsidePolicy_IsDeniedWithoutBrowserAccess(string? key)
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.PressKey, key);
        Assert.Equal(ReaderOutcome.Denied, step.Outcome);
        Assert.Equal(0, browser.Captures);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ExplicitBrowserKey_InBrowseMode_IsUnsupported()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowRight");
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task Tab_ToTextbox_SynchronizesCursorFocusAndMode()
    {
        var browser = new FakeBrowser();
        browser.AfterKey = () => browser.Snapshot = Snapshot(
            new AccessibleNode("edit", "textbox", "Account", IsFocused: true));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(["Tab"], browser.Keys);
        Assert.Equal("edit", step.CursorNodeId);
        Assert.Equal(step.FocusedNodeId, step.CursorNodeId);
        Assert.Equal(ReaderMode.Focus, step.Mode);
    }

    [Fact]
    public async Task BrowserFailure_IsReportedAsSystemError()
    {
        var browser = new FakeBrowser { FailCapture = true };
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(ReaderOutcome.BrowserError, step.Outcome);
        Assert.Equal("TEST_CAPTURE_FAILED", step.ErrorCode);
        Assert.Null(step.CursorNodeId);
    }

    [Fact]
    public async Task UnnamedButton_RemainsUnnamed()
    {
        var browser = new FakeBrowser(Snapshot(new AccessibleNode("unnamed", "button", "")));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.NextButton);
        Assert.Equal("button", step.Announcement);
        Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
    }

    [Fact]
    public async Task ReadCurrent_AnnouncesHeadingLevelAndExpandedState()
    {
        var browser = new FakeBrowser(Snapshot(new AccessibleNode("heading", "heading", "Summary",
            IsFocused: true, HeadingLevel: 2, Expanded: false)));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("Summary, heading level 2, collapsed", step.Announcement);
    }

    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) =>
        new("document", nodes.ToImmutableArray(), []);

    [Fact]
    public async Task FocusMode_ExternalFocusChange_ReadsActualBrowserFocus()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        browser.Snapshot = Snapshot(
            new("link", "link", "Read details"),
            new("button", "button", "Open details", IsFocused: true));
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("Open details, button", step.Announcement);
        Assert.Equal("button", step.FocusedNodeId);
        Assert.Equal("button", step.CursorNodeId);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Empty(browser.Keys);
        Assert.Equal("button", reader.Timeline.Last(item => item.Kind == ReaderEventKind.Announcement).NodeId);
    }

    [Fact]
    public async Task FocusMode_LostFocus_ClearsCursorEvenWhenOldNodeStillExists()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        browser.Snapshot = Snapshot(new AccessibleNode("link", "link", "Read details"));
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Null(step.FocusedNodeId);
        Assert.Null(step.CursorNodeId);
        Assert.Equal("No current exposed content.", step.Announcement);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task BrowseMode_ExternalFocusChange_PreservesIndependentVirtualCursor()
    {
        var browser = new FakeBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        browser.Snapshot = Snapshot(
            new("heading", "heading", "Fixture", HeadingLevel: 1),
            new("link", "link", "Read details"),
            new("button", "button", "Open details", IsFocused: true));
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(ReaderMode.Browse, step.Mode);
        Assert.Equal("heading", step.CursorNodeId);
        Assert.Equal("button", step.FocusedNodeId);
        Assert.Equal("Fixture, heading level 1", step.Announcement);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task FailedObservation_AfterInput_PreservesExecutionUncertainty()
    {
        var browser = new FakeBrowser();
        browser.AfterKey = () => browser.FailCapture = true;
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(["Tab"], browser.Keys);
        Assert.Equal(ReaderOutcome.BrowserError, step.Outcome);
        Assert.True(step.InputMayHaveOccurred);
        Assert.Equal(1, step.BrowserKeystrokes);
        Assert.Equal("TEST_CAPTURE_FAILED", step.ErrorCode);
    }

    private sealed class FakeBrowser : IScreenReaderBrowser
    {
        private int _inFlight;
        public FakeBrowser(AccessibilitySnapshot? snapshot = null)
        {
            Snapshot = snapshot ?? ScreenReaderSessionTests.Snapshot(
                new("heading", "heading", "Fixture", HeadingLevel: 1),
                new("link", "link", "Read details", IsFocused: true),
                new("button", "button", "Open details"));
        }

        public AccessibilitySnapshot Snapshot { get; set; }
        public List<string> Keys { get; } = [];
        public Action? AfterKey { get; set; }
        public bool FailCapture { get; set; }
        public int Captures { get; private set; }
        public int MaxConcurrentInputs { get; private set; }

        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Captures++;
            if (FailCapture)
                throw new BrowserOperationException("TEST_CAPTURE_FAILED");
            return Task.FromResult(Snapshot);
        }

        public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inFlight++;
            MaxConcurrentInputs = Math.Max(MaxConcurrentInputs, _inFlight);
            Keys.Add(key);
            await Task.Yield();
            AfterKey?.Invoke();
            _inFlight--;
        }
    }
}
