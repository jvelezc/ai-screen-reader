using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class CaptureEpochReaderTests
{
    [Fact]
    public async Task EpochChange_ReanchorsTextAndSeedsLiveBaselineWithoutInventingChangesThroughGap()
    {
        var browser = new EpochBrowser();
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal("Before", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        var boundary = reader.Timeline[^1].Sequence;
        browser.Snapshot = Snapshot(1, "After", "ready", true);
        var after = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("After", after.Announcement);
        Assert.Equal(0, after.TextRange!.Start);
        var baselineEvents = reader.Timeline.Where(e => e.Sequence > boundary).ToArray();
        Assert.Single(baselineEvents, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
        Assert.DoesNotContain(baselineEvents, e => e.Kind is ReaderEventKind.LiveRegionChanged
            or ReaderEventKind.WidgetStateChanged or ReaderEventKind.FocusChanged or ReaderEventKind.DialogOpened);
        browser.Snapshot = Snapshot(1, "After", "finished", true);
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("finished", Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.LiveRegionChanged).Detail);
    }

    private static AccessibilitySnapshot Snapshot(long epoch, string text, string live, bool selected) => new("same-document", [
        new("text", "StaticText", text, Text: new(text, SemanticSource.ChromiumAccessibility)),
        new("check", "checkbox", "Choice", Checked: selected ? "true" : "false"),
        new("live", "status", "Status", LiveRegion: new("polite", false, false, live))
    ], [])
    { CaptureEpoch = epoch };

    private sealed class EpochBrowser : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = CaptureEpochReaderTests.Snapshot(0, "Before", "waiting", false);
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No browser input expected.");
    }
}
