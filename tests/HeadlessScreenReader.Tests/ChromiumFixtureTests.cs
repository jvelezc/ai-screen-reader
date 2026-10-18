using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumFixtureTests
{
    [Fact]
    public async Task FocusMode_ExternalKeyboardFocus_ReportsMatchingCursorAndAnnouncement()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        // Simulate an external keyboard controller; the reader must observe, not repair, that change.
        await browser.PressKeyAsync("Tab");
        var observed = await browser.CaptureAsync();
        Assert.Equal("Open details", observed.FocusedNode?.Name);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("Open details, button", step.Announcement);
        Assert.Equal(observed.FocusedNode!.Id, step.FocusedNodeId);
        Assert.Equal(step.FocusedNodeId, step.CursorNodeId);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Equal(step.CursorNodeId, reader.Timeline.Last(item => item.Kind == ReaderEventKind.Announcement).NodeId);
    }

    [Fact]
    public async Task Capture_UsesExposedSemanticsAndExcludesInputValues()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        var snapshot = await browser.CaptureAsync();
        Assert.Contains(snapshot.Nodes, node => node.Role == "heading"
            && node.Name == "Screen reader fixture" && node.HeadingLevel == 1);
        Assert.Contains(snapshot.Nodes, node => node.Role == "button"
            && node.Name == "Unavailable" && node.IsDisabled);
        Assert.Contains(snapshot.Nodes, node => node.Role == "button" && node.Name == "");
        Assert.DoesNotContain(snapshot.Nodes, node => node.Name == "Hidden diagnostic text");
        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("fixture-secret-do-not-log", json, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-account-value", json, StringComparison.Ordinal);
        Assert.NotEmpty(snapshot.Limitations);
    }

    [Fact]
    public async Task KeyboardActivation_ReachesObservedFixtureDestination()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        using var reader = new ScreenReaderSession(browser);
        var before = await browser.CaptureAsync();
        Assert.DoesNotContain(before.Nodes, node => node.Name == "Details opened");
        await reader.ExecuteAsync(ReaderCommand.NextButton);
        Assert.Equal(ReaderOutcome.FocusRequired,
            (await reader.ExecuteAsync(ReaderCommand.Activate)).Outcome);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var activation = await reader.ExecuteAsync(ReaderCommand.Activate);
        var after = await browser.CaptureAsync();
        Assert.Equal(1, activation.BrowserKeystrokes);
        Assert.Equal("Details opened, heading level 2", activation.Announcement);
        Assert.Equal("Details opened", after.FocusedNode?.Name);
        Assert.Equal(before.DocumentId, after.DocumentId);
    }

    [Fact]
    public async Task BrowseAndFocusModes_RouteShortcutToDifferentConsumers()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextButton);
        var browse = await browser.CaptureAsync();
        Assert.DoesNotContain(browse.Nodes, node => node.Name == "Browser received b");
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var focus = await reader.ExecuteAsync(ReaderCommand.NextButton);
        var after = await browser.CaptureAsync();
        Assert.Equal(1, focus.BrowserKeystrokes);
        Assert.Contains(after.Nodes, node => node.Name == "Browser received b");
    }
}
