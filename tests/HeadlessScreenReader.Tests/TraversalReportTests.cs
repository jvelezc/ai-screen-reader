using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using System.Text.Json;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalReportTests
{
    [Fact]
    public async Task Report_LinksEveryObservedEdgeAndEventWithoutEnablingReplay()
    {
        var browser = new TraversalRunnerTests.ControlledBrowser();
        browser.AfterKey = () => browser.Snapshot = TraversalRunnerTests.Snapshot(new AccessibleNode("dest", "heading", "Destination"));
        var receipt = await TraversalRunnerTests.Run(browser, TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)));
        var before = browser.Keys.ToArray();
        var report = TraversalReport.Render(receipt);
        Assert.Contains("| Completed | Reached | NoneObserved | Allowed | RouteCompleted |", report);
        Assert.Contains("[o1](#o1)", report);
        Assert.Contains("[o2](#o2)", report);
        Assert.Contains("<a id=\"o2\"></a>", report);
        Assert.Contains("GoalVerified", report);
        foreach (var item in receipt.Timeline) Assert.Contains($"<a id=\"event-{item.Sequence}\"></a>", report);
        Assert.Contains("Structured reader observations and events only. No recording", report);
        Assert.Equal(before, browser.Keys);
        Assert.DoesNotContain("<script", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Report_EscapesUntrustedNamesAndNeverCreatesExecutablePageMarkup()
    {
        var browser = new TraversalRunnerTests.ControlledBrowser();
        var name = "<script>alert('x')</script>|[bad](javascript:evil)\n# injected";
        browser.Snapshot = TraversalRunnerTests.Snapshot(new AccessibleNode("open", "button", name, IsFocused: true));
        var plan = TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.ReadCurrent)) with
        { Name = name, Precondition = new("button", name) };
        var receipt = await TraversalRunnerTests.Run(browser, plan);
        var report = TraversalReport.Render(receipt);
        Assert.DoesNotContain("<script", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", report);
        Assert.Contains("\\|\\[bad\\]", report);
        Assert.DoesNotContain("\n# injected", report);
        Assert.DoesNotContain("[bad](javascript:evil)", report);
        Assert.Contains("| Completed | Unknown | NoneObserved | Allowed | RouteCompleted |", report);
    }

    [Fact]
    public async Task Projection_RejectsMissingAndDuplicateEvidenceReferences()
    {
        var receipt = await TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.ReadCurrent)));
        Assert.Throws<ArgumentException>(() => TraversalProjection.Build(receipt with
        { Observations = [receipt.Observations[0]] }));
        Assert.Throws<ArgumentException>(() => TraversalProjection.Build(receipt with
        { Observations = [.. receipt.Observations, receipt.Observations[0]] }));
        Assert.Throws<ArgumentException>(() => TraversalProjection.Build(receipt with
        { Attempts = [receipt.Attempts[0] with { SourceObservationId = "missing" }] }));
        Assert.Throws<ArgumentException>(() => TraversalProjection.Build(receipt with
        { Attempts = [receipt.Attempts[0], receipt.Attempts[0]] }));
    }

    [Fact]
    public async Task Receipt_RoundTripsStructuredEvidenceAndProjectsSameObservedGraph()
    {
        var receipt = await TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)));
        var serialized = JsonSerializer.Serialize(receipt, TraversalDemo.JsonOptions);
        var restored = JsonSerializer.Deserialize<TraversalReceipt>(serialized, TraversalDemo.JsonOptions)!;
        Assert.Equal("hsr-traversal-v1", restored.Schema);
        Assert.Equal(receipt.RunId, restored.RunId);
        Assert.Equal(TraversalReachability.Unknown, restored.Reachability);
        Assert.Equal(VerificationStatus.TimedOut, restored.Attempts[0].Step!.Verification);
        Assert.Equal(receipt.Attempts[0].Events.Select(e => (e.Sequence, e.Kind)), restored.Attempts[0].Events.Select(e => (e.Sequence, e.Kind)));
        Assert.Equal(receipt.Observations.Select(o => o.Fingerprint), restored.Observations.Select(o => o.Fingerprint));
        Assert.Equal(TraversalProjection.Build(receipt).Edges.ToArray(), TraversalProjection.Build(restored).Edges.ToArray());
        Assert.False(restored.HasRecording);
    }
}
