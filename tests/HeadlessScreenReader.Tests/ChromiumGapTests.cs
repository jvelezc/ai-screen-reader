using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumGapTests
{
    [Fact]
    public async Task Required_ChromiumNativeCheckbox_HasProvenanceAndTracksChanges()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Semantics);
        var initial = await browser.CaptureAsync();
        var required = Assert.Single(initial.Nodes, node => node.Name == "Required choice" && node.Role == "checkbox");
        Assert.True(required.IsRequired);
        Assert.Equal(new RequiredObservation(true, SemanticSource.NativeHtmlProperty), required.Required);
        Assert.Equal(new RequiredObservation(false, SemanticSource.NativeHtmlProperty),
            Assert.Single(initial.Nodes, node => node.Name == "Optional choice" && node.Role == "checkbox").Required);
        Assert.Equal(new RequiredObservation(true, SemanticSource.DeclaredAria),
            Assert.Single(initial.Nodes, node => node.Name == "Declared choice" && node.Role == "checkbox").Required);
        Assert.Null(Assert.Single(initial.Nodes, node => node.Name == "Toggle requirement" && node.Role == "button").Required);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "Required choice");
        var readRequired = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Contains(", required", readRequired.Announcement, StringComparison.Ordinal);
        Assert.Equal(required.Required, readRequired.Required);
        await TabToAsync(reader, browser, "Toggle requirement");
        await reader.ExecuteAsync(ReaderCommand.Activate);
        var changed = Assert.Single((await browser.CaptureAsync()).Nodes, node => node.Id == required.Id);
        Assert.False(changed.IsRequired);
        Assert.Equal(new RequiredObservation(false, SemanticSource.NativeHtmlProperty), changed.Required);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.WidgetStateChanged
            && item.NodeId == required.Id && !item.Detail.Contains(", required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogicalSet_ChromiumDeclarations_AreSeparateFromExposedPeersAndValidateBounds()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Semantics);
        var snapshot = await browser.CaptureAsync();
        var fifth = Assert.Single(snapshot.Nodes, node => node.Name == "Fifth" && node.Role == "option");
        Assert.Equal(new LogicalSetPosition(5, 20, true, SemanticSource.DeclaredAria), fifth.LogicalSet);
        Assert.Equal(new LogicalSetPosition(8, -1, true, SemanticSource.DeclaredAria),
            Assert.Single(snapshot.Nodes, node => node.Name == "Eighth" && node.Role == "option").LogicalSet);
        Assert.False(Assert.Single(snapshot.Nodes, node => node.Name == "Inconsistent" && node.Role == "option").LogicalSet?.IsValid);
        Assert.False(Assert.Single(snapshot.Nodes, node => node.Name == "Invalid position" && node.Role == "option").LogicalSet?.IsValid);
        Assert.False(Assert.Single(snapshot.Nodes, node => node.Name == "Zero values" && node.Role == "option").LogicalSet?.IsValid);
        Assert.False(Assert.Single(snapshot.Nodes, node => node.Name == "Invalid negative size" && node.Role == "option").LogicalSet?.IsValid);
        Assert.Equal(new LogicalSetPosition(9, null, true, SemanticSource.DeclaredAria),
            Assert.Single(snapshot.Nodes, node => node.Name == "Position only" && node.Role == "option").LogicalSet);
        Assert.Equal(new LogicalSetPosition(null, 30, true, SemanticSource.DeclaredAria),
            Assert.Single(snapshot.Nodes, node => node.Name == "Size only" && node.Role == "option").LogicalSet);
        Assert.Null(Assert.Single(snapshot.Nodes, node => node.Name == "No declaration" && node.Role == "option").LogicalSet);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "Fifth");
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("Fifth, option, selected, item 5 of 20", step.Announcement);
        Assert.Equal(fifth.LogicalSet, step.Context?.LogicalSet);
        Assert.Equal(1, step.Context?.ExposedPeers?.Position);
        Assert.Equal(2, step.Context?.ExposedPeers?.Count);
    }

    [Fact]
    public async Task ProtectedEdit_Chromium_AnnouncesProtectionWithoutExposingSeededValues()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        var initial = await browser.CaptureAsync();
        Assert.Equal(new ProtectedObservation(true, SemanticSource.NativeHtmlProperty),
            Assert.Single(initial.Nodes, node => node.Name == "Secret" && node.Role == "textbox").Protection);
        Assert.Equal(new ProtectedObservation(false, SemanticSource.NativeHtmlProperty),
            Assert.Single(initial.Nodes, node => node.Name == "Account" && node.Role == "textbox").Protection);
        using var reader = new ScreenReaderSession(browser);
        await TabToAsync(reader, browser, "Secret");
        var protectedRead = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal("Secret, textbox, protected", protectedRead.Announcement);
        Assert.Equal(new ProtectedObservation(true, SemanticSource.NativeHtmlProperty), protectedRead.Protection);
        var output = System.Text.Json.JsonSerializer.Serialize(new { initial, reader.Timeline });
        Assert.DoesNotContain("fixture-secret-do-not-log", output, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-account-value", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackwardNavigation_Chromium_TraversesHeadingLinkAndLandmarkWithoutBrowserInput()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        Assert.Equal("Controls, heading level 2", (await reader.ExecuteAsync(ReaderCommand.PreviousHeading)).Announcement);
        Assert.Equal("Screen reader fixture, heading level 1", (await reader.ExecuteAsync(ReaderCommand.PreviousHeading)).Announcement);
        await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        Assert.Equal("Read details, link", (await reader.ExecuteAsync(ReaderCommand.PreviousLink)).Announcement);
        await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        Assert.Equal("Primary, navigation", (await reader.ExecuteAsync(ReaderCommand.PreviousLandmark)).Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousLandmark)).Outcome);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        Assert.Equal("Screen reader fixture, heading level 1", (await reader.ExecuteAsync(ReaderCommand.NextHeading)).Announcement);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task PreviousLandmark_ChromiumNestedContexts_SkipsAncestorsAndPreservesSibling()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Landmarks);
        var initialFocus = (await browser.CaptureAsync()).FocusedNode?.Id;
        using var reader = new ScreenReaderSession(browser);
        var end = await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
        Assert.True(end.Context?.AncestryComplete);
        Assert.Equal(new[] { "main", "region", "navigation" }, end.Context!.Ancestors.Select(item => item.Role));
        Assert.Equal("Earlier section, region", (await reader.ExecuteAsync(ReaderCommand.PreviousLandmark)).Announcement);
        Assert.Equal("Primary, navigation", (await reader.ExecuteAsync(ReaderCommand.PreviousLandmark)).Announcement);
        var noMatch = await reader.ExecuteAsync(ReaderCommand.PreviousLandmark);
        Assert.Equal(ReaderOutcome.NoMatch, noMatch.Outcome);
        Assert.Equal("Primary, navigation", (await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Announcement);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        Assert.Equal("Primary, navigation", (await reader.ExecuteAsync(ReaderCommand.NextLandmark)).Announcement);
        Assert.Equal("main", (await reader.ExecuteAsync(ReaderCommand.NextLandmark)).Announcement);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
        Assert.Equal(initialFocus, (await browser.CaptureAsync()).FocusedNode?.Id);
    }

    [Fact]
    public async Task IdleObservation_Chromium_SeesDelayedUpdateWithoutAdditionalInput()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.LiveRegions);
        using var reader = new ScreenReaderSession(browser, maxCommands: 100);
        await TabToAsync(reader, browser, "Start timed update");
        await reader.ExecuteAsync(ReaderCommand.Activate);
        using var cancellation = new CancellationTokenSource();
        var batches = new List<ReaderObservationBatch>();
        await foreach (var batch in reader.ObserveChangesAsync(new()
        { Duration = TimeSpan.FromSeconds(3), PollInterval = TimeSpan.FromMilliseconds(25), MaxCaptures = 60 }, cancellation.Token))
        {
            batches.Add(batch);
            if (batch.Events.Any(item => item.Kind == ReaderEventKind.LiveRegionChanged && item.Detail == "Timed complete"))
                cancellation.Cancel();
        }
        var events = batches.SelectMany(batch => batch.Events).ToArray();
        Assert.Single(events, item => item.Kind == ReaderEventKind.LiveRegionChanged && item.Detail == "Timed complete");
        Assert.Equal(ObservationStreamStatus.Cancelled, batches[^1].Status);
        Assert.DoesNotContain(events, item => item.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.GoalVerified);
        Assert.Equal("Start timed update", (await browser.CaptureAsync()).FocusedNode?.Name);
    }

    private static async Task TabToAsync(ScreenReaderSession reader, IScreenReaderBrowser browser, string name)
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            await reader.ExecuteAsync(ReaderCommand.Tab);
            if ((await browser.CaptureAsync()).FocusedNode?.Name == name) return;
        }
        Assert.Fail($"Fixture focus did not reach {name}.");
    }
}
