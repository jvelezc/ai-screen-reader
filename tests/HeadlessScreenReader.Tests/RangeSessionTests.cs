using System.Collections.Immutable;
using System.Globalization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class RangeSessionTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    public void RangeConsistency_UnknownBoundariesAndInvalidNumbers_AreDistinct(int scenario, bool expected)
    {
        var range = scenario switch
        {
            0 => Range(null, null, null),
            1 => Range(-2, -2, 10),
            2 => Range(10, -2, 10),
            3 => Range(0, 0, 0),
            4 => Range(0.5, null, null),
            5 => Range(-2.5, -2, 10),
            6 => Range(10.5, -2, 10),
            7 => Range(null, 1, 0),
            8 => Range(double.NaN, 0, 1),
            9 => Range(0, double.NegativeInfinity, 1),
            10 => Range(0, 0, double.PositiveInfinity),
            11 => Range(null, 0, null),
            _ => Range(null, null, 100)
        };
        Assert.Equal(expected, range.IsConsistent);
    }

    [Theory]
    [InlineData(0, "Value, slider, value 0.5, minimum -2, maximum 10")]
    [InlineData(1, "Value, slider, value Half, minimum -2, maximum 10")]
    [InlineData(2, "Value, slider, current value not exposed, minimum 0, maximum 1")]
    [InlineData(3, "Value, slider, current value not exposed")]
    [InlineData(4, "Value, slider, range data inconsistent")]
    [InlineData(5, "Value, slider, value 0.5")]
    public async Task ReadRange_AnnouncesObservedTextNumbersOrUnknown_UsingInvariantCulture(int scenario, string expected)
    {
        var range = scenario switch
        {
            0 => Range(0.5, -2, 10),
            1 => Range(0.5, -2, 10) with { ValueText = new("Half", SemanticSource.DeclaredAria) },
            2 => Range(null, 0, 1),
            3 => Range(null, null, null),
            4 => Range(11, 0, 10),
            _ => Range(0.5, null, null)
        };
        var browser = new RangeBrowser(Snapshot(new AccessibleNode("range", "slider", "Value", Range: range)));
        using var reader = new ScreenReaderSession(browser);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var step = await reader.ExecuteAsync(ReaderCommand.NextRange);
            Assert.Equal(expected, step.Announcement);
            Assert.Equal(range, step.Range);
            Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
            Assert.Equal(0, step.BrowserKeystrokes);
            Assert.Empty(browser.Keys);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RangeNavigation_BothDirections_NoWrapOrInput(bool backwards)
    {
        var browser = new RangeBrowser(Snapshot(new AccessibleNode("start", "heading", "Start"),
            new("slider", "slider", "Volume"), new("skip", "spinbutton", "Number"),
            new("meter", "meter", "Balance"), new("progress", "progressbar", "Transfer"),
            new("end", "button", "End")));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(backwards ? ReaderCommand.DocumentEnd : ReaderCommand.DocumentStart);
        var command = backwards ? ReaderCommand.PreviousRange : ReaderCommand.NextRange;
        var expected = backwards ? new[] { "progress", "meter", "slider" } : ["slider", "meter", "progress"];
        foreach (var id in expected) Assert.Equal(id, (await reader.ExecuteAsync(command)).CursorNodeId);
        var noMatch = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.NoMatch, noMatch.Outcome);
        Assert.Equal(expected[^1], noMatch.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(ReaderCommand.NextRange)]
    [InlineData(ReaderCommand.PreviousRange)]
    public async Task RangeNavigation_FocusMode_IsUnsupported(ReaderCommand command)
    {
        var browser = new RangeBrowser(Snapshot(new AccessibleNode("range", "slider", "Value", IsFocused: true)));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(ReaderOutcome.Unsupported, (await reader.ExecuteAsync(command)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task RangeEvents_OnlyChangedValuesEmitAndRetainHistoricalStructuredData()
    {
        var before = Range(40, 0, 100);
        var browser = new RangeBrowser(Snapshot(new AccessibleNode("range", "slider", "Volume", Range: before)));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextRange);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.WidgetStateChanged);
        var after = before with { Value = 50 };
        browser.Snapshot = Snapshot(new AccessibleNode("range", "slider", "Volume", Range: after));
        var changed = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(after, changed.Range);
        var item = Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.WidgetStateChanged);
        Assert.Equal("range", item.NodeId);
        Assert.Equal(after, item.Range);
        Assert.Equal("Volume, slider, value 50, minimum 0, maximum 100", item.Detail);
        browser.Snapshot = Snapshot(new AccessibleNode("range", "slider", "Volume", Range: after with { Value = 60 }));
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(50, item.Range?.Value);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task RangeEvents_TextOnlyChange_IsObservedWithoutNumericChange()
    {
        var range = Range(2, 0, 5) with { ValueText = new("Low", SemanticSource.DeclaredAria) };
        var browser = new RangeBrowser(Snapshot(new AccessibleNode("range", "slider", "Level", Range: range)));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextRange);
        browser.Snapshot = Snapshot(new AccessibleNode("range", "slider", "Level",
            Range: range with { ValueText = new("Quiet", SemanticSource.DeclaredAria) }));
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(2, step.Range?.Value);
        Assert.Equal("Level, slider, value Quiet, minimum 0, maximum 5", step.Announcement);
        Assert.Equal("Quiet", Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.WidgetStateChanged).Range?.ValueText?.Text);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void RangeExpectation_RequiresExactObservedConsistentValueAndFocus(int scenario, bool expected)
    {
        var range = scenario switch
        {
            1 => Range(null, 0, 100),
            2 => Range(0, 1, 100),
            3 => null,
            4 => Range(1, 0, 100),
            _ => Range(0, 0, 100)
        };
        var snapshot = Snapshot(new AccessibleNode("range", "slider", "Volume", IsFocused: scenario != 5, Range: range));
        var expectation = new ObservationExpectation("slider", "Volume", MustBeFocused: true,
            RangeValue: scenario == 6 ? double.NaN : 0);
        Assert.Equal(expected, expectation.IsSatisfiedBy(snapshot));
    }

    [Fact]
    public async Task RangeVerification_RefreshesResultAfterReadOnlyPolling_WithoutInput()
    {
        var initial = Snapshot(new AccessibleNode("range", "progressbar", "Transfer", Range: Range(50, 0, 200)));
        var complete = Snapshot(new AccessibleNode("range", "progressbar", "Transfer", Range: Range(200, 0, 200)));
        var browser = new RangeBrowser(initial) { CaptureSequence = [initial, complete] };
        using var reader = new ScreenReaderSession(browser, waitOptions: new() { MaxPolls = 2 });
        var step = await reader.ExecuteAsync(ReaderCommand.NextRange,
            expectation: new("progressbar", "Transfer", RangeValue: 200));
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.Equal(200, step.Range?.Value);
        Assert.Equal("Transfer, progressbar, value 200, minimum 0, maximum 200", step.Announcement);
        Assert.Equal(2, browser.Captures);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task UnknownProgress_DoesNotVerifyCompletionOrClaimBarrier()
    {
        var browser = new RangeBrowser(Snapshot(new AccessibleNode("range", "progressbar", "Waiting", Range: Range(null, 0, 1))));
        using var reader = new ScreenReaderSession(browser, waitOptions: new() { MaxPolls = 1 });
        var step = await reader.ExecuteAsync(ReaderCommand.NextRange,
            expectation: new("progressbar", "Waiting", RangeValue: 1));
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal(VerificationStatus.TimedOut, step.Verification);
        Assert.Null(step.Range?.Value);
        Assert.Null(step.BarrierCode);
        Assert.False(step.InputMayHaveOccurred);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false, ReaderOutcome.FocusRequired)]
    [InlineData(true, ReaderOutcome.Unsupported)]
    public async Task RangeActivation_DoesNotInventFocusOrEnterSpace(bool focused, ReaderOutcome expected)
    {
        var browser = new RangeBrowser(Snapshot(new AccessibleNode("range", "slider", "Volume", IsFocused: focused, Range: Range(40, 0, 100))));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextRange);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(expected, step.Outcome);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Empty(browser.Keys);
    }

    private static RangeObservation Range(double? value, double? min, double? max) =>
        new(value, min, max, SemanticSource.ChromiumAccessibility);
    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) => new("range-document", nodes.ToImmutableArray(), []);

    private sealed class RangeBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public AccessibilitySnapshot[] CaptureSequence { get; init; } = [];
        public List<string> Keys { get; } = [];
        public int Captures { get; private set; }
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            var index = Captures++;
            return Task.FromResult(CaptureSequence.Length == 0 ? Snapshot : CaptureSequence[Math.Min(index, CaptureSequence.Length - 1)]);
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) { Keys.Add(key); return Task.CompletedTask; }
    }
}
