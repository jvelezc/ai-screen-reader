using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumTextTests
{
    [Fact]
    public async Task ExposedText_Chromium_PreservesWhitespaceAndExcludesEditableOrHiddenContents()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Text);
        var snapshot = await browser.CaptureAsync();
        var passage = Assert.Single(snapshot.Nodes, node => node.Text?.Text.StartsWith("  Café", StringComparison.Ordinal) == true);
        Assert.Equal(new ExposedText("  Café é 👩‍💻 can't\n\nLast line  ", SemanticSource.ChromiumAccessibility), passage.Text);
        Assert.All(snapshot.Nodes.Where(node => node.Text is not null), node => Assert.Contains(node.Role, new[] { "StaticText", "LineBreak" }));
        Assert.All(snapshot.Nodes.Where(node => node.Role == "InlineTextBox"), node => Assert.Null(node.Text));
        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("text-fixture-value-do-not-read", json, StringComparison.Ordinal);
        Assert.DoesNotContain("editable-text-do-not-read", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-text-do-not-read", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextNavigation_Chromium_ReadsGraphemesAndLinesWithoutInputOrFocusMovement()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Text);
        var snapshot = await browser.CaptureAsync();
        var passage = Assert.Single(snapshot.Nodes, node => node.Text?.Text.StartsWith("  Café", StringComparison.Ordinal) == true);
        using var reader = new ScreenReaderSession(browser, maxCommands: 200);
        await MoveToAsync(reader, passage.Id);
        var run = await reader.ExecuteAsync(ReaderCommand.ReadCurrentText);
        Assert.Equal(passage.Text!.Text, run.Announcement);
        foreach (var expected in new[] { " ", " ", "C", "a", "f", "é", " ", "é", " ", "👩‍💻" })
        {
            var step = await reader.ExecuteAsync(ReaderCommand.NextCharacter);
            Assert.Equal(expected, step.Announcement);
            Assert.Equal(TextGranularity.Character, step.TextRange?.Granularity);
            Assert.Equal(passage.Id, step.TextRange?.NodeId);
        }
        var backwards = await reader.ExecuteAsync(ReaderCommand.PreviousCharacter);
        Assert.Equal(" ", backwards.Announcement);
        await MoveToAsync(reader, passage.Id);
        foreach (var expected in new[] { "  Café é 👩‍💻 can't", "", "Last line  " })
        {
            var line = await reader.ExecuteAsync(ReaderCommand.NextLine);
            Assert.Equal(ReaderOutcome.CursorMoved, line.Outcome);
            Assert.Equal(expected, line.Announcement);
            Assert.Equal(passage.Id, line.TextRange?.NodeId);
        }
        var nextRun = await reader.ExecuteAsync(ReaderCommand.NextLine);
        Assert.Equal("Adjacent ", nextRun.Announcement);
        Assert.NotEqual(passage.Id, nextRun.TextRange?.NodeId);
        Assert.Equal(snapshot.FocusedNode?.Id, (await browser.CaptureAsync()).FocusedNode?.Id);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.GoalVerified);
    }

    [Fact]
    public async Task TextNavigation_Chromium_TraversesExposedWordsOnceAndExcludesEditableText()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Text);
        using var reader = new ScreenReaderSession(browser, maxCommands: 200);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var words = new List<string>();
        ReaderStep? boundary = null;
        for (var i = 0; i < 70; i++)
        {
            var step = await reader.ExecuteAsync(ReaderCommand.NextWord);
            if (step.Outcome == ReaderOutcome.NoMatch) { boundary = step; break; }
            Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
            words.Add(step.Announcement);
        }
        Assert.NotNull(boundary);
        Assert.Equal(new[] { "Text", "reading", "Café", "é", "👩‍💻", "can't", "Last", "line", "Adjacent", "bold", "text", ".", "Private", "note", "Change", "passage" }, words);
        Assert.Equal(boundary!.TextRange, (await reader.ExecuteAsync(ReaderCommand.ReadCurrentText)).TextRange);
        var output = JsonSerializer.Serialize(reader.Timeline);
        Assert.DoesNotContain("text-fixture-value-do-not-read", output, StringComparison.Ordinal);
        Assert.DoesNotContain("editable-text-do-not-read", output, StringComparison.Ordinal);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task TextCursor_ChromiumChangedPassage_ReportsInvalidationBeforeContinuing()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Text);
        var initial = await browser.CaptureAsync();
        var passage = Assert.Single(initial.Nodes, node => node.Text?.Text.StartsWith("  Café", StringComparison.Ordinal) == true);
        using var reader = new ScreenReaderSession(browser, maxCommands: 200);
        await MoveToAsync(reader, passage.Id);
        Assert.Equal("Café", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        var focused = false;
        for (var i = 0; i < 8; i++)
        {
            await browser.PressKeyAsync("Tab");
            if ((await browser.CaptureAsync()).FocusedNode?.Name != "Change passage") continue;
            focused = true;
            break;
        }
        Assert.True(focused);
        await browser.PressKeyAsync("Enter");
        var changed = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal(ReaderOutcome.Unknown, changed.Outcome);
        Assert.Contains(changed.TextCursorReset, new TextCursorResetReason?[] { TextCursorResetReason.TextChanged, TextCursorResetReason.NodeRemoved });
        Assert.Null(changed.TextRange);
        var updated = Assert.Single((await browser.CaptureAsync()).Nodes, node => node.Text?.Text == "Updated passage");
        await MoveToAsync(reader, updated.Id);
        Assert.Equal("Updated", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
    }

    private static async Task MoveToAsync(ScreenReaderSession reader, string nodeId)
    {
        var step = await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        for (var i = 0; i < 60 && step.CursorNodeId != nodeId; i++) step = await reader.ExecuteAsync(ReaderCommand.MoveNext);
        Assert.Equal(nodeId, step.CursorNodeId);
    }
}
