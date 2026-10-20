using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class ParagraphSessionTests
{
    [Fact]
    public async Task ParagraphRead_JoinsExactRunsAndRetainsImmutableProvenance()
    {
        var browser = new ParagraphBrowser(Snapshot("Read ", "inter", "national é👩‍💻\r\n", "text."));
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal("Read international é👩‍💻\r\ntext.", step.Announcement);
        Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
        Assert.Equal("p", step.CursorNodeId);
        Assert.Equal("root", step.FocusedNodeId);
        var passage = Assert.IsType<ReaderTextPassage>(step.Passage);
        Assert.Equal("doc", passage.DocumentId);
        Assert.Equal("main", passage.FrameId);
        Assert.Equal("p", passage.ContainerNodeId);
        Assert.Equal(new[] { "t0", "t1", "t2", "t3" }, passage.Fragments.Select(r => r.NodeId));
        Assert.All(passage.Fragments, r => { Assert.Equal(0, r.Start); Assert.Equal(r.Text.Length, r.Length); Assert.Equal(TextGranularity.TextRun, r.Granularity); Assert.Equal(SemanticSource.ChromiumAccessibility, r.Source); });
        Assert.Same(passage, reader.Timeline.Single(e => e.Kind == ReaderEventKind.Announcement).Passage);
        Assert.Null(step.TextRange);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParagraphNavigation_BothDirectionsDoesNotWrapOrDispatch(bool backwards)
    {
        var nodes = new List<AccessibleNode> { Node("root", "RootWebArea", null) };
        foreach (var id in new[] { "one", "two", "three" })
        {
            nodes.Add(Node(id, "paragraph", "root"));
            nodes.Add(Node(id + "text", "StaticText", id, id));
        }
        nodes.Add(Node("tail", "button", "root"));
        var browser = new ParagraphBrowser(new("doc", nodes.ToImmutableArray(), []));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(backwards ? ReaderCommand.DocumentEnd : ReaderCommand.DocumentStart);
        var expected = backwards ? new[] { "three", "two", "one" } : ["one", "two", "three"];
        var command = backwards ? ReaderCommand.PreviousParagraph : ReaderCommand.NextParagraph;
        foreach (var id in expected)
        {
            var step = await reader.ExecuteAsync(command);
            Assert.Equal(id, step.CursorNodeId);
            Assert.Equal(id, step.Announcement);
        }
        var end = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.NoMatch, end.Outcome);
        Assert.Equal(expected[^1], end.CursorNodeId);
        Assert.Null(end.Passage);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ReadCurrentParagraph_ReanchorsDescendantAndClearsSingleRunCursor()
    {
        var browser = new ParagraphBrowser(Snapshot("Hello ", "world"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        var word = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("Hello", word.Announcement);
        Assert.Equal("t0", word.CursorNodeId);
        var paragraph = await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph);
        Assert.Equal("Hello world", paragraph.Announcement);
        Assert.Equal("p", paragraph.CursorNodeId);
        Assert.Equal(ReaderOutcome.Observed, paragraph.Outcome);
        Assert.Equal("Hello", (await reader.ExecuteAsync(ReaderCommand.NextWord)).Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousParagraph)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData("button")]
    [InlineData("textbox")]
    [InlineData("image")]
    [InlineData("table")]
    [InlineData("paragraph")]
    public async Task ParagraphBoundaries_UnsupportedContentCannotProducePartialPassage(string role)
    {
        var snapshot = Snapshot("Before ", " after");
        var browser = new ParagraphBrowser(snapshot with { Nodes = snapshot.Nodes.Add(Node("boundary", role, "p")) });
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Equal("PARAGRAPH_CONTENT_UNSUPPORTED", step.ErrorCode);
        Assert.Equal("p", step.CursorNodeId);
        Assert.Null(step.Passage);
        Assert.DoesNotContain("Before", step.Announcement, StringComparison.Ordinal);
        Assert.Null(step.BarrierCode);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ParagraphOwnership_UnobservedOrConflictingSourcesRemainUnknown(int scenario)
    {
        var snapshot = Snapshot("First", " second");
        snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.Select(node => scenario switch
        {
            0 when node.Id == "p" => node with { ParentId = "missing" },
            1 when node.Id == "p" => node with { ParentId = "p" },
            2 when node.Id == "t1" => node with { DocumentId = "foreign" },
            3 when node.Id == "t1" => node with { FrameId = "other-frame" },
            4 when node.Id == "t1" => node with { Text = null },
            5 when node.Id == "p" => node with { DocumentId = null },
            6 when node.Id == "root" => node with { DocumentId = "wrong-root" },
            _ => node
        }).ToImmutableArray()
        };
        var browser = new ParagraphBrowser(snapshot);
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Null(step.Passage);
        Assert.Null(step.BarrierCode);
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
    }

    [Theory]
    [InlineData(127, ReaderOutcome.CursorMoved)]
    [InlineData(128, ReaderOutcome.CursorMoved)]
    [InlineData(129, ReaderOutcome.Unsupported)]
    public async Task ParagraphLimits_RunCountIsBounded(int count, ReaderOutcome expected)
    {
        using var reader = new ScreenReaderSession(new ParagraphBrowser(Snapshot(Enumerable.Repeat("x", count).ToArray())));
        var step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(expected, step.Outcome);
        if (expected == ReaderOutcome.CursorMoved)
        {
            Assert.Equal(count, step.Passage!.Fragments.Length);
            Assert.Equal(new string('x', count), step.Announcement);
        }
        else { Assert.Null(step.Passage); Assert.Equal("PARAGRAPH_TEXT_LIMIT", step.ErrorCode); }
    }

    [Theory]
    [InlineData(32767, ReaderOutcome.CursorMoved)]
    [InlineData(32768, ReaderOutcome.CursorMoved)]
    [InlineData(32769, ReaderOutcome.Unsupported)]
    public async Task ParagraphLimits_Utf16LengthIsBounded(int length, ReaderOutcome expected)
    {
        using var reader = new ScreenReaderSession(new ParagraphBrowser(Snapshot("prefix", new string('x', length - 6))));
        var step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(expected, step.Outcome);
        if (expected == ReaderOutcome.CursorMoved) Assert.Equal(length, step.Passage!.Text.Length);
        else { Assert.Null(step.Passage); Assert.Equal("PARAGRAPH_TEXT_LIMIT", step.ErrorCode); }
    }

    [Theory]
    [InlineData(ReaderCommand.ReadCurrentParagraph)]
    [InlineData(ReaderCommand.NextParagraph)]
    [InlineData(ReaderCommand.PreviousParagraph)]
    public async Task ParagraphCommands_FocusModeAndSemanticGoalsCannotDispatchOrVerify(ReaderCommand command)
    {
        var browser = new ParagraphBrowser(Snapshot("Read"));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(ReaderOutcome.Unsupported, (await reader.ExecuteAsync(command)).Outcome);
        await reader.ExecuteAsync(ReaderCommand.EnterBrowseMode);
        var captures = browser.Captures;
        var goal = await reader.ExecuteAsync(command, expectation: new("paragraph", ""));
        Assert.Equal(ReaderOutcome.Unsupported, goal.Outcome);
        Assert.Equal(VerificationStatus.NotRequested, goal.Verification);
        Assert.Equal(captures, browser.Captures);
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ParagraphRefresh_UsesFreshTextAndNeverReturnsRemovedDocumentPassage(int scenario)
    {
        var browser = new ParagraphBrowser(Snapshot("Original"));
        using var reader = new ScreenReaderSession(browser);
        var original = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        browser.Snapshot = scenario switch
        {
            0 => Snapshot("Updated"),
            1 => new("doc", [Node("root", "RootWebArea", null)], []),
            _ => new("replacement", [Node("new-root", "RootWebArea", null) with { DocumentId = "replacement" }], [])
        };
        var fresh = await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph);
        Assert.Equal(scenario == 0 ? ReaderOutcome.Observed : ReaderOutcome.NoMatch, fresh.Outcome);
        if (scenario == 0) Assert.Equal("Updated", fresh.Passage!.Text);
        else { Assert.Null(fresh.Passage); Assert.Null(fresh.CursorNodeId); }
        Assert.Equal("Original", original.Passage!.Text);
        Assert.Equal("Original", reader.Timeline.First(e => e.Kind == ReaderEventKind.Announcement).Passage!.Text);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ParagraphEmptyAndOutsideAnchor_DoNotBorrowOtherText()
    {
        var browser = new ParagraphBrowser(new("doc", [Node("root", "RootWebArea", null)], []));
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextParagraph)).Outcome);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousParagraph)).Outcome);
        browser.Snapshot = Snapshot("");
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph)).Outcome);
        var empty = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(ReaderOutcome.NoMatch, empty.Outcome);
        Assert.Equal("p", empty.CursorNodeId);
        Assert.Null(empty.Passage);
    }

    [Fact]
    public async Task ParagraphBudget_StopsAdditionalCaptureAndInput()
    {
        var browser = new ParagraphBrowser(Snapshot("Read"));
        using var reader = new ScreenReaderSession(browser, maxCommands: 1);
        await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(ReaderOutcome.BudgetExceeded, (await reader.ExecuteAsync(ReaderCommand.ReadCurrentParagraph)).Outcome);
        Assert.Equal(1, browser.Captures);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ParagraphOverlappingRuns_CannotDuplicateSourceText()
    {
        var snapshot = Snapshot("Outer", "Duplicate");
        snapshot = snapshot with { Nodes = snapshot.Nodes.Select(n => n.Id == "t1" ? n with { ParentId = "t0" } : n).ToImmutableArray() };
        using var reader = new ScreenReaderSession(new ParagraphBrowser(snapshot));
        var step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal("PARAGRAPH_OVERLAPPING_TEXT", step.ErrorCode);
        Assert.Null(step.Passage);
    }

    [Fact]
    public async Task ParagraphNamedLink_WithoutExposedTextCannotInventOrOmitItsLabel()
    {
        var snapshot = Snapshot("Before ");
        snapshot = snapshot with { Nodes = snapshot.Nodes.Add(Node("link", "link", "p") with { Name = "Link label" }) };
        using var reader = new ScreenReaderSession(new ParagraphBrowser(snapshot));
        var step = await reader.ExecuteAsync(ReaderCommand.NextParagraph);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal("PARAGRAPH_LINK_TEXT_UNOBSERVED", step.ErrorCode);
        Assert.Null(step.Passage);
        Assert.DoesNotContain("Link label", step.Announcement, StringComparison.Ordinal);
    }

    private static AccessibleNode Node(string id, string role, string? parent, string? text = null) =>
        new(id, role, text ?? "", IsFocused: id is "root" or "new-root", ParentId: parent,
            DocumentId: "doc", FrameId: "main", Text: text is null ? null : new(text, SemanticSource.ChromiumAccessibility));

    private static AccessibilitySnapshot Snapshot(params string[] runs) => new("doc",
        new[] { Node("root", "RootWebArea", null), Node("p", "paragraph", "root") }
            .Concat(runs.Select((text, index) => Node("t" + index, "StaticText", "p", text))).ToImmutableArray(), []);

    private sealed class ParagraphBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public int Captures { get; private set; }
        public List<string> Keys { get; } = [];
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) { Captures++; return Task.FromResult(Snapshot); }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) { Keys.Add(key); return Task.CompletedTask; }
    }
}
