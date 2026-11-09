using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

internal static class PublicTaskProof
{
    internal static async Task<int> RunAsync(string runtime, string root, TextWriter output)
    {
        var image = Environment.GetEnvironmentVariable("HSR_WORKER_IMAGE") ?? throw new InvalidOperationException();
        var folder = Path.Combine(Path.GetFullPath(root), "artifacts", "public-tasks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var executor = new PublicTaskExecutor(folder, Path.Combine(root, "scripts"), runtime, image);
        var receipts = new List<object>(); var ready = true;
        foreach (var scenario in new[] { "working", "keyboard-blocked", "pointer-unconfirmed", "alternative-content" })
        {
            var job = new PublicTaskJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                PublicReaderSource.FromUrl("https://screenreader.privensic.com/ci/tasks/v1/" + scenario), new string('a', 64));
            var phases = new List<string>();
            var native = await executor.ExecuteAsync(job, Guid.NewGuid(), (phase, _) => { phases.Add(phase); return Task.FromResult(true); }, CancellationToken.None).ConfigureAwait(false);
            var report = native.Bundle.Report;
            var assessment = PublicTaskAssessment.Evaluate(report, true, native.ResourcesAbsent);
            var expected = scenario switch { "working" => "NoBlockingBarrierInTestedTask", "keyboard-blocked" => "WebsiteEvidenceReady", _ => "InsufficientEvidence" };
            var passed = assessment.Outcome == expected && report.Attempts.Length == 3 && report.Attempts.All(a => a.DecodedVideoFrames > 0)
                && phases.Contains("KeyboardAttempt1") && phases.Contains("KeyboardAttempt2") && phases.Contains("PointerComparison");
            foreach (var file in native.Bundle.Files) await File.WriteAllBytesAsync(Path.Combine(folder, scenario + "-" + file.Key), file.Value).ConfigureAwait(false);
            receipts.Add(new
            {
                Scenario = scenario,
                Passed = passed,
                assessment.Outcome,
                native.ResourcesAbsent,
                Attempts = report.Attempts.Select(a => new { a.Method, a.Outcome, Events = a.Events.Length, a.DecodedVideoFrames, a.ErrorCode })
            });
            ready &= passed;
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            Schema = "hsr-public-task-native-proof-v1",
            Ready = ready,
            Folder = folder,
            Cases = receipts,
            DatabaseAccessed = false,
            TenantAcceptance = false,
            ActualAssistiveTechnology = false
        })).ConfigureAwait(false);
        return ready ? 0 : 3;
    }
}
