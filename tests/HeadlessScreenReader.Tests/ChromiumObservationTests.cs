using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumObservationTests
{
    [Fact]
    public async Task ClosedDialog_QueuedNotificationCannotStealSubsequentFocus()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Widgets);
        await browser.PressKeyAsync("Tab");
        await browser.PressKeyAsync("Enter");
        Assert.Contains((await browser.CaptureAsync()).Nodes, n => n.Role == "dialog" && n.Name == "Review preferences");
        // Observe the native queued notification deterministically instead of racing host round trips.
        var keptFocus = await browser.DiagnosticPage.EvaluateAsync<bool>("""
            async () => {
              const dialog = document.querySelector('dialog');
              const standard = document.querySelector('input[value="standard"]');
              const closed = new Promise(resolve => dialog.addEventListener('close',
                () => resolve(document.activeElement === standard), {once:true}));
              dialog.close();
              standard.focus();
              return await closed;
            }
            """);
        Assert.True(keptFocus, "A delayed fixture close handler must not undo a later focus transition.");
        Assert.Equal("Standard", (await browser.CaptureAsync()).FocusedNode?.Name);
    }

    [Fact]
    public async Task Context_ChromiumWidgets_ExposesRadioOptionAndDialogContainers()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Widgets);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "Review preferences");
        var opened = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("button", "Close review", MustBeFocused: true));
        Assert.Contains(opened.Context!.Ancestors, item => item.Role == "dialog" && item.Name == "Review preferences");
        await reader.ExecuteAsync(ReaderCommand.Escape);
        await TabToAsync(reader, browser, "Standard");
        var express = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowRight");
        var radioGroup = Assert.Single(express.Context!.Ancestors, item => item.Role == "group");
        Assert.Equal("Delivery speed", radioGroup.Name);
        Assert.Equal(new ExposedPeerPosition(radioGroup.NodeId, 2, 2), express.Context.ExposedPeers);
        await TabToAsync(reader, browser, "Delivery region");
        await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        var south = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        var list = Assert.Single(south.Context!.Ancestors, item => item.Role == "listbox");
        Assert.Equal("Delivery regions", list.Name);
        Assert.Equal(new ExposedPeerPosition(list.NodeId, 2, 2), south.Context.ExposedPeers);
        Assert.NotEqual(south.FocusedNodeId, south.Context.NodeId);
        Assert.Equal(south.ActiveDescendantNodeId, south.Context.NodeId);
        var committed = await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(committed.FocusedNodeId, committed.Context?.NodeId);
        Assert.Null(committed.Context?.ExposedPeers);
        Assert.Equal("Delivery region: South", Assert.Single(reader.Timeline,
            item => item.Kind == ReaderEventKind.LiveRegionChanged && item.CommandSequence == committed.Sequence).Detail);
    }

    [Fact]
    public async Task LiveChanges_ChromiumText_ExcludesEditableContentsAndSeparatesNestedRoots()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.LiveRegions);
        var initial = await browser.CaptureAsync();
        var progress = Assert.Single(initial.Nodes, node => node.Name == "Progress updates");
        Assert.Equal(new LiveRegionObservation("polite", true, false, "Working"), progress.LiveRegion);
        Assert.Equal("Nested ready", Assert.Single(initial.Nodes, node => node.Name == "Nested updates").LiveRegion?.Text);
        var serialized = JsonSerializer.Serialize(initial);
        Assert.DoesNotContain("live-input-canary-do-not-log", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("live-editable-canary-do-not-log", serialized, StringComparison.Ordinal);

        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "Update progress");
        await reader.ExecuteAsync(ReaderCommand.Activate);
        var change = Assert.Single(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        Assert.Equal(progress.Id, change.NodeId);
        Assert.Equal("Complete", change.Detail);
        Assert.Equal("polite", change.LiveRegion?.Politeness);
        await TabToAsync(reader, browser, "Start busy update");
        await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.True(Assert.Single((await browser.CaptureAsync()).Nodes,
            node => node.Name == "Progress updates").LiveRegion?.IsBusy);
        Assert.Single(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        await TabToAsync(reader, browser, "Update nested status");
        await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.True(Assert.Single((await browser.CaptureAsync()).Nodes,
            node => node.Name == "Nested updates").LiveRegion?.IsBusy);
        Assert.Single(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        await TabToAsync(reader, browser, "Finish busy update");
        await reader.ExecuteAsync(ReaderCommand.Activate);
        Assert.Equal(["Complete", "Finished update", "Nested complete"], reader.Timeline
            .Where(item => item.Kind == ReaderEventKind.LiveRegionChanged).Select(item => item.Detail));
        Assert.DoesNotContain("live-editable-canary-do-not-log", JsonSerializer.Serialize(reader.Timeline), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveChanges_ChromiumValidation_ExposesAssertiveAlertWithoutReplacingAnnouncement()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Widgets);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "Save preferences");
        var invalid = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("alert", "Accept terms before saving.", MustBeFocused: true));
        var change = Assert.Single(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        Assert.Equal(invalid.FocusedNodeId, change.NodeId);
        Assert.Equal("Accept terms before saving.", change.LiveRegion?.Text);
        Assert.Equal("assertive", change.LiveRegion?.Politeness);
        Assert.Equal("Accept terms before saving., alert", invalid.Announcement);
        Assert.Equal(VerificationStatus.Satisfied, invalid.Verification);
        Assert.Equal(1, invalid.BrowserKeystrokes);
    }

    private static async Task TabToAsync(ScreenReaderSession reader, IScreenReaderBrowser browser, string name)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await reader.ExecuteAsync(ReaderCommand.Tab);
            if ((await browser.CaptureAsync()).FocusedNode?.Name == name) return;
        }
        Assert.Fail($"Fixture focus did not reach {name}.");
    }
}
