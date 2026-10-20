using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumParagraphTests
{
    [Fact]
    public async Task Paragraph_Chromium_JoinsInlineWordAndPreservesSourcesWithoutInput()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Paragraphs);
        var before = await browser.CaptureAsync();
        using var reader = new ScreenReaderSession(browser);
        var first = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal("Read international text with linked words.", first.Announcement);
        var passage = Assert.IsType<ReaderTextPassage>(first.Passage);
        Assert.True(passage.Fragments.Length > 3);
        Assert.Contains(passage.Fragments, r => r.Text == "inter");
        Assert.Contains(passage.Fragments, r => r.Text == "national");
        Assert.All(passage.Fragments, range =>
        {
            var source = Assert.Single(before.Nodes, n => n.Id == range.NodeId);
            Assert.Equal(source.Text!.Text, range.Text);
            Assert.Equal(source.DocumentId, range.DocumentId);
            Assert.Equal(source.FrameId, range.FrameId);
            Assert.Equal(SemanticSource.ChromiumAccessibility, range.Source);
            Assert.Equal(0, range.Start);
            Assert.Equal(source.Text.Text.Length, range.Length);
        });
        var current = await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph);
        Assert.Equal(first.Announcement, current.Announcement);
        Assert.Equal(first.CursorNodeId, current.CursorNodeId);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousParagraph)).Outcome);
        Assert.Equal(passage.Text, reader.Timeline.First(e => e.Kind == ReaderEventKind.Announcement).Passage!.Text);
        var after = await browser.CaptureAsync();
        Assert.Equal(before.FocusedNode?.Id, after.FocusedNode?.Id);
        Assert.Equal(before.Nodes.Select(n => (n.Id, n.Name)), after.Nodes.Select(n => (n.Id, n.Name)));
        Assert.DoesNotContain(reader.Timeline, e => e.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.GoalVerified);
    }

    [Fact]
    public async Task Paragraph_Chromium_HiddenEditableTextExcludedAndUnicodeBreaksPreserved()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Paragraphs);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal("Visible only.", (await reader.ExecuteAsync(ReaderCommand.NextParagraph)).Announcement);
        var unicode = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal("Unicode é👩‍💻\nnext.", unicode.Announcement);
        Assert.Contains(unicode.Passage!.Fragments, r => r.Text == "\n");
        Assert.Equal(unicode.Announcement.Length, unicode.Passage.Fragments.Sum(r => r.Length));
        var snapshot = await browser.CaptureAsync();
        var serialized = JsonSerializer.Serialize(snapshot) + JsonSerializer.Serialize(reader.Timeline);
        Assert.DoesNotContain("HIDDEN_PARAGRAPH_CANARY", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("PARAGRAPH_EDITABLE_CANARY", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Theory]
    [InlineData("textbox", "Field")]
    [InlineData("image", "Diagram")]
    public async Task Paragraph_Chromium_ControlAndImageBoundariesDoNotProducePartialText(string role, string name)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Paragraphs);
        var snapshot = await browser.CaptureAsync();
        var boundary = Assert.Single(snapshot.Nodes, n => n.Role == role && n.Name == name);
        var byId = snapshot.Nodes.ToDictionary(n => n.Id);
        var owner = byId[boundary.ParentId!];
        while (owner.Role != "paragraph") owner = byId[owner.ParentId!];
        using var reader = new ScreenReaderSession(browser);
        ReaderStep? step = null;
        for (var i = 0; i < 16; i++)
        {
            step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
            if (step.CursorNodeId == owner.Id) break;
        }
        Assert.Equal(owner.Id, step!.CursorNodeId);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Equal("PARAGRAPH_CONTENT_UNSUPPORTED", step.ErrorCode);
        Assert.Null(step.Passage);
        Assert.DoesNotContain("Before", step.Announcement, StringComparison.Ordinal);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
        var next = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.NotEqual(owner.Id, next.CursorNodeId);
    }

    [Fact]
    public async Task Paragraph_Chromium_CurrentLinkReanchorsWithoutActivationAndPreviousLeavesContainer()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Paragraphs);
        using var reader = new ScreenReaderSession(browser);
        var link = await reader.ExecuteAsync(ReaderCommand.NextLink);
        Assert.StartsWith("linked words, link", link.Announcement, StringComparison.Ordinal);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousParagraph)).Outcome);
        var paragraph = await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph);
        Assert.Equal("Read international text with linked words.", paragraph.Announcement);
        Assert.NotEqual(link.CursorNodeId, paragraph.CursorNodeId);
        Assert.Equal("Visible only.", (await reader.ExecuteAsync(ReaderCommand.NextParagraph)).Announcement);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task Paragraph_Chromium_ChildDocumentPassageDoesNotMixParentSourcesOrWrap()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Paragraphs);
        using var reader = new ScreenReaderSession(browser);
        var parent = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        var child = await FindParagraph(reader, "Child paragraph.");
        Assert.NotEqual(parent.Passage!.DocumentId, child.Passage!.DocumentId);
        Assert.NotEqual(parent.Passage.FrameId, child.Passage.FrameId);
        Assert.All(child.Passage.Fragments, r => { Assert.Equal(child.Passage.DocumentId, r.DocumentId); Assert.Equal(child.Passage.FrameId, r.FrameId); });
        var boundary = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(ReaderOutcome.NoMatch, boundary.Outcome);
        Assert.Equal(child.CursorNodeId, boundary.CursorNodeId);
        Assert.Equal("Last parent paragraph.", (await reader.ExecuteAsync(ReaderCommand.PreviousParagraph)).Announcement);
        var observation = reader.Timeline.Single(e => e.Kind == ReaderEventKind.Announcement && e.Passage?.Text == "Child paragraph.");
        Assert.Equal(child.Passage.DocumentId, observation.Passage!.DocumentId);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task Paragraph_Chromium_ExternalMutationAndRemovalCannotReuseOldPassage()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Paragraphs);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var original = await FindParagraph(reader, "Original passage.");
        await browser.PressKeyAsync("Enter");
        var updated = await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph);
        Assert.Equal(ReaderOutcome.Observed, updated.Outcome);
        Assert.Equal("Updated paragraph.", updated.Passage!.Text);
        Assert.Equal(original.CursorNodeId, updated.CursorNodeId);
        Assert.Equal("Original passage.", original.Passage!.Text);
        await browser.PressKeyAsync("Tab");
        await browser.PressKeyAsync("Enter");
        var removed = await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph);
        Assert.Equal(ReaderOutcome.NoMatch, removed.Outcome);
        Assert.Null(removed.CursorNodeId);
        Assert.Null(removed.Passage);
        Assert.Equal("Original passage.", reader.Timeline.First(e => e.Passage?.Text == "Original passage.").Passage!.Text);
    }

    [Fact]
    public async Task Paragraph_Chromium_NamedEmptyLinkCannotBecomeInventedText()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Paragraphs);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextLink);
        var link = await reader.ExecuteAsync(ReaderCommand.NextLink);
        Assert.Equal("Named empty link, link", link.Announcement);
        var paragraph = await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph);
        Assert.Equal(ReaderOutcome.Unknown, paragraph.Outcome);
        Assert.Equal("PARAGRAPH_LINK_TEXT_UNOBSERVED", paragraph.ErrorCode);
        Assert.Null(paragraph.Passage);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    private static async Task<ReaderStep> FindParagraph(ScreenReaderSession reader, string text)
    {
        for (var i = 0; i < 16; i++)
        {
            var step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
            if (step.Passage?.Text == text) return step;
            Assert.NotNull(step.CursorNodeId);
        }
        throw new InvalidOperationException("Expected fixture paragraph was not found.");
    }
}
