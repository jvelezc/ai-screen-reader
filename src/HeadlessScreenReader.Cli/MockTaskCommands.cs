using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli;

internal static class MockTaskCommands
{
    internal static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (args.Length == 3 && args[0] == "--record-mock-tasks")
            {
                var source = MockSiteTasks.Fixtures.Select(MockSiteTasks.Describe).SingleOrDefault(t => t.Id == args[1]);
                if (source is null) { await output.WriteLineAsync("MOCK_TASK_SCENARIO_REQUIRED"); return 2; }
                var result = await MockTaskRecordings.RecordAsync(args[2], source.Fixture, stop.Token).ConfigureAwait(false);
                await output.WriteLineAsync(JsonSerializer.Serialize(result, TraversalDemo.JsonOptions));
                return result.Observation == "IncompleteOrMixedResults" ? 3 : 0;
            }
            if (args.Length == 3 && args[0] == "--verify-mock-tasks")
            {
                var result = await MockTaskRecordings.VerifyAsync(args[1], args[2]).ConfigureAwait(false);
                await output.WriteLineAsync(JsonSerializer.Serialize(result, TraversalDemo.JsonOptions)); return 0;
            }
            await output.WriteLineAsync("MOCK_TASK_ARGUMENTS_INVALID"); return 2;
        }
        catch (OperationCanceledException) { await output.WriteLineAsync("MOCK_TASK_COLLECTION_CANCELLED"); return 130; }
        catch (Exception e) when (e is IOException or JsonException or BrowserOperationException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        { await output.WriteLineAsync("MOCK_TASK_PACKAGE_UNCONFIRMED"); return 3; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
