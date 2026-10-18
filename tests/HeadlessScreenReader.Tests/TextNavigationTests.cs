using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class TextNavigationTests
{
    [Fact]
    public async Task Characters_GraphemesAndCrLf_PreserveUtf16OffsetsAndReverseWithoutWrapping()
    {
        var browser = new TextBrowser(Snapshot("Aé👩‍💻\r\nZ"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var expected = new[] { ("A", 0, 1), ("é", 1, 2), ("👩‍💻", 3, 5), ("\r\n", 8, 2), ("Z", 10, 1) };
        ReaderStep? last = null;
        foreach (var (text, start, length) in expected)
        {
            last = await reader.ExecuteAsync(ReaderCommand.NextCharacter);
            AssertRange(last, text, start, length, TextGranularity.Character);
        }
        var boundary = await reader.ExecuteAsync(ReaderCommand.NextCharacter);
        Assert.Equal(ReaderOutcome.NoMatch, boundary.Outcome);
        Assert.Equal(last!.TextRange, boundary.TextRange);
        Assert.Equal("Z", (await reader.ExecuteAsync(ReaderCommand.ReadCurrentText)).Announcement);
        foreach (var (text, start, length) in expected.Reverse().Skip(1))
            AssertRange(await reader.ExecuteAsync(ReaderCommand.PreviousCharacter), text, start, length, TextGranularity.Character);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousCharacter)).Outcome);
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.GoalVerified);
    }

    [Fact]
    public async Task Words_UnicodeApostrophesAndPunctuation_HaveDeterministicBoundaries()
    {
        var browser = new TextBrowser(Snapshot("  can't, café_2 👩‍💻 中文 42"));
        using var reader = new ScreenReaderSession(browser);
        var expected = new[] { ("can't", 2, 5), (",", 7, 1), ("café_2", 9, 6), ("👩‍💻", 16, 5), ("中文", 22, 2), ("42", 25, 2) };
        foreach (var (text, start, length) in expected)
            AssertRange(await reader.ExecuteAsync(ReaderCommand.NextWord), text, start, length, TextGranularity.Word);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextWord)).Outcome);
        foreach (var (text, start, length) in expected.Reverse().Skip(1))
            AssertRange(await reader.ExecuteAsync(ReaderCommand.PreviousWord), text, start, length, TextGranularity.Word);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task Lines_CrLfBlankAndTrailingLines_AreDistinctFromNoMatch()
    {
        var browser = new TextBrowser(Snapshot("one\r\n\r\ntwo\rfour\n"));
        using var reader = new ScreenReaderSession(browser);
        foreach (var (text, start, length) in new[] { ("one", 0, 3), ("", 5, 0), ("two", 7, 3), ("four", 11, 4), ("", 16, 0) })
            AssertRange(await reader.ExecuteAsync(ReaderCommand.NextLine), text, start, length, TextGranularity.Line);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextLine)).Outcome);
        AssertRange(await reader.ExecuteAsync(ReaderCommand.PreviousLine), "four", 11, 4, TextGranularity.Line);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task TextRuns_CrossingBoundaries_SkipsDuplicateInlineAndControlNames()
    {
        var browser = new TextBrowser(new("document", [
            new("first", "StaticText", "a b", Text: new("a b", SemanticSource.ChromiumAccessibility)),
            new("duplicate", "InlineTextBox", "a b", ParentId: "first", Text: new("a b", SemanticSource.ChromiumAccessibility)),
            new("control", "textbox", "Control", Text: new("excluded", SemanticSource.ChromiumAccessibility)),
            new("last", "StaticText", "c", Text: new("c", SemanticSource.ChromiumAccessibility))
        ], []));
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal("a", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Equal("b", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Equal("c", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextWord)).Outcome);
        Assert.Equal("b", (await reader.ExecuteAsync(ReaderCommand.PreviousWord)).Announcement);
        Assert.DoesNotContain(reader.Timeline, item => item.Detail.Contains("excluded", StringComparison.Ordinal));
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextNavigation_UnsetCursor_StartsAtDirectionalBoundary(bool backwards)
    {
        var browser = new TextBrowser(Snapshot("first last"));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(backwards ? ReaderCommand.PreviousWord : ReaderCommand.NextWord);
        Assert.Equal(backwards ? "last" : "first", step.Announcement);
        Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task SemanticAnchor_RequiresObservedDescendantAndDoesNotReadNames()
    {
        var browser = new TextBrowser(new("document", [
            new("heading", "heading", "Name only", HeadingLevel: 1),
            new("unrelated", "StaticText", "Unrelated", ParentId: "missing", Text: new("Unrelated", SemanticSource.ChromiumAccessibility))
        ], []));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.ReadCurrentText)).Outcome);
        browser.Snapshot = browser.Snapshot with { Nodes = [browser.Snapshot.Nodes[0], browser.Snapshot.Nodes[1] with { ParentId = "heading" }] };
        var text = await reader.ExecuteAsync(ReaderCommand.ReadCurrentText);
        Assert.Equal("Unrelated", text.Announcement);
        Assert.Equal(TextGranularity.TextRun, text.TextRange?.Granularity);
        Assert.Equal("unrelated", text.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(TextCursorResetReason.TextChanged)]
    [InlineData(TextCursorResetReason.NodeRemoved)]
    [InlineData(TextCursorResetReason.DocumentChanged)]
    public async Task TextCursor_ChangedObservation_RequiresReanchorBeforeReadingFreshText(TextCursorResetReason reason)
    {
        var browser = new TextBrowser(Snapshot("old"));
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal("old", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        browser.Snapshot = Snapshot("new", reason == TextCursorResetReason.NodeRemoved ? "replacement" : "text",
            reason == TextCursorResetReason.DocumentChanged ? "replacement-document" : "document");
        var fresh = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal(ReaderOutcome.Unknown, fresh.Outcome);
        Assert.Equal(reason, fresh.TextCursorReset);
        Assert.Null(fresh.TextRange);
        var stillInvalid = await reader.ExecuteAsync(ReaderCommand.ReadCurrentText);
        Assert.Equal(ReaderOutcome.Unknown, stillInvalid.Outcome);
        Assert.Equal(reason, stillInvalid.TextCursorReset);
        Assert.Null(stillInvalid.TextRange);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var anchored = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("new", anchored.Announcement);
        Assert.Equal(browser.Snapshot.DocumentId, anchored.TextRange?.DocumentId);
        Assert.Equal(0, anchored.TextRange?.Start);
        Assert.Null(anchored.TextCursorReset);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SemanticAnchor_BrokenAncestry_DoesNotInventText(bool cycle)
    {
        var browser = new TextBrowser(new("document", [
            new("heading", "heading", "Name only", HeadingLevel: 1),
            new("other", "group", "", ParentId: cycle ? "other" : "missing"),
            new("text", "StaticText", "Unrelated", ParentId: "other", Text: new("Unrelated", SemanticSource.ChromiumAccessibility))
        ], []));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrentText);
        Assert.Equal(ReaderOutcome.NoMatch, step.Outcome);
        Assert.Equal("heading", step.CursorNodeId);
        Assert.Null(step.TextRange);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task Words_BoundaryApostrophes_AreSeparateFromInternalApostrophes()
    {
        var browser = new TextBrowser(Snapshot("'we’re' 'ends'"));
        using var reader = new ScreenReaderSession(browser);
        foreach (var expected in new[] { "'", "we’re", "'", "'", "ends", "'" })
            Assert.Equal(expected, (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextWord)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task SemanticMovement_ClearsTextSelectionAndReadCurrentPreservesExactWhitespace()
    {
        var browser = new TextBrowser(Snapshot("  exact  "));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextCharacter);
        await reader.ExecuteAsync(ReaderCommand.NextCharacter);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var current = await reader.ExecuteAsync(ReaderCommand.ReadCurrentText);
        Assert.Equal("  exact  ", current.Announcement);
        Assert.Equal(TextGranularity.TextRun, current.TextRange?.Granularity);
        AssertRange(await reader.ExecuteAsync(ReaderCommand.NextCharacter), " ", 0, 1, TextGranularity.Character);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(ReaderCommand.ReadCurrentText)]
    [InlineData(ReaderCommand.NextCharacter)]
    [InlineData(ReaderCommand.PreviousCharacter)]
    [InlineData(ReaderCommand.NextWord)]
    [InlineData(ReaderCommand.PreviousWord)]
    [InlineData(ReaderCommand.NextLine)]
    [InlineData(ReaderCommand.PreviousLine)]
    public async Task TextNavigation_FocusMode_IsUnsupportedWithoutInput(ReaderCommand command)
    {
        var snapshot = Snapshot("readable");
        var browser = new TextBrowser(snapshot with { Nodes = snapshot.Nodes.Add(new("focus", "textbox", "Field", IsFocused: true)) });
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Null(step.TextRange);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("absent")]
    public async Task TextNavigation_NoWords_ReturnsNoMatchWithoutInventedText(string scenario)
    {
        var snapshot = scenario == "absent" ? new AccessibilitySnapshot("document", [], [])
            : Snapshot(scenario == "empty" ? "" : "   ");
        var browser = new TextBrowser(snapshot);
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal(ReaderOutcome.NoMatch, step.Outcome);
        Assert.Null(step.TextRange);
        Assert.Null(step.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task TextNavigation_GranularityChange_UsesSelectedStartOffset()
    {
        var browser = new TextBrowser(Snapshot("ab cd\nef"));
        using var reader = new ScreenReaderSession(browser);
        AssertRange(await reader.ExecuteAsync(ReaderCommand.NextWord), "ab", 0, 2, TextGranularity.Word);
        AssertRange(await reader.ExecuteAsync(ReaderCommand.NextCharacter), "b", 1, 1, TextGranularity.Character);
        AssertRange(await reader.ExecuteAsync(ReaderCommand.NextWord), "cd", 3, 2, TextGranularity.Word);
        AssertRange(await reader.ExecuteAsync(ReaderCommand.NextLine), "ef", 6, 2, TextGranularity.Line);
        AssertRange(await reader.ExecuteAsync(ReaderCommand.PreviousWord), "cd", 3, 2, TextGranularity.Word);
        AssertRange(await reader.ExecuteAsync(ReaderCommand.PreviousLine), "ab cd", 0, 5, TextGranularity.Line);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task TextNavigation_IsolatedSurrogate_IsPreservedWithoutCrashing()
    {
        var malformed = new string(['A', '\ud800', 'B']);
        var browser = new TextBrowser(Snapshot(malformed));
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal("A", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Equal(malformed.Substring(1, 1), (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Equal("B", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task TextNavigation_SemanticExpectationAndBudget_DoNotDispatchInput()
    {
        var browser = new TextBrowser(Snapshot("text"));
        using var reader = new ScreenReaderSession(browser, maxCommands: 1);
        var unsupported = await reader.ExecuteAsync(ReaderCommand.NextWord, expectation: new("heading", "Goal"));
        Assert.Equal(ReaderOutcome.Unsupported, unsupported.Outcome);
        Assert.Equal(0, browser.Captures);
        Assert.Equal("text", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Equal(ReaderOutcome.BudgetExceeded, (await reader.ExecuteAsync(ReaderCommand.NextWord)).Outcome);
        Assert.Empty(browser.Keys);
    }

    private static void AssertRange(ReaderStep step, string text, int start, int length, TextGranularity granularity)
    {
        Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
        Assert.Equal(new ReaderTextRange("document", "text", start, length, text, granularity,
            SemanticSource.ChromiumAccessibility), step.TextRange);
        Assert.Equal(text, step.Announcement);
        Assert.Equal("text", step.CursorNodeId);
        Assert.Equal(0, step.BrowserKeystrokes);
    }

    private static AccessibilitySnapshot Snapshot(string text, string id = "text", string document = "document") =>
        new(document, [new("root", "RootWebArea", ""), new(id, "StaticText", text.Trim(), ParentId: "root",
            Text: new(text, SemanticSource.ChromiumAccessibility))], []);

    private sealed class TextBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public List<string> Keys { get; } = [];
        public int Captures { get; private set; }
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        { Captures++; return Task.FromResult(Snapshot); }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        { Keys.Add(key); return Task.CompletedTask; }
    }
}
