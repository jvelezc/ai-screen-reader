using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumRangeTests
{
    [Fact]
    public async Task Ranges_Chromium_ExposeNativeBoundsAndSourceTaggedAriaText_ExcludeEditableValues()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Ranges);
        var snapshot = await browser.CaptureAsync();
        AccessibleNode Named(string name) => Assert.Single(snapshot.Nodes, n => n.Name == name
            && n.Role is "slider" or "meter" or "progressbar" or "spinbutton" or "textbox" or "checkbox");
        Assert.Equal(new(40, 0, 100, SemanticSource.ChromiumAccessibility, new("40", SemanticSource.ChromiumAccessibility)), Named("Volume").Range);
        Assert.Equal(new(0, -5, 5, SemanticSource.ChromiumAccessibility, new("Neutral", SemanticSource.DeclaredAria)), Named("Level").Range);
        Assert.Equal(new(50, 0, 200, SemanticSource.ChromiumAccessibility), Named("Transfer").Range);
        Assert.Equal(new(-2, -10, 10, SemanticSource.ChromiumAccessibility), Named("Balance").Range);
        Assert.Equal(new(null, 0, 1, SemanticSource.ChromiumAccessibility), Named("Waiting").Range);
        Assert.True(Named("Locked volume").IsDisabled);
        Assert.Equal(80, Named("Locked volume").Range?.Value);
        foreach (var name in new[] { "Numeric field", "Private field", "Editable range", "Nonrange" }) Assert.Null(Named(name).Range);
        var serialized = JsonSerializer.Serialize(snapshot);
        foreach (var canary in new[] { "819273", "731829", "RANGE_PASSWORD_CANARY", "RANGE_EDITABLE_CANARY", "RANGE_VALUE_TEXT_CANARY" })
            Assert.DoesNotContain(canary, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeSlider_Chromium_ActualKeyboardFocusChangesAndClampsObservedValues()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Ranges);
        using var reader = new ScreenReaderSession(browser);
        var focused = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(ReaderMode.Focus, focused.Mode);
        Assert.Equal(focused.CursorNodeId, focused.FocusedNodeId);
        Assert.Equal(40, focused.Range?.Value);
        var keys = new[] { "ArrowRight", "ArrowLeft", "Home", "ArrowLeft", "End", "ArrowRight" };
        var values = new[] { 50d, 40, 0, 0, 100, 100 };
        for (var i = 0; i < keys.Length; i++)
        {
            var step = await reader.ExecuteAsync(ReaderCommand.PressKey, keys[i],
                expectation: new("slider", "Volume", MustBeFocused: true, RangeValue: values[i]));
            Assert.Equal(VerificationStatus.Satisfied, step.Verification);
            Assert.Equal(values[i], step.Range?.Value);
            Assert.Equal(focused.FocusedNodeId, step.FocusedNodeId);
            Assert.Equal(1, step.BrowserKeystrokes);
        }
        Assert.Equal(new[] { 50d, 40, 0, 100 }, reader.Timeline.Where(e => e.Kind == ReaderEventKind.WidgetStateChanged)
            .Select(e => e.Range!.Value!.Value).ToArray());
    }

    [Fact]
    public async Task AriaSlider_Chromium_FractionalValuesAndTextFollowActualFocus()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Ranges);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var focused = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal("Neutral", focused.Range?.ValueText?.Text);
        var step = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowRight",
            expectation: new("slider", "Level", MustBeFocused: true, RangeValue: 0.5));
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.Equal(0.5, step.Range?.Value);
        Assert.Equal("Level 0.5", step.Range?.ValueText?.Text);
        Assert.Equal("Level, slider, value Level 0.5, minimum -5, maximum 5", step.Announcement);
        Assert.Equal(focused.FocusedNodeId, step.FocusedNodeId);
        var minimum = await reader.ExecuteAsync(ReaderCommand.PressKey, "Home");
        Assert.Equal(-5, minimum.Range?.Value);
        var maximum = await reader.ExecuteAsync(ReaderCommand.PressKey, "End");
        Assert.Equal(5, maximum.Range?.Value);
        Assert.Equal(new[] { 0d, 0.5, -5 }, reader.Timeline
            .Where(e => e.Kind == ReaderEventKind.InputAttempted && e.Command == ReaderCommand.PressKey)
            .Select(e => e.Range!.Value!.Value).ToArray());
    }

    [Fact]
    public async Task RangeNavigation_Chromium_IsReadOnlyAndLeavesUnknownProgressExplicit()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Ranges);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var ids = new List<string>();
        foreach (var name in new[] { "Volume", "Level", "Transfer", "Balance", "Waiting", "Locked volume", "Editable range" })
        {
            var step = await reader.ExecuteAsync(ReaderCommand.NextRange);
            var node = Assert.Single((await browser.CaptureAsync()).Nodes, n => n.Id == step.CursorNodeId);
            Assert.Equal(name, node.Name);
            Assert.Equal(0, step.BrowserKeystrokes);
            ids.Add(node.Id);
            if (name == "Waiting")
            {
                Assert.Null(step.Range?.Value);
                Assert.Equal("Waiting, progressbar, current value not exposed, minimum 0, maximum 1", step.Announcement);
            }
        }
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextRange)).Outcome);
        Assert.Equal(ids[^2], (await reader.ExecuteAsync(ReaderCommand.PreviousRange)).CursorNodeId);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task Progress_Chromium_VerifiesExactValueAfterButtonInput_WithoutTreatingUnknownAsComplete()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Ranges);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var button = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal("Complete transfer, button", button.Announcement);
        var result = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("progressbar", "Transfer", RangeValue: 200));
        Assert.Equal(VerificationStatus.Satisfied, result.Verification);
        Assert.Equal(1, result.BrowserKeystrokes);
        var snapshot = await browser.CaptureAsync();
        Assert.Equal(200, Assert.Single(snapshot.Nodes, n => n.Name == "Transfer").Range?.Value);
        Assert.Null(Assert.Single(snapshot.Nodes, n => n.Name == "Waiting").Range?.Value);
        var changed = Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.WidgetStateChanged);
        Assert.Equal(200, changed.Range?.Value);
        Assert.Contains(reader.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
    }
}
