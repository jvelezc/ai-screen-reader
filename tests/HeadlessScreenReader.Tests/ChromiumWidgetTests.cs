using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumWidgetTests
{
    [Fact]
    public async Task ModalDialog_TabStaysInsideAndEscapeRestoresInvoker()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Widgets);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "button", "Review preferences");
        var opened = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("dialog", "Review preferences"));
        Assert.Equal(VerificationStatus.Satisfied, opened.Verification);
        var modal = await browser.CaptureAsync();
        Assert.Contains(modal.Nodes, node => node.Role == "dialog" && node.IsModal);
        Assert.Equal("Close review", modal.FocusedNode?.Name);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal("Keep reviewing", (await browser.CaptureAsync()).FocusedNode?.Name);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal("Close review", (await browser.CaptureAsync()).FocusedNode?.Name);
        await reader.ExecuteAsync(ReaderCommand.ReverseTab);
        Assert.Equal("Keep reviewing", (await browser.CaptureAsync()).FocusedNode?.Name);
        var closed = await reader.ExecuteAsync(ReaderCommand.Escape,
            expectation: new("button", "Review preferences", MustBeFocused: true));
        Assert.Equal(VerificationStatus.Satisfied, closed.Verification);
        Assert.DoesNotContain((await browser.CaptureAsync()).Nodes, node => node.Role == "dialog");
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.DialogOpened);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.DialogClosed);
    }

    [Fact]
    public async Task InvalidForm_ExposesErrorFocusAndWidgetValidationState()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Widgets);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "button", "Save preferences");
        var step = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("alert", "Accept terms before saving.", MustBeFocused: true));
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.Contains("Accept terms before saving.", step.Announcement, StringComparison.Ordinal);
        var snapshot = await browser.CaptureAsync();
        var checkbox = Assert.Single(snapshot.Nodes,
            node => node.Role == "checkbox" && node.Name == "Accept terms");
        Assert.Equal("Required to save preferences.", checkbox.Description);
        Assert.Equal("true", checkbox.Invalid);
        Assert.Equal("false", checkbox.Checked);
        Assert.True(Assert.Single(snapshot.Nodes, node => node.Role == "combobox").IsRequired);
    }

    [Fact]
    public async Task Combobox_TracksActiveDescendantSeparatelyFromDomFocusAndCommitsChoice()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Widgets);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "combobox", "Delivery region");
        var before = await browser.CaptureAsync();
        await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        var north = await browser.CaptureAsync();
        Assert.Equal(before.FocusedNode?.Id, north.FocusedNode?.Id);
        Assert.Equal("North", north.EffectiveFocusedNode?.Name);
        Assert.True(north.EffectiveFocusedNode?.Selected);
        Assert.True(north.FocusedNode?.Expanded);
        var next = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        Assert.Equal("South, option, selected", next.Announcement);
        Assert.NotEqual(next.FocusedNodeId, next.ActiveDescendantNodeId);
        Assert.Equal(next.ActiveDescendantNodeId, next.CursorNodeId);
        var commit = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(1, commit.BrowserKeystrokes);
        var after = await browser.CaptureAsync();
        Assert.Equal(before.FocusedNode?.Id, after.FocusedNode?.Id);
        Assert.Null(after.FocusedNode?.ActiveDescendantId);
        Assert.False(after.FocusedNode?.Expanded);
        Assert.Contains(after.Nodes, node => node.Name == "Delivery region: South");
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.ActiveDescendantChanged);
    }

    [Fact]
    public async Task PreferenceWorkflow_CheckboxRadioAndCombo_SaveThenObserveBlockedRoute()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Widgets);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "checkbox", "Accept terms");
        var check = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("checkbox", "Accept terms", Checked: "true"));
        Assert.Equal(VerificationStatus.Satisfied, check.Verification);
        Assert.Contains("checked", check.Announcement, StringComparison.Ordinal);
        await TabToAsync(reader, browser, "radio", "Standard");
        var radio = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowRight",
            expectation: new("radio", "Express", MustBeFocused: true, Checked: "true"));
        Assert.Equal(VerificationStatus.Satisfied, radio.Verification);
        await TabToAsync(reader, browser, "combobox", "Delivery region");
        await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        await reader.ExecuteAsync(ReaderCommand.Activate);
        await TabToAsync(reader, browser, "button", "Save preferences");
        var saved = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("heading", "Preferences saved", MustBeFocused: true));
        Assert.Equal(VerificationStatus.Satisfied, saved.Verification);
        Assert.Equal("Preferences saved, heading level 2", saved.Announcement);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.GoalVerified);
        await TabToAsync(reader, browser, "button", "Continue to payment");
        var blocked = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("heading", "Payment"));
        Assert.Equal(ReaderOutcome.Blocked, blocked.Outcome);
        Assert.Equal(VerificationStatus.Blocked, blocked.Verification);
        Assert.Equal("OBSERVED_DISABLED_CONTROL", blocked.BarrierCode);
        Assert.Equal(0, blocked.BrowserKeystrokes);
        Assert.Contains("Payment cannot be reached through this disabled control.", blocked.Announcement, StringComparison.Ordinal);
        Assert.DoesNotContain(reader.Timeline, item => item.CommandSequence == blocked.Sequence
            && item.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task MissingDestination_TimesOutAsUnknownAfterRealKeyboardInput()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Widgets);
        using var reader = new ScreenReaderSession(browser,
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(150) });
        var result = await reader.ExecuteAsync(ReaderCommand.Tab,
            expectation: new("heading", "Unobserved destination"));
        Assert.Equal(ReaderOutcome.Unknown, result.Outcome);
        Assert.Equal(VerificationStatus.TimedOut, result.Verification);
        Assert.Equal(1, result.BrowserKeystrokes);
        Assert.True(result.InputMayHaveOccurred);
        Assert.Null(result.BarrierCode);
    }

    [Fact]
    public async Task Timeline_DoesNotCaptureFixtureInputCanaries()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        using var reader = new ScreenReaderSession(browser);
        for (var i = 0; i < 8; i++) await reader.ExecuteAsync(ReaderCommand.Tab);
        var json = JsonSerializer.Serialize(reader.Timeline);
        Assert.DoesNotContain("fixture-secret-do-not-log", json, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-account-value", json, StringComparison.Ordinal);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.FocusChanged);
    }

    private static async Task TabToAsync(ScreenReaderSession reader, ChromiumFixtureBrowser browser,
        string role, string name)
    {
        for (var i = 0; i < 16; i++)
        {
            await reader.ExecuteAsync(ReaderCommand.Tab);
            var focus = (await browser.CaptureAsync()).FocusedNode;
            if (focus?.Role == role && focus.Name == name) return;
        }
        Assert.Fail($"Keyboard traversal did not reach {role}: {name}.");
    }
}
