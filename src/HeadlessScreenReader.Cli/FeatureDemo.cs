using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>Separate diagnostic collection on four trusted fresh fixtures; no search or replay.</summary>
public static class FeatureDemo
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "features", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var ready = true;
        var expected = new[] { TraversalReachability.Reached, TraversalReachability.NotReached, TraversalReachability.Unknown, TraversalReachability.Unknown };
        var expectedKeys = new[] { 2, 0, 3, 4 };
        for (var i = 0; i < TraversalDemo.Plans.Length; i++)
        {
            await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: cancellationToken);
            var discovery = await browser.CaptureFeatureDiagnosticsAsync(cancellationToken);
            var receipt = await new TraversalRunner().RunAsync(browser, TraversalDemo.Plans[i],
                waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 }, cancellationToken: cancellationToken);
            var inventory = FeatureProvenance.Build(discovery, receipt);
            ready &= inventory.Features.Length == 5 && inventory.Features.All(f => f.Binding == FeatureSourceBinding.Matched)
                && inventory.Features.Any(f => !f.HasExposedName && f.Candidate.Hint?.Value == "account-settings")
                && receipt.Reachability == expected[i] && receipt.CompletedKeystrokes == expectedKeys[i]
                && (i != 1 || receipt.Barrier == TraversalBarrier.Observed);
            await TraversalArtifactWriter.WriteAsync(folder, i + 1, receipt, browser.Environment, features: discovery);
        }
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "index.md"),
            "# Diagnostic feature provenance\n\nFresh authored experiments. Diagnostic hints are hypotheses; unnamed reader controls remain unnamed.\n\n"
            + string.Join("\n", Enumerable.Range(1, 4).Select(i => $"- [Route {i}: trace and diagnostic candidates](route-{i}.md)"))
            + "\n\nNo autonomous search, feature-success inference, effort score, replay, human control or recording is added.\n");
        Console.WriteLine(JsonSerializer.Serialize(new { FeatureProvenanceReady = ready, Routes = 4, Report = Path.Combine(folder, "index.md") }));
        return ready ? 0 : 3;
    }
}
