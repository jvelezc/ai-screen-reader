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
        var live = new PublicTaskLiveFrameStore(folder);
        var receipts = new List<object>(); var ready = true;
        foreach (var scenario in new[] { "working", "keyboard-blocked", "pointer-unconfirmed", "alternative-content" })
        {
            var job = new PublicTaskJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                PublicReaderSource.FromUrl("https://screenreader.privensic.com/ci/tasks/v1/" + scenario), new string('a', 64));
            var phases = new List<string>();
            var execution = executor.ExecuteAsync(job, Guid.NewGuid(), (phase, _) => { phases.Add(phase); return Task.FromResult(true); }, CancellationToken.None);
            PublicTaskLiveFrame? observed = null;
            while (!execution.IsCompleted && observed is null)
            {
                for (var attempt = 1; attempt <= 3 && observed is null; attempt++)
                    observed = await live.ReadAsync(job, attempt, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false);
                if (observed is null) await Task.WhenAny(execution, Task.Delay(30)).ConfigureAwait(false);
            }
            var native = await execution.ConfigureAwait(false);
            var liveSameExecution = observed?.ExecutionId == native.Start.ExecutionId;
            var liveCleared = observed is not null && await live.ReadAsync(job, observed.Attempt, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false) is null;
            var report = native.Bundle.Report;
            var assessment = PublicTaskAssessment.Evaluate(report, true, native.ResourcesAbsent);
            var expected = scenario switch { "working" => "NoBlockingBarrierInTestedTask", "keyboard-blocked" => "WebsiteEvidenceReady", _ => "InsufficientEvidence" };
            var passed = assessment.Outcome == expected && report.AssessmentPolicy == PublicTaskReport.TransportAttributionPolicy
                && report.ReaderProfile == HeadlessScreenReader.ScreenReader.ScreenReaderSession.Profile
                && report.Attempts.All(a => HasReaderEvidence(a, scenario == "pointer-unconfirmed"))
                && report.NetworkPolicy == PublicTaskNetworkTrace.PublicDependencyPolicy
                && report.Egress is { Requests: > 0 } && report.EgressIsolationProbeThroughSequence is > 0
                && report.Attempts.Length == 3 && report.Attempts.All(a => a.DecodedVideoFrames > 0 && a.Network is { Requests: > 0 })
                && phases.Contains("KeyboardAttempt1") && phases.Contains("KeyboardAttempt2") && phases.Contains("PointerComparison")
                && liveSameExecution && liveCleared;
            foreach (var file in native.Bundle.Files) await File.WriteAllBytesAsync(Path.Combine(folder, scenario + "-" + file.Key), file.Value).ConfigureAwait(false);
            receipts.Add(new
            {
                Scenario = scenario,
                Passed = passed,
                assessment.Outcome,
                report.AssessmentPolicy,
                report.ReaderProfile,
                LiveSameExecution = liveSameExecution,
                LiveFrameClearedAfterCompletion = liveCleared,
                native.ResourcesAbsent,
                Attempts = report.Attempts.Select(a => new
                {
                    a.Method,
                    a.Outcome,
                    Events = a.Events.Length,
                    a.DecodedVideoFrames,
                    a.ErrorCode,
                    ReaderRecords = a.Events.Count(e => e.Reader is not null),
                    UnconfirmedPointerInputsWithoutReader = a.Events.Count(e => e.Reader is null && e.Action == "Pointer" && e.InputAttempted && !e.InputCompleted),
                    ReaderCommands = a.Events.Select(e => e.Reader?.Command).ToArray(),
                    NetworkRequests = a.Network!.Requests,
                    NetworkEvents = a.Network.Events.Length,
                    a.Network.DroppedEvents
                }),
                Diagnostics = PublicTaskIssueTable.From(report)
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

    internal static bool HasReaderEvidence(PublicTaskAttempt attempt, bool permitUnconfirmedPointer) =>
        attempt.InitialReader is { Command: "ReadCurrent", Outcome: "Observed" }
        && attempt.Events.All(e => e.Reader is not null || permitUnconfirmedPointer
            && attempt.Method == "Pointer" && attempt.Outcome == "Unconfirmed" && attempt.ErrorCode == "PUBLIC_TASK_ATTEMPT_UNCONFIRMED"
            && e.Action == "Pointer" && e.InputAttempted && !e.InputCompleted);
}
