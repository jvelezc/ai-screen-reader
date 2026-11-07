using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>Real immutable-image acceptance, without queue admission, Supabase credentials or tenant/AT claims.</summary>
internal static class IsolatedMockTaskProof
{
    private static void Require(bool condition)
    {
        if (!condition) throw new BrowserOperationException("ISOLATED_MOCK_TASK_PROOF_UNCONFIRMED");
    }
    internal static async Task<int> RunAsync(TextWriter output)
    {
        try
        {
            var runtime = Environment.GetEnvironmentVariable("HSR_WORKER_RUNTIME") ?? "podman";
            var image = Environment.GetEnvironmentVariable("HSR_WORKER_IMAGE") ?? throw new InvalidOperationException();
            var state = Environment.GetEnvironmentVariable("HSR_WORKER_EVIDENCE") ?? throw new InvalidOperationException();
            var journal = new OwnershipJournal(Path.Combine(state, ".mock-task-proof", Guid.NewGuid().ToString("N")));
            using var exclusive = journal.AcquireExclusive();
            var executor = new IsolatedFixtureExecutor(runtime, image, Environment.GetEnvironmentVariable("HSR_WORKER_SECCOMP"), journal);
            var worker = Guid.NewGuid(); var runs = new List<object>(); var replayDenied = 0; var authorityDenied = 0;
            MockTaskJob Job(string source) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, MockTaskJob.CurrentProfile, MockTaskSource.FromId(source));
            using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            foreach (var source in new[] { "checkout-working", "checkout-keyboard-blocked", "control-named", "control-unnamed" })
            {
                var job = Job(source); var checks = 0;
                var result = await executor.ExecuteMockTaskAsync(job, worker, _ => { checks++; return Task.FromResult(true); }, stop.Token).ConfigureAwait(false);
                var bundle = MockTaskCollectionBundle.Decode(result.Bundle, result.Start);
                Require(result.Start.Job == job && result.Start.WorkerId == worker
                    && result.ContainerAbsenceConfirmed && checks == 30 && journal.ReadPending().Count == 0);
                var originalsFolder = Path.Combine(Path.GetDirectoryName(journal.IntentsDirectory)!, "completed", job.JobId.ToString("N"));
                Directory.CreateDirectory(originalsFolder);
                var originalsFile = Path.Combine(originalsFolder, "originals.zip");
                await using (var saved = new FileStream(originalsFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await saved.WriteAsync(bundle.Originals, stop.Token).ConfigureAwait(false);
                Require(TraversalArchive.Hash(await File.ReadAllBytesAsync(originalsFile, stop.Token).ConfigureAwait(false)) == bundle.Receipt.ArchiveSha256);
                await File.WriteAllBytesAsync(Path.Combine(originalsFolder, "receipt.json"),
                    JsonSerializer.SerializeToUtf8Bytes(bundle.Receipt, ReaderWorkerWire.Json), stop.Token).ConfigureAwait(false);
                runs.Add(new
                {
                    Source = source,
                    JobId = job.JobId,
                    ManifestSha256 = bundle.Receipt.ManifestSha256,
                    ArchiveSha256 = bundle.Receipt.ArchiveSha256,
                    AuthorizedBoundaries = 13,
                    AuthorityChecks = checks,
                    OriginalBytes = bundle.Originals.Length,
                    OriginalArchive = originalsFile,
                    OriginalReadbackVerified = true,
                    ContainerAbsenceConfirmed = true
                });
                if (source == "checkout-working")
                {
                    try { await executor.ExecuteMockTaskAsync(job, worker, _ => Task.FromResult(true), stop.Token).ConfigureAwait(false); }
                    catch (BrowserOperationException e) when (e.Code == "MOCK_TASK_REPLAY_OR_FENCE_UNCONFIRMED") { replayDenied++; }
                }
            }
            foreach (var reject in new[] { 5, 25 })
            {
                // Callback5 is before first Tab;25 is before Pointer. Existing input intent remains explicit.
                var job = Job("checkout-working"); var checks = 0;
                try { await executor.ExecuteMockTaskAsync(job, worker, _ => Task.FromResult(++checks < reject), stop.Token).ConfigureAwait(false); }
                catch (BrowserOperationException e) when (e.Code == "MOCK_TASK_AUTHORITY_UNCONFIRMED") { authorityDenied++; }
                Require(checks == reject && journal.ReadPending().Count == 0);
                try { await executor.ExecuteMockTaskAsync(job, worker, _ => Task.FromResult(true), stop.Token).ConfigureAwait(false); }
                catch (BrowserOperationException e) when (e.Code == "MOCK_TASK_REPLAY_OR_FENCE_UNCONFIRMED") { replayDenied++; }
            }
            Require(replayDenied == 3 && authorityDenied == 2 && journal.ReadPending().Count == 0);
            var removals = Directory.GetFiles(journal.RemovalsDirectory, "*.json");
            Require(removals.Length == 9);
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Schema = "hsr-isolated-mock-task-proof-v1",
                State = "IsolatedMockTasksVerified",
                Image = image,
                Collections = runs,
                CollectedScenarios = 4,
                AuthorityLossesDenied = authorityDenied,
                ReplayAttemptsDenied = replayDenied,
                NativeRemovals = removals.Length,
                PendingNativeIntents = 0,
                DatabaseAccessed = false,
                QueueAdmissionImplemented = false,
                OriginalsRetainedAndReadbackVerified = true,
                AppJwtValidated = false,
                PublicWebsiteInputEnabled = false,
                LegalCaseDetermined = false
            })).ConfigureAwait(false);
            return 0;
        }
        catch (Exception) { await output.WriteLineAsync("ISOLATED_MOCK_TASK_PROOF_UNCONFIRMED").ConfigureAwait(false); return 3; }
    }
}
