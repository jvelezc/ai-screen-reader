using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli;

internal static class SyntheticSearchCommands
{
    internal static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (args.Length == 3 && args[0] == "--record-search-tasks")
            {
                var source = SyntheticSearchSites.Fixtures.Select(SyntheticSearchSites.Describe).SingleOrDefault(s => s.Id == args[1]);
                if (source is null) { await output.WriteLineAsync("SEARCH_SCENARIO_REQUIRED"); return 2; }
                var result = await SyntheticSearchRecordings.RecordAsync(args[2], source.Fixture, stop.Token).ConfigureAwait(false);
                await output.WriteLineAsync(JsonSerializer.Serialize(result, TraversalDemo.JsonOptions));
                return result.Observation == SyntheticSearchAssessment.Incomplete ? 3 : 0;
            }
            if (args.Length == 3 && args[0] == "--verify-search-tasks")
            {
                var result = await SyntheticSearchVerification.VerifyAsync(args[1], args[2]).ConfigureAwait(false);
                await output.WriteLineAsync(JsonSerializer.Serialize(result, TraversalDemo.JsonOptions)); return 0;
            }
            await output.WriteLineAsync("SEARCH_ARGUMENTS_INVALID"); return 2;
        }
        catch (OperationCanceledException) { await output.WriteLineAsync("SEARCH_COLLECTION_CANCELLED"); return 130; }
        catch (Exception e) when (e is IOException or JsonException or BrowserOperationException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        { await output.WriteLineAsync("SEARCH_PACKAGE_UNCONFIRMED"); return 3; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
