using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class WorkerEvidenceTests
{
    internal static WorkerEvidenceArchive Receipt(FixtureJob job) => new(job, Guid.NewGuid(),
        new(new(job.OrganizationId, job.JobId, new('a', 64)), new('b', 64), 2, 800, true, true));

    [Fact]
    [Trait("Category", "Browser")]
    public async Task ActualChromiumProducerArchiveRetainsExactObservationAndAttemptAcrossReconnect()
    {
        var root = Path.Combine(Path.GetTempPath(), "hsr-producer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var job = FixtureWorkerTests.Job(); var worker = Guid.NewGuid();
            var observation = await FixtureWorker.ObserveAsync(job);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(observation, ObservationStore.Json);
            var archive = new TraversalArchive(new FileTraversalObjectStore(root));
            var producer = await new WorkerEvidenceFinalizer(archive).FinalizeAsync(job, worker, bytes, TraversalArchive.Hash(bytes), default);
            Assert.Equal(job, producer.Job); Assert.Equal(worker, producer.WorkerId);
            Assert.Equal("QueueWorkerObservation", producer.Origin);
            Assert.Equal(job.JobId, producer.Receipt.Identity.ArchiveId);
            Assert.Equal(2, producer.Receipt.Files); Assert.True(producer.Receipt.IndexReadbackVerified);
            var reconnected = new TraversalArchive(new FileTraversalObjectStore(root));
            Assert.True((await reconnected.VerifyAsync(producer.Receipt.Identity)).IntegrityMatches);
            using var restored = new MemoryStream(); long? offset = 0;
            do
            {
                var page = await reconnected.ReadFileAsync(producer.Receipt.Identity, "observation.json", offset.Value);
                await restored.WriteAsync(page.Data); offset = page.NextOffset;
            } while (offset is not null);
            Assert.Equal(bytes, restored.ToArray());
            var manifest = await reconnected.ReadFileAsync(producer.Receipt.Identity, TraversalArchive.SourceManifest, 0);
            using var json = JsonDocument.Parse(manifest.Data); var source = json.RootElement;
            Assert.Equal(worker, source.GetProperty("WorkerId").GetGuid());
            Assert.Equal(job, source.GetProperty("Job").Deserialize<FixtureJob>());
            Assert.Equal(TraversalArchive.Hash(bytes), source.GetProperty("ObservationSha256").GetString());
            Assert.Equal("NotCollected", source.GetProperty("Recording").GetString());
            Assert.Equal(0, source.GetProperty("DispatchedInputs").GetInt32()); Assert.False(source.GetProperty("GoalVerified").GetBoolean());
            Assert.True(observation.Shutdown.ProcessExitConfirmed); Assert.True(observation.Shutdown.CollectorStopAcknowledged);
            Assert.DoesNotContain("lease_token", source.GetRawText()); Assert.DoesNotContain("Token", source.GetRawText());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("observation")]
    [InlineData("digest")]
    [InlineData("worker")]
    public async Task UnboundProducerInputsCannotPublishAnyArchive(string failure)
    {
        var job = FixtureWorkerTests.Job(); var worker = Guid.NewGuid();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(failure == "observation" ? FixtureWorkerTests.Job() : job), ObservationStore.Json);
        var objects = new TraversalArchiveTests.Objects();
        await Assert.ThrowsAsync<BrowserOperationException>(() => new WorkerEvidenceFinalizer(new(objects)).FinalizeAsync(job,
            failure == "worker" ? Guid.Empty : worker, bytes, failure == "digest" ? new('f', 64) : TraversalArchive.Hash(bytes), default));
        Assert.Empty(objects.Values); Assert.Empty(objects.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptOriginalReadbackCannotPublishProducerIndex(bool corrupt)
    {
        var job = FixtureWorkerTests.Job(); var bytes = JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(job), ObservationStore.Json);
        var objects = new TraversalArchiveTests.Objects();
        objects.ReadOverride = (identity, part, _) => Task.FromResult(objects.Values.TryGetValue(TraversalArchive.ObjectKey(identity, part), out var value)
            ? corrupt ? new byte[] { 9, 8 } : null : null);
        await Assert.ThrowsAsync<TraversalArchiveException>(() => new WorkerEvidenceFinalizer(new(objects))
            .FinalizeAsync(job, Guid.NewGuid(), bytes, TraversalArchive.Hash(bytes), default));
        Assert.DoesNotContain(objects.Writes, path => path.EndsWith("/index.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProducerIndexReadbackPrecedesCompletionAndRejectedFenceLeavesOnlyOrphanBytes(bool accepted)
    {
        var root = Path.Combine(Path.GetTempPath(), "hsr-producer-coordinator-" + Guid.NewGuid().ToString("N"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(lease.Job), ObservationStore.Json);
        var objects = new TraversalArchiveTests.Objects(); var leases = new Leases(lease, accepted);
        objects.BeforeWrite = async (_, part, _, token) =>
        {
            if (part is not null) return;
            var local = await File.ReadAllBytesAsync(Path.Combine(root, lease.Job.Reference.Replace('/', Path.DirectorySeparatorChar)), token);
            Assert.Equal(bytes, local); entered.SetResult(); await release.Task;
        };
        try
        {
            var task = new ObservationCoordinator(leases, new Executor(bytes), new(root, producer: new(new(objects))))
                .RunOnceAsync(lease.WorkerId, default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(leases.Completed); Assert.False(task.IsCompleted);
            release.SetResult(); var result = await task;
            Assert.Equal(accepted ? "Observed" : "Unconfirmed", result.State);
            Assert.Equal(accepted ? null : "WORKER_COMPLETION_UNCONFIRMED", result.ErrorCode);
            Assert.NotNull(leases.Completed?.WorkerArchive); leases.Completed!.WorkerArchive!.Validate(lease.Job, lease.WorkerId);
            Assert.False(result.OffHostReadbackVerified); Assert.Equal(0, leases.Blocks);
            Assert.True((await new TraversalArchive(objects).VerifyAsync(leases.Completed.WorkerArchive.Receipt.Identity)).IntegrityMatches);
            Assert.DoesNotContain(lease.Token.ToString(), JsonSerializer.Serialize(leases.Completed));
        }
        finally { release.TrySetResult(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancelledProducerReadbackJoinsActualStorageBeforeBlockingWithoutCompletion()
    {
        var root = Path.Combine(Path.GetTempPath(), "hsr-producer-stop-" + Guid.NewGuid().ToString("N"));
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(lease.Job), ObservationStore.Json);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var objects = new TraversalArchiveTests.Objects(); var leases = new Leases(lease, true); var settled = false;
        objects.ReadOverride = async (identity, part, _) =>
        {
            var value = objects.Values.GetValueOrDefault(TraversalArchive.ObjectKey(identity, part));
            if (part is null && value is not null) { entered.SetResult(); await release.Task; settled = true; }
            return value?.ToArray();
        };
        using var stop = new CancellationTokenSource();
        try
        {
            var task = new ObservationCoordinator(leases, new Executor(bytes), new(root, producer: new(new(objects))))
                .RunOnceAsync(lease.WorkerId, stop.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
            Assert.False(task.IsCompleted); Assert.Null(leases.Completed); Assert.Equal(0, leases.Blocks);
            release.SetResult(); var result = await task;
            Assert.True(settled); Assert.Equal("Blocked", result.State); Assert.Equal("WORKER_STOP_REQUESTED", result.ErrorCode);
            Assert.Null(leases.Completed); Assert.Equal(1, leases.Blocks);
        }
        finally { release.TrySetResult(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("scan")]
    [InlineData("generation")]
    [InlineData("worker")]
    [InlineData("origin")]
    [InlineData("index")]
    [InlineData("readback")]
    [InlineData("files")]
    [InlineData("length")]
    public void ProducerReceiptCannotChangeAttemptOrUpgradeIncompleteReadback(string failure)
    {
        var job = FixtureWorkerTests.Job(); var receipt = Receipt(job); var worker = receipt.WorkerId;
        receipt = failure switch
        {
            "tenant" => receipt with { Job = job with { OrganizationId = Guid.NewGuid() } },
            "scan" => receipt with { Job = job with { ScanId = Guid.NewGuid() } },
            "generation" => receipt with { Job = job with { Generation = 2 } },
            "worker" => receipt with { WorkerId = Guid.NewGuid() },
            "origin" => receipt with { Origin = "OperatorImport" },
            "index" => receipt with { Receipt = receipt.Receipt with { Identity = receipt.Receipt.Identity with { IndexSha256 = new('A', 64) } } },
            "readback" => receipt with { Receipt = receipt.Receipt with { IndexReadbackVerified = false } },
            "files" => receipt with { Receipt = receipt.Receipt with { Files = 3 } },
            _ => receipt with { Receipt = receipt.Receipt with { Bytes = FixtureObservation.MaximumBytes + 4097 } }
        };
        Assert.Equal("WORKER_EVIDENCE_PROVENANCE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => receipt.Validate(job, worker)).Code);
    }

    [Fact]
    public async Task KnownProducerReceiptDoesNotClaimCurrentOriginalAvailability()
    {
        var project = new PortalProjectStub(); var job = new FixtureJob(project.Job.OrganizationId, project.Job.ScanId, project.Job.JobId, 1);
        var producer = Receipt(job); project.Record = project.Record with { State = "observed" };
        project.Job = project.Job with { State = "completed", Generation = 1, WorkerArchive = producer };
        var reader = new PortalObservationStub();
        var response = await new PortalScanService(project, reader).ReportAsync(new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer), job.OrganizationId, job.ScanId, default);
        Assert.Equal("Unavailable", response.EvidenceState); Assert.Null(response.Observation);
        Assert.Equal("CONTROL_EVIDENCE_UNAVAILABLE", response.ErrorCode); Assert.Equal(producer, response.WorkerArchive);
    }

    private sealed class Executor(byte[] bytes) : IFixtureExecutor
    { public Task<byte[]> ExecuteAsync(FixtureJob job, CancellationToken token) => Task.FromResult(bytes); }
    private sealed class Leases(WorkerLease lease, bool accepted) : IWorkerLeases
    {
        public StoredObservation? Completed { get; private set; }
        public int Blocks { get; private set; }
        public Task<WorkerClaim> ClaimAsync(Guid worker, CancellationToken token) => Task.FromResult(new WorkerClaim("Claimed", lease));
        public Task<bool> RenewAsync(WorkerLease actual, CancellationToken token) => Task.FromResult(true);
        public Task<bool> CompleteAsync(WorkerLease actual, StoredObservation stored, CancellationToken token)
        { Completed = stored; return Task.FromResult(accepted); }
        public Task<bool> BlockAsync(WorkerLease actual, string code, CancellationToken token) { Blocks++; return Task.FromResult(true); }
    }
}
