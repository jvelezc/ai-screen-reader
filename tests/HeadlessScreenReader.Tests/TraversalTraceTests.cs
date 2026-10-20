using System.Collections.Immutable;
using System.Xml.Linq;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalTraceTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Fact]
    public async Task TraceEdges_CarryExactCommandAndActualOrderedKeyEvents()
    {
        var receipt = await Receipt();
        var attempt = receipt.Attempts[0];
        var events = ImmutableArray.Create(
            Event(10, ReaderEventKind.InputAttempted, "p"), Event(11, ReaderEventKind.InputCompleted, "p"),
            Event(12, ReaderEventKind.InputAttempted, "e"), Event(13, ReaderEventKind.InputCompleted, "e"));
        receipt = receipt with
        {
            Attempts = [attempt with
        {
            Instruction = new(ReaderCommand.Typeahead, "PE"), Events = events,
            Step = attempt.Step! with { Command = ReaderCommand.Typeahead, BrowserKeystrokes = 2 }
        }]
        };
        var graph = TraversalProjection.Build(receipt);
        Assert.Equal("execution-trace-v2", graph.Version);
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(ReaderCommand.Typeahead, edge.Command);
        Assert.Equal(new[] { new TraversalKeyTrace("p", 10, 11), new TraversalKeyTrace("e", 12, 13) }, edge.KeyboardInputs);
        Assert.Equal(2, edge.CompletedKeystrokes);
        Assert.True(edge.InputEvidenceComplete);
        Assert.Equal("p (completed), e (completed)", TraversalTraceDiagram.KeyLabel(edge));
        Assert.Equal("button: Open", graph.Nodes[0].Label);
        Assert.Equal("open", graph.Nodes[0].FocusedNodeId);
        Assert.Equal("o1", edge.SourceObservationId);
        Assert.Equal("o2", edge.ResultObservationId);
    }

    [Fact]
    public async Task BrowseOnlyTrace_RecordsReaderCommandWithoutInventingBrowserShortcut()
    {
        var receipt = await Receipt(ReaderCommand.NextButton);
        var edge = Assert.Single(TraversalProjection.Build(receipt).Edges);
        Assert.Equal(ReaderCommand.NextButton, edge.Command);
        Assert.Empty(edge.KeyboardInputs);
        Assert.Equal(0, edge.CompletedKeystrokes);
        Assert.True(edge.InputEvidenceComplete);
        Assert.Equal("no browser keys observed", TraversalTraceDiagram.KeyLabel(edge));
        var svg = TraversalTraceDiagram.Render(receipt);
        Assert.Contains("NextButton | no browser keys observed", svg);
        Assert.DoesNotContain("b (completed)", svg);
    }

    [Fact]
    public async Task RepeatedKeyAttempts_MatchCompletionsInOrderAndPreserveUncertainLastInput()
    {
        var receipt = await Receipt();
        var attempt = receipt.Attempts[0];
        receipt = receipt with
        {
            Attempts = [attempt with
        {
            Events = [Event(10, ReaderEventKind.InputAttempted, "Tab"), Event(11, ReaderEventKind.InputCompleted, "Tab"),
                Event(12, ReaderEventKind.InputAttempted, "Tab")],
            Step = attempt.Step! with { BrowserKeystrokes = 1, InputMayHaveOccurred = true }
        }]
        };
        var edge = Assert.Single(TraversalProjection.Build(receipt).Edges);
        Assert.Equal(new[] { new TraversalKeyTrace("Tab", 10, 11), new TraversalKeyTrace("Tab", 12, null) }, edge.KeyboardInputs);
        Assert.True(edge.InputMayHaveOccurred);
        Assert.True(edge.InputEvidenceComplete);
        Assert.Equal("Tab (completed), Tab (completion unobserved)", TraversalTraceDiagram.KeyLabel(edge));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedEvidence_IsExplicitAndNeverTreatedAsZeroCostPath(bool attemptOnly)
    {
        var receipt = await Receipt();
        receipt = receipt with
        {
            TimelineTruncated = !attemptOnly,
            Attempts = [receipt.Attempts[0] with { TimelineTruncated = attemptOnly }]
        };
        var edge = Assert.Single(TraversalProjection.Build(receipt).Edges);
        Assert.False(edge.InputEvidenceComplete);
        Assert.Contains("keyboard evidence incomplete", TraversalTraceDiagram.KeyLabel(edge));
        Assert.Single(edge.KeyboardInputs);
    }

    [Fact]
    public async Task OrphanCompletion_StaysObservedWithMissingAttemptMarkedIncomplete()
    {
        var receipt = await Receipt();
        receipt = receipt with { Attempts = [receipt.Attempts[0] with { Events = [Event(10, ReaderEventKind.InputCompleted, "Enter")] }] };
        var edge = Assert.Single(TraversalProjection.Build(receipt).Edges);
        Assert.Equal(new TraversalKeyTrace("Enter", null, 10), Assert.Single(edge.KeyboardInputs));
        Assert.False(edge.InputEvidenceComplete);
        Assert.Contains("Enter (completion observed; attempt missing)", TraversalTraceDiagram.KeyLabel(edge));
    }

    [Fact]
    public async Task MissingInputEvents_CannotHideKnownCompletedKeyboardCalls()
    {
        var receipt = await Receipt();
        receipt = receipt with { Attempts = [receipt.Attempts[0] with { Events = [] }] };
        var edge = Assert.Single(TraversalProjection.Build(receipt).Edges);
        Assert.Empty(edge.KeyboardInputs);
        Assert.Equal(1, edge.CompletedKeystrokes);
        Assert.False(edge.InputEvidenceComplete);
        var diagram = TraversalTraceDiagram.Render(receipt);
        Assert.Contains("keyboard evidence incomplete", diagram);
        Assert.DoesNotContain("#event-0", diagram, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Svg_HasDirectedStateNodesActionEdgesAndSafeEvidenceLinks()
    {
        var receipt = await Receipt();
        var text = TraversalTraceDiagram.Render(receipt, route: 2);
        var svg = XDocument.Parse(text);
        Assert.Equal(Svg + "svg", svg.Root!.Name);
        Assert.Equal("img", svg.Root.Attribute("role")!.Value);
        Assert.Equal(2, svg.Descendants(Svg + "a").Count(a => a.Attribute("href")!.Value.Contains("#o", StringComparison.Ordinal)));
        Assert.Single(svg.Descendants(Svg + "path"), p => p.Attribute("marker-end")?.Value == "url(#arrow)");
        Assert.Contains(svg.Descendants(Svg + "text"), t => t.Value.Contains("Activate | Enter (completed)", StringComparison.Ordinal));
        Assert.All(svg.Descendants(Svg + "a"), a => Assert.StartsWith("route-2.md#", a.Attribute("href")!.Value));
        Assert.Contains("reasonable-effort assessment: not assessed", text);
        Assert.Empty(svg.Descendants(Svg + "script"));
        Assert.DoesNotContain("javascript:", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Svg_WrongDestinationHasOnlyObservedNodesAndDoesNotDrawGoalAsReached()
    {
        var browser = new TraversalRunnerTests.ControlledBrowser();
        browser.AfterKey = () => browser.Snapshot = TraversalRunnerTests.Snapshot(new AccessibleNode("dialog", "dialog", "Address required", IsFocused: true));
        var receipt = await TraversalRunnerTests.Run(browser, TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)));
        var graph = TraversalProjection.Build(receipt);
        Assert.Equal("dialog: Address required", graph.Nodes[^1].Label);
        var svg = TraversalTraceDiagram.Render(receipt);
        Assert.Contains("dialog: Address required", svg);
        Assert.Contains("Unknown | RouteCompleted", svg);
        Assert.DoesNotContain("Objective verified here", svg);
        Assert.Equal(2, graph.Nodes.Length);
    }

    [Fact]
    public async Task Svg_XmlEscapesNamesAndReplacesInvalidXmlControlsWithoutExecutingPageContent()
    {
        var browser = new TraversalRunnerTests.ControlledBrowser();
        var name = "<script>&\" untrusted\u0001\n";
        browser.Snapshot = TraversalRunnerTests.Snapshot(new AccessibleNode("open", "button", name, IsFocused: true));
        var plan = TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.ReadCurrent)) with
        { Precondition = new("button", name), Name = name };
        var receipt = await TraversalRunnerTests.Run(browser, plan);
        var svg = XDocument.Parse(TraversalTraceDiagram.Render(receipt));
        Assert.Empty(svg.Descendants(Svg + "script"));
        Assert.Contains(svg.Descendants(Svg + "title"), t => t.Value.Contains("<script>&\" untrusted", StringComparison.Ordinal));
        Assert.DoesNotContain("\u0001", svg.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Svg_NoObservationAndUnresolvedInputNeverCreateFictitiousResultNode()
    {
        var baseline = await TraversalRunnerTests.Run(new() { FailCapture = true },
            TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)));
        var emptySvg = XDocument.Parse(TraversalTraceDiagram.Render(baseline));
        Assert.Contains(emptySvg.Descendants(Svg + "text"), t => t.Value == "No reliable state observation was retained.");
        Assert.Empty(emptySvg.Descendants(Svg + "a"));
        var browser = new TraversalRunnerTests.ControlledBrowser { AfterKey = () => throw new BrowserOperationException("FAILED") };
        var interrupted = await TraversalRunnerTests.Run(browser, TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)));
        var svg = XDocument.Parse(TraversalTraceDiagram.Render(interrupted));
        Assert.Equal(2, svg.Descendants(Svg + "a").Count());
        Assert.Single(svg.Descendants(Svg + "path"), p => p.Attribute("marker-end") is not null && p.Attribute("stroke-dasharray") is not null);
        var trace = TraversalProjection.Build(interrupted);
        Assert.Empty(trace.Edges);
        Assert.Single(trace.UnresolvedAttempts);
        Assert.Contains(svg.Descendants(Svg + "text"), t => t.Value.Contains("Key attempts: Enter | completed API calls: 0 | possible input: True", StringComparison.Ordinal));
        Assert.Contains(svg.Descendants(Svg + "text"), t => t.Value.Contains("no reliable result node", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NodeLabels_PreserveUnnamedSemanticsAndSeparateFocusFromVirtualCursor()
    {
        var receipt = await Receipt();
        var initial = receipt.Observations[0];
        var snapshot = TraversalRunnerTests.Snapshot(new AccessibleNode("focus", "button", "Focused", IsFocused: true),
            new AccessibleNode("cursor", "button", ""));
        receipt = receipt with { Observations = [initial with { Snapshot = snapshot, CursorNodeId = "cursor" }, receipt.Observations[1]] };
        var node = TraversalProjection.Build(receipt).Nodes[0];
        Assert.Equal("button: (unnamed)", node.Label);
        Assert.Equal("cursor", node.CursorNodeId);
        Assert.Equal("focus", node.FocusedNodeId);
        Assert.DoesNotContain("cart", node.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Svg_LongUnicodeLabelsRemainWellFormedAndKeepFullEvidenceInTitles()
    {
        var receipt = await Receipt();
        var name = string.Concat(Enumerable.Repeat("A\U0001F9ED", 80));
        var initial = receipt.Observations[0];
        receipt = receipt with
        {
            Observations = [initial with
        { Snapshot = TraversalRunnerTests.Snapshot(new AccessibleNode("open", "button", name, IsFocused: true)) }, receipt.Observations[1]]
        };
        var svg = XDocument.Parse(TraversalTraceDiagram.Render(receipt));
        Assert.Contains(svg.Descendants(Svg + "title"), t => t.Value.Contains(name, StringComparison.Ordinal));
        Assert.Contains(svg.Descendants(Svg + "text"), t => t.Value.EndsWith("...", StringComparison.Ordinal));
        Assert.DoesNotContain("\uFFFD", svg.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidRouteNumber_CannotCreateMisleadingArtifactLinks()
    {
        var receipt = await Receipt();
        Assert.Throws<ArgumentOutOfRangeException>(() => TraversalTraceDiagram.Render(receipt, route: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TraversalReport.Render(receipt, route: 0));
    }

    private static Task<TraversalReceipt> Receipt(ReaderCommand command = ReaderCommand.Activate) =>
        TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(command)));
    private static ReaderEvent Event(long sequence, ReaderEventKind kind, string detail) =>
        new(sequence, 2, sequence, kind, ReaderCommand.Typeahead, "doc", "open", detail);
}
