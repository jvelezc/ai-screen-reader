using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumTraversalTests
{
    [Fact]
    public async Task SuccessfulCommerceRoute_VerifiesActualKeyboardDestinationWithSourceEvidence()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var receipt = await Run(browser, 0);
        Assert.Equal(TraversalExecution.Completed, receipt.Execution);
        Assert.Equal(TraversalReachability.Reached, receipt.Reachability);
        Assert.Equal(TraversalStopReason.RouteCompleted, receipt.StopReason);
        Assert.Equal(new[] { "Tab", "Enter" }, receipt.Timeline.Where(e => e.Kind == ReaderEventKind.InputAttempted).Select(e => e.Detail));
        Assert.Equal(2, receipt.CompletedKeystrokes);
        Assert.Equal(VerificationStatus.Satisfied, receipt.Attempts[^1].Step!.Verification);
        Assert.DoesNotContain(receipt.Observations[0].Snapshot.Nodes, n => n.Role == "heading" && n.Name == "Checkout preview");
        Assert.Equal("Checkout preview", receipt.Observations[^1].Snapshot.EffectiveFocusedNode?.Name);
        Assert.Equal(3, TraversalProjection.Build(receipt).Nodes.Length);
        Assert.Equal(2, TraversalProjection.Build(receipt).Edges.Length);
        Assert.Contains(receipt.Observations[0].Snapshot.Nodes, n => n.Role == "button" && n.Name == "");
        var serialized = JsonSerializer.Serialize(receipt, TraversalDemo.JsonOptions);
        Assert.DoesNotContain("traversal-account-canary", serialized);
        Assert.DoesNotContain("traversal-secret-canary", serialized);
        Assert.Equal(receipt.Observations[0].Snapshot.DocumentId, receipt.Observations[^1].Snapshot.DocumentId);
    }

    [Fact]
    public async Task DisabledCommerceRoute_IsObservedBarrierWithoutAnyBrowserInput()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var receipt = await Run(browser, 1);
        Assert.Equal(TraversalReachability.NotReached, receipt.Reachability);
        Assert.Equal(TraversalBarrier.Observed, receipt.Barrier);
        Assert.Equal(TraversalStopReason.ObservedBarrier, receipt.StopReason);
        Assert.Equal("OBSERVED_DISABLED_CONTROL", receipt.Attempts[^1].Step!.BarrierCode);
        Assert.Equal(0, receipt.CompletedKeystrokes);
        Assert.Equal(0, receipt.AttemptedKeystrokes);
        Assert.Equal("Unavailable checkout", receipt.Observations[^1].Snapshot.Nodes.Single(n => n.Id == receipt.Attempts[^1].Step!.CursorNodeId).Name);
        Assert.Equal(3, receipt.Attempts.Length);
    }

    [Fact]
    public async Task ValidationDialogRoute_PreservesWrongDestinationRatherThanCheckoutSuccess()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var receipt = await Run(browser, 2);
        Assert.Equal(TraversalExecution.Completed, receipt.Execution);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Equal(VerificationStatus.TimedOut, receipt.Attempts[^1].Step!.Verification);
        Assert.Equal(3, receipt.CompletedKeystrokes);
        var last = receipt.Observations[^1];
        Assert.Contains(last.Snapshot.Nodes, n => n.Role == "dialog" && n.Name == "Address required");
        Assert.Equal("Address required", last.Snapshot.EffectiveFocusedNode?.Name);
        Assert.DoesNotContain(last.Snapshot.Nodes, n => n.Role == "heading" && n.Name == "Checkout preview");
        Assert.DoesNotContain(receipt.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
        Assert.Equal(last.Id, TraversalProjection.Build(receipt).Edges[^1].ResultObservationId);
        Assert.Contains("Unknown", TraversalReport.Render(receipt));
    }

    [Fact]
    public async Task UnchangedCommerceRoute_RecordsCompletedInputWithoutGoalOrBarrierClaim()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var receipt = await Run(browser, 3);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Equal(4, receipt.CompletedKeystrokes);
        Assert.Equal(receipt.Observations[^2].Fingerprint, receipt.Observations[^1].Fingerprint);
        Assert.NotEqual(receipt.Observations[^2].Id, receipt.Observations[^1].Id);
        Assert.Equal(VerificationStatus.TimedOut, receipt.Attempts[^1].Step!.Verification);
        Assert.Equal(4, TraversalProjection.Build(receipt).Edges.Length);
        Assert.DoesNotContain(receipt.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
    }

    private static Task<TraversalReceipt> Run(ChromiumFixtureBrowser browser, int index) =>
        new TraversalRunner().RunAsync(browser, TraversalDemo.Plans[index],
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(300), MaxPolls = 5 });
}
