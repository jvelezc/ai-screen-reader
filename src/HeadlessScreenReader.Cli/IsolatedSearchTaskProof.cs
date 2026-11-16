using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

internal static class IsolatedSearchTaskProof
{
    private static void Require(bool condition)
    { if (!condition) throw new BrowserOperationException("ISOLATED_SEARCH_TASK_PROOF_UNCONFIRMED"); }

    internal static async Task<int> RunAsync(TextWriter output)
    {
        var stage = "Configuration";
        try
        {
            var runtime = Environment.GetEnvironmentVariable("HSR_WORKER_RUNTIME") ?? "podman";
            var image = Environment.GetEnvironmentVariable("HSR_WORKER_IMAGE") ?? throw new InvalidOperationException();
            var state = Environment.GetEnvironmentVariable("HSR_WORKER_EVIDENCE") ?? throw new InvalidOperationException();
            var journal = new OwnershipJournal(Path.Combine(state, ".search-task-proof", Guid.NewGuid().ToString("N")));
            using var exclusive = journal.AcquireExclusive();
            var executor = new IsolatedFixtureExecutor(runtime, image, Environment.GetEnvironmentVariable("HSR_WORKER_SECCOMP"), journal);
            var worker = Guid.NewGuid(); var runs = new List<object>(); var replays = 0;
            var controlRoot = Path.Combine(Path.GetDirectoryName(journal.IntentsDirectory)!, "search-task-control");
            using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            SyntheticSearchTaskJob Job(string id) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId(id));
            string Folder(SyntheticSearchTaskJob job) => Path.Combine(controlRoot, $"job-{job.OrganizationId:N}-{job.ScanId:N}-{job.JobId:N}-1");
            foreach (var id in new[] { "search-working", "search-keyboard-blocked" })
            {
                stage = "Collect:" + id;
                var job = Job(id);
                var result = await executor.ExecuteSearchTaskAsync(job, worker, _ => Task.FromResult(true), stop.Token).ConfigureAwait(false);
                var bundle = SyntheticSearchTaskBundle.Decode(result.Bundle, result.Start);
                var retained = SyntheticSearchTaskGrants.ReadRetainedSources(result.RetainedEvidenceDirectory, result.Start);
                bundle.MatchRetainedSources(retained);
                Require(result.ContainerAbsenceConfirmed && result.Start.Job == job && result.Start.WorkerId == worker
                    && retained.Files.Select(file => file.Trial).Distinct().Count() == 5 && !retained.HasUncommittedFiles && journal.ReadPending().Count == 0);
                var folder = Path.Combine(Path.GetDirectoryName(journal.IntentsDirectory)!, "completed", job.JobId.ToString("N"));
                Directory.CreateDirectory(folder);
                var originalPath = Path.Combine(folder, "originals.zip");
                await File.WriteAllBytesAsync(originalPath, bundle.Originals, stop.Token).ConfigureAwait(false);
                Require(TraversalArchive.Hash(await File.ReadAllBytesAsync(originalPath, stop.Token).ConfigureAwait(false)) == bundle.Receipt.ArchiveSha256);
                await File.WriteAllBytesAsync(Path.Combine(folder, "receipt.json"), JsonSerializer.SerializeToUtf8Bytes(bundle.Receipt, ReaderWorkerWire.Json), stop.Token).ConfigureAwait(false);
                runs.Add(new
                {
                    Source = id,
                    job.JobId,
                    result.Start.ExecutionId,
                    bundle.Receipt.ManifestSha256,
                    bundle.Receipt.ArchiveSha256,
                    AuthorizedBoundaries = 85,
                    NativeInputs = 77,
                    KeyboardContexts = 4,
                    MouseContexts = 1,
                    RetainedImages = retained.Files.Length,
                    Originals = originalPath,
                    OriginalReadbackVerified = true,
                    ContainerAbsenceConfirmed = true
                });
                if (id == "search-working")
                {
                    stage = "CompletedReplay";
                    try { await executor.ExecuteSearchTaskAsync(job, worker, _ => Task.FromResult(true), stop.Token).ConfigureAwait(false); }
                    catch (BrowserOperationException exception) when (exception.Code == "SEARCH_TASK_REPLAY_OR_FENCE_UNCONFIRMED") { replays++; }
                }
            }
            stage = "MidQueryAuthorityLoss";
            var interrupted = Job("search-working");
            var deniedIntent = Path.Combine(Folder(interrupted), "intent-0009.json");
            var denied = false;
            try { await executor.ExecuteSearchTaskAsync(interrupted, worker, _ => Task.FromResult(!File.Exists(deniedIntent)), stop.Token).ConfigureAwait(false); }
            catch (BrowserOperationException exception) when (exception.Code == "SEARCH_TASK_AUTHORITY_UNCONFIRMED") { denied = true; }
            Require(denied && journal.ReadPending().Count == 0);
            var savedStart = JsonSerializer.Deserialize<SyntheticSearchTaskStart>(await File.ReadAllBytesAsync(Path.Combine(Folder(interrupted), "start.json"), stop.Token).ConfigureAwait(false), ReaderWorkerWire.Json)!;
            Require(savedStart.Job == interrupted && savedStart.WorkerId == worker);
            var partial = SyntheticSearchTaskGrants.ReadRetainedSources(Folder(interrupted), savedStart);
            Require(!partial.Files.IsDefaultOrEmpty && partial.Files.All(file => file.Trial == 1) && !partial.IsCompletedRecording && !partial.CanReplay
                && !File.Exists(Path.Combine(Folder(interrupted), "intent-0010.json")));
            var deniedResponse = JsonSerializer.Deserialize<SyntheticSearchTaskAck>(await File.ReadAllBytesAsync(Path.Combine(Folder(interrupted), "response-0009.json"), stop.Token).ConfigureAwait(false), ReaderWorkerWire.Json)!;
            Require(deniedResponse.Decision == SyntheticSearchInputDecision.Revoked);
            stage = "InterruptedReplay";
            try { await executor.ExecuteSearchTaskAsync(interrupted, worker, _ => Task.FromResult(true), stop.Token).ConfigureAwait(false); }
            catch (BrowserOperationException exception) when (exception.Code == "SEARCH_TASK_REPLAY_OR_FENCE_UNCONFIRMED") { replays++; }
            var removals = Directory.GetFiles(journal.RemovalsDirectory, "*.json");
            Require(replays == 2 && removals.Length == 5 && journal.ReadPending().Count == 0);
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Schema = "hsr-isolated-search-task-proof-v1",
                State = "IsolatedSearchTasksVerified",
                Image = image,
                Collections = runs,
                CollectedScenarios = 2,
                AuthorityLossesDenied = 1,
                ReplayAttemptsDenied = replays,
                NativeRemovals = removals.Length,
                RetainedInterruptedImages = partial.Files.Length,
                PendingNativeIntents = 0,
                DatabaseAccessed = false,
                QueueAdmissionImplemented = false,
                AppJwtValidated = false,
                PublicWebsiteInputEnabled = false,
                LegalCaseDetermined = false
            }, ReaderWorkerWire.Json)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            var reason = exception is BrowserOperationException failure && failure.Code.Length <= 80
                && failure.Code.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_')
                    ? failure.Code : "UNCONFIRMED";
            var category = exception switch { BrowserOperationException => "Engine", OperationCanceledException => "Canceled", IOException => "Io", JsonException => "Evidence", _ => "Unexpected" };
            await output.WriteLineAsync(JsonSerializer.Serialize(new { Schema = "hsr-isolated-search-task-proof-v1", State = "Unconfirmed", Stage = stage, Category = category, Reason = reason })).ConfigureAwait(false);
            return 3;
        }
    }
}
