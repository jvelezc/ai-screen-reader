using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public static class TraversalDemo
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ImmutableArray<TraversalPlan> Plans => AuthoredKeyboardRoutes.Plans;

    public static async Task<int> RunAsync(bool headed, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "traversal", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var receipts = new List<TraversalReceipt>();
        for (var index = 0; index < Plans.Length; index++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            // Alternate routes are independent experiments; browser input is never replayed on a mutated context.
            await using var browser = await ChromiumFixtureBrowser.LaunchAsync(headed: headed,
                cancellationToken: cancellationToken, fixture: BundledFixture.Traversal);
            var receipt = await new TraversalRunner().RunAsync(browser, Plans[index],
                waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 },
                cancellationToken: cancellationToken);
            receipts.Add(receipt);
            await TraversalArtifactWriter.WriteAsync(folder, index + 1, receipt, browser.Environment);
        }
        var verified = receipts.Count > 0 && receipts[0].Reachability == TraversalReachability.Reached;
        var blocked = receipts.Count > 1 && receipts[1].Barrier == TraversalBarrier.Observed && receipts[1].CompletedKeystrokes == 0;
        var validation = receipts.Count > 2 && receipts[2].Reachability == TraversalReachability.Unknown
            && receipts[2].Observations[^1].Snapshot.Nodes.Any(n => n.Role == "dialog" && n.Name == "Address required")
            && !receipts[2].Observations[^1].Snapshot.Nodes.Any(n => n.Role == "heading" && n.Name == "Checkout preview");
        var unchanged = receipts.Count > 3 && receipts[3].Reachability == TraversalReachability.Unknown
            && receipts[3].Attempts[^1].Step?.Verification == VerificationStatus.TimedOut
            && receipts[3].Barrier == TraversalBarrier.NoneObserved;
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "index.md"),
            "# Synthetic shop traversal reports\n\nFresh browser per route; original input is never replayed.\n\n"
            + string.Join("\n", receipts.Select((r, i) => $"- [{r.Plan.Name}](route-{i + 1}.md): {r.Reachability}; {r.StopReason}. [Node and edge trace](route-{i + 1}.svg)"))
            + "\n\nRecording, diagnostics, human intervention and hosted persistence remain open milestone gates.\n");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            TraversalGoalVerified = verified,
            DisabledRouteObserved = blocked,
            ActualDialogRetained = validation,
            UnchangedRemainsUnknown = unchanged,
            Report = Path.Combine(folder, "index.md"),
            Profile = ScreenReader.ScreenReaderSession.Profile,
            Schema = "hsr-traversal-v1",
            Routes = receipts.Count
        }));
        if (cancellationToken.IsCancellationRequested) return 130;
        return verified && blocked && validation && unchanged ? 0 : 1;
    }
}
