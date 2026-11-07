using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumMockSiteTests
{
    [Theory]
    [InlineData(BundledFixture.MockNamedControlV1, false)]
    [InlineData(BundledFixture.MockUnnamedControlV1, true)]
    public async Task ActualChromiumScreeningPreservesNamedAndUnnamedControlsWithoutTaskClaims(BundledFixture fixture, bool potentialIssue)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var snapshot = await browser.CaptureAsync();
        var report = WebsiteScreening.Inspect(snapshot);
        Assert.Equal(potentialIssue ? 1 : 0, report.Issues.Length);
        if (potentialIssue)
        {
            var issue = Assert.Single(report.Issues);
            Assert.Equal("button", issue.Role); Assert.Equal("", issue.ExposedName);
            Assert.Contains(snapshot.Nodes, n => n.Id == issue.NodeId && n.Name == "");
        }
        Assert.Equal("NotStarted", report.TaskExecution); Assert.Equal("NotAssessed", report.TaskBarrier);
        Assert.False(report.LegalCaseDetermined); Assert.Equal("InsufficientTaskEvidence", report.EvidenceReadiness);
    }

    [Fact]
    public void CatalogueBindsFourAuthoredScenariosToExactEmbeddedSource()
    {
        using var source = typeof(ChromiumMockSiteTests).Assembly.GetManifestResourceStream("MockSitesV1.Manifest.json")!;
        using var manifest = JsonDocument.Parse(source);
        Assert.Equal("hsr-mock-sites-v1", manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal(1, manifest.RootElement.GetProperty("version").GetInt32());
        Assert.True(manifest.RootElement.GetProperty("synthetic").GetBoolean());
        var scenarios = manifest.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.Equal(new[] { "checkout-working", "checkout-keyboard-blocked", "control-named", "control-unnamed" },
            scenarios.Select(s => s.GetProperty("id").GetString()));
        foreach (var scenario in scenarios)
        {
            var fixture = Enum.Parse<BundledFixture>(scenario.GetProperty("fixture").GetString()!);
            Assert.Equal(BundledFixtureCatalog.Describe(fixture).ContentSha256, scenario.GetProperty("contentSha256").GetString());
            Assert.Equal($"/lab/v1/{scenario.GetProperty("id").GetString()}.html", scenario.GetProperty("path").GetString());
            var expected = scenario.GetProperty("expected");
            Assert.Equal("Not assessed by this catalogue", expected.GetProperty("barrierVerdict").GetString());
            var checkout = fixture is BundledFixture.MockCheckoutWorkingV1 or BundledFixture.MockCheckoutKeyboardBlockedV1;
            Assert.Equal(fixture == BundledFixture.MockUnnamedControlV1 ? "" : checkout ? "Place sample order" : "Open sample details",
                expected.GetProperty("actionName").GetString());
            Assert.Equal(checkout ? "Sample order placed" : "Sample details opened", expected.GetProperty("goalName").GetString());
            Assert.Equal(fixture == BundledFixture.MockCheckoutKeyboardBlockedV1 ? "Not observed after Enter or Space" : "Observed focused goal heading",
                expected.GetProperty("keyboardGoal").GetString());
        }
        // Appending cases must not renumber fixtures used by existing clients and receipts.
        Assert.Equal(0, (int)BundledFixture.Foundation);
        Assert.Equal(17, (int)BundledFixture.SyntheticHandoff);
    }

    [Theory]
    [InlineData(BundledFixture.MockCheckoutWorkingV1, "Enter", true)]
    [InlineData(BundledFixture.MockCheckoutWorkingV1, "Space", true)]
    [InlineData(BundledFixture.MockCheckoutKeyboardBlockedV1, "Enter", false)]
    [InlineData(BundledFixture.MockCheckoutKeyboardBlockedV1, "Space", false)]
    public async Task CheckoutKeyboardPairRetainsActualInputsGoalAndGraph(BundledFixture fixture, string key, bool reachesGoal)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var title = reachesGoal ? "Reference checkout" : "Keyboard-blocked checkout";
        var plan = new TraversalPlan("Mock checkout v1", new("heading", title),
            new("heading", "Sample order placed", MustBeFocused: true),
            key == "Enter" ? [new(ReaderCommand.Tab), new(ReaderCommand.Activate)]
                : [new(ReaderCommand.Tab), new(ReaderCommand.EnterFocusMode), new(ReaderCommand.PressKey, key)]);
        var receipt = await new TraversalRunner().RunAsync(browser, plan,
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 8 });
        Assert.Equal(2, receipt.CompletedKeystrokes);
        Assert.Equal(new[] { "Tab", key }, receipt.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail));
        Assert.Equal(reachesGoal ? TraversalReachability.Reached : TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(reachesGoal ? VerificationStatus.Satisfied : VerificationStatus.TimedOut, receipt.Attempts[^1].Step!.Verification);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Equal(reachesGoal, receipt.Timeline.Any(e => e.Kind == ReaderEventKind.GoalVerified));
        Assert.Equal(reachesGoal, receipt.Observations[^1].Snapshot.Nodes.Any(n => n.Role == "heading" && n.Name == "Sample order placed"));
        var graph = TraversalProjection.Build(receipt);
        Assert.Equal(plan.Instructions.Length + 1, graph.Nodes.Length); Assert.Equal(plan.Instructions.Length, graph.Edges.Length);
        Assert.Equal(new[] { "Tab", key }, graph.Edges.SelectMany(e => e.KeyboardInputs).Select(i => i.Key));
        Assert.All(graph.Edges, edge =>
        {
            Assert.True(edge.InputEvidenceComplete);
            Assert.Contains(graph.Nodes, node => node.ObservationId == edge.SourceObservationId);
            Assert.Contains(graph.Nodes, node => node.ObservationId == edge.ResultObservationId);
        });
    }

    [Theory]
    [InlineData(BundledFixture.MockCheckoutWorkingV1, "Sample order placed")]
    [InlineData(BundledFixture.MockCheckoutKeyboardBlockedV1, "Sample order placed")]
    [InlineData(BundledFixture.MockNamedControlV1, "Sample details opened")]
    [InlineData(BundledFixture.MockUnnamedControlV1, "Sample details opened")]
    public async Task FreshPointerComparisonAndResetKeepSyntheticStateExplicit(BundledFixture fixture, string goal)
    {
        // This independent actual mouse experiment is not a reader/MCP input receipt.
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var initial = await browser.CaptureAsync();
        var action = initial.Nodes.First(n => n.Role == "button");
        Assert.DoesNotContain(initial.Nodes, n => n.Role == "heading" && n.Name == goal);
        await browser.DiagnosticPage.Locator("#action").ClickAsync();
        Assert.Equal(goal, (await browser.CaptureAsync()).EffectiveFocusedNode?.Name);
        await browser.DiagnosticPage.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Reset scenario", Exact = true }).ClickAsync();
        var reset = await browser.CaptureAsync();
        Assert.DoesNotContain(reset.Nodes, n => n.Role == "heading" && n.Name == goal);
        Assert.Equal(action.Id, reset.EffectiveFocusedNode?.Id);
        Assert.Equal(action.Name, reset.EffectiveFocusedNode?.Name);
        Assert.Equal(BundledFixtureCatalog.Describe(fixture), browser.Environment.Content);
    }

    [Theory]
    [InlineData(BundledFixture.MockNamedControlV1, "Open sample details")]
    [InlineData(BundledFixture.MockUnnamedControlV1, "")]
    public async Task MissingControlNameStaysMissingWithoutInventingAnActivationFailure(BundledFixture fixture, string name)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var snapshot = await browser.CaptureAsync();
        Assert.Equal(name, snapshot.Nodes.First(n => n.Role == "button").Name);
        using var reader = new ScreenReaderSession(browser);
        var cursor = await reader.ExecuteAsync(ReaderCommand.NextButton);
        Assert.Equal(0, cursor.BrowserKeystrokes);
        if (name.Length == 0) Assert.DoesNotContain("Open sample details", cursor.Announcement);
        else Assert.Contains(name, cursor.Announcement);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var step = await reader.ExecuteAsync(ReaderCommand.Activate,
            expectation: new("heading", "Sample details opened", MustBeFocused: true));
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.Equal(1, step.BrowserKeystrokes);
    }
}
