using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>An explicit replay request on a fresh trusted shop, never an evidence-viewer action.</summary>
public static class ReplayDemo
{
    public static ReplayEnvironment Environment(FixtureBrowserEnvironment environment) =>
        new(ScreenReader.ScreenReaderSession.Profile, environment.BrowserVersion, environment.PlaywrightVersion,
            environment.SourceUrl is null ? environment.Content?.Fixture.ToString() ?? "Unavailable" : "Public",
            environment.Content?.ContentSha256 ?? "", ControlledReplay.PolicyId, environment.Headed,
            environment.FrameLimits is { } limits ? $"{limits.MaxFrames}:{limits.MaxDepth}" : "Unavailable");

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "replay", Guid.NewGuid().ToString("N")));
        TraversalReceipt historical;
        FixtureBrowserEnvironment recordedEnvironment;
        await using (var original = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: cancellationToken))
        {
            recordedEnvironment = original.Environment;
            historical = await new TraversalRunner().RunAsync(original, ControlledReplay.Plan,
                waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 }, cancellationToken: cancellationToken);
            await TraversalArtifactWriter.WriteAsync(folder, 1, historical, recordedEnvironment);
        }
        var originalPath = Path.Combine(folder, "route-1.json");
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(originalPath));
        await using var current = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: cancellationToken);
        var replay = await new ControlledReplay().RunAsync(current, historical, Environment(recordedEnvironment),
            Environment(current.Environment), cancellationToken);
        if (replay.Result is { } result) await TraversalArtifactWriter.WriteAsync(folder, 2, result, current.Environment);
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "replay.json"), JsonSerializer.Serialize(replay, TraversalDemo.JsonOptions));
        var finalBytes = await File.ReadAllBytesAsync(originalPath);
        var unchanged = hash.SequenceEqual(SHA256.HashData(finalBytes));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "index.md"),
            "# Explicit fresh-run replay\n\n[Historical route](route-1.md) · [Replay eligibility and result](replay.json)\n\n"
            + $"Disposition: {replay.Disposition}. Code: {replay.Code}. Original receipt unchanged: {unchanged}.\n\n"
            + (replay.Result is null ? "No new traversal result was produced.\n" : $"[Independent new traversal](route-2.md): {replay.Result.Execution}; destination {replay.Result.Reachability}; {replay.Result.CompletedKeystrokes} completed keys.\n")
            + "\nHistorical and new goal verification are separate. No passive-viewer trigger, blind key playback, public replay or general recording is enabled.\n");
        var ready = replay.Disposition == ReplayDisposition.AttemptRecorded && replay.Result is
        { Reachability: TraversalReachability.Reached, Execution: TraversalExecution.Completed, CompletedKeystrokes: 2 }
            && replay.Result.RunId != historical.RunId && replay.HistoricalSource == replay.FreshSource && unchanged;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            FreshReplayReady = ready,
            replay.Disposition,
            replay.Code,
            replay.RequestId,
            replay.HistoricalRunId,
            NewRunId = replay.Result?.RunId,
            NewGoal = replay.Result?.Reachability.ToString() ?? "Not attempted",
            OriginalReceiptUnchanged = unchanged,
            Report = Path.Combine(folder, "index.md")
        }, TraversalDemo.JsonOptions));
        return ready ? 0 : 3;
    }
}
