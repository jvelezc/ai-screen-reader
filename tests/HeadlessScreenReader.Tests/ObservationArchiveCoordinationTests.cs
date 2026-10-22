using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ObservationArchiveCoordinationTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Archive(Func<FixtureJob, byte[], string, CancellationToken, Task> finalize) : IObservationArchive
    {
        public Task FinalizeAsync(FixtureJob job, byte[] bytes, string digest, CancellationToken token) => finalize(job, bytes, digest, token);
        public Task<byte[]?> ReadAsync(FixtureJob job, string digest, CancellationToken token) => throw new InvalidOperationException();
    }
    private sealed class Executor(byte[] bytes) : IFixtureExecutor
    {
        public Task<byte[]> ExecuteAsync(FixtureJob job, CancellationToken token) => Task.FromResult(bytes);
    }
    private sealed class Leases(WorkerLease lease) : IWorkerLeases
    {
        public int Completes { get; private set; }
        public List<string> Blocks { get; } = [];
        public Func<StoredObservation, Task<bool>> Complete { get; init; } = _ => Task.FromResult(true);
        public Task<WorkerClaim> ClaimAsync(Guid worker, CancellationToken token) => Task.FromResult(new WorkerClaim("Claimed", lease));
        public Task<bool> RenewAsync(WorkerLease actual, CancellationToken token) { Assert.Same(lease, actual); return Task.FromResult(true); }
        public Task<bool> CompleteAsync(WorkerLease actual, StoredObservation stored, CancellationToken token)
        { Assert.Same(lease, actual); Completes++; return Complete(stored); }
        public Task<bool> BlockAsync(WorkerLease actual, string code, CancellationToken token)
        { Assert.Same(lease, actual); Blocks.Add(code); return Task.FromResult(true); }
    }
    private sealed class Evidence : IDisposable
    {
        private readonly string _parent = Path.GetFullPath(Path.GetTempPath());
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-archive-" + Guid.NewGuid().ToString("N"));
        public WorkerLease Lease { get; } = new(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid());
        public byte[] Bytes => JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(Lease.Job), ObservationStore.Json);
        public string FilePath => Path.Combine(Root, Lease.Job.Reference.Replace('/', Path.DirectorySeparatorChar));
        public void Dispose()
        {
            Assert.StartsWith(_parent, Path.GetFullPath(Root));
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    [Fact]
    public async Task RemoteReadbackMustSettleAfterLocalFlushAndBeforeFencedCompletion()
    {
        using var evidence = new Evidence(); var entered = Signal(); var release = Signal();
        var leases = new Leases(evidence.Lease)
        { Complete = stored => { Assert.True(stored.OffHostReadbackVerified); Assert.Equal(evidence.Lease.Job.Reference, stored.Reference); return Task.FromResult(true); } };
        var archive = new Archive(async (job, bytes, digest, _) =>
        {
            Assert.Equal(evidence.Lease.Job, job); Assert.Equal(evidence.Bytes, bytes);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(evidence.FilePath));
            Assert.Equal(64, digest.Length); entered.SetResult(); await release.Task;
        });
        var run = new ObservationCoordinator(leases, new Executor(evidence.Bytes), new(evidence.Root, archive))
            .RunOnceAsync(evidence.Lease.WorkerId, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(run.IsCompleted); Assert.Equal(0, leases.Completes); Assert.Empty(leases.Blocks);
        release.SetResult(); var result = await run;
        Assert.Equal("Observed", result.State); Assert.True(result.OffHostReadbackVerified);
        Assert.Equal(1, leases.Completes); Assert.Empty(leases.Blocks);
    }

    [Theory]
    [InlineData(ObservationArchiveFailure.Unavailable, "EVIDENCE_S3_UNAVAILABLE")]
    [InlineData(ObservationArchiveFailure.Invalid, "EVIDENCE_S3_CONTENT_INVALID")]
    [InlineData(ObservationArchiveFailure.WriteUnconfirmed, "EVIDENCE_S3_WRITE_UNCONFIRMED")]
    public async Task RemoteFailureRetainsLocalBytesAndBlocksWithoutInventingCompletion(ObservationArchiveFailure failure, string code)
    {
        using var evidence = new Evidence(); var leases = new Leases(evidence.Lease);
        var archive = new Archive((_, _, _, _) => Task.FromException(new ObservationArchiveException(failure)));
        var result = await new ObservationCoordinator(leases, new Executor(evidence.Bytes), new(evidence.Root, archive))
            .RunOnceAsync(evidence.Lease.WorkerId, CancellationToken.None);
        Assert.Equal("Blocked", result.State); Assert.Equal(code, result.EvidenceErrorCode);
        Assert.Equal("FIXTURE_OBSERVATION_UNCONFIRMED", result.ErrorCode);
        Assert.Equal(0, leases.Completes); Assert.Equal(new[] { "FIXTURE_OBSERVATION_UNCONFIRMED" }, leases.Blocks);
        Assert.Null(result.OffHostReadbackVerified); Assert.Null(result.Reference);
        Assert.Equal(evidence.Bytes, await File.ReadAllBytesAsync(evidence.FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedOffHostBytesDoNotImplyDatabaseCompletion(bool databaseThrows)
    {
        using var evidence = new Evidence(); var leases = new Leases(evidence.Lease)
        { Complete = _ => databaseThrows ? Task.FromException<bool>(new IOException("private-database-canary")) : Task.FromResult(false) };
        var archive = new Archive((_, _, _, _) => Task.CompletedTask);
        var result = await new ObservationCoordinator(leases, new Executor(evidence.Bytes), new(evidence.Root, archive))
            .RunOnceAsync(evidence.Lease.WorkerId, CancellationToken.None);
        Assert.Equal("Unconfirmed", result.State); Assert.Equal("WORKER_COMPLETION_UNCONFIRMED", result.ErrorCode);
        Assert.True(result.OffHostReadbackVerified); Assert.Equal(1, leases.Completes); Assert.Empty(leases.Blocks);
        Assert.Equal(evidence.Lease.Job.Reference, result.Reference);
        Assert.DoesNotContain("private-database-canary", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task StopDuringFinalizationJoinsActualCallbackBeforeBlockingTheJob()
    {
        using var evidence = new Evidence(); using var stop = new CancellationTokenSource();
        var entered = Signal(); var release = Signal(); var leases = new Leases(evidence.Lease);
        var archive = new Archive(async (_, _, _, token) => { entered.SetResult(); await release.Task; token.ThrowIfCancellationRequested(); });
        var run = new ObservationCoordinator(leases, new Executor(evidence.Bytes), new(evidence.Root, archive))
            .RunOnceAsync(evidence.Lease.WorkerId, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        Assert.False(run.IsCompleted); Assert.Equal(0, leases.Completes); Assert.Empty(leases.Blocks);
        release.SetResult(); var result = await run;
        Assert.Equal("Blocked", result.State); Assert.Equal("WORKER_STOP_REQUESTED", result.ErrorCode);
        Assert.Equal(new[] { "WORKER_STOP_REQUESTED" }, leases.Blocks); Assert.Equal(0, leases.Completes);
        Assert.Null(result.OffHostReadbackVerified);
    }

    [Fact]
    public async Task LocalOnlyPersistenceDoesNotClaimOffHostVerification()
    {
        using var evidence = new Evidence(); var leases = new Leases(evidence.Lease);
        var result = await new ObservationCoordinator(leases, new Executor(evidence.Bytes), new(evidence.Root))
            .RunOnceAsync(evidence.Lease.WorkerId, CancellationToken.None);
        Assert.Equal("Observed", result.State); Assert.False(result.OffHostReadbackVerified);
        Assert.Equal(evidence.Bytes, await File.ReadAllBytesAsync(evidence.FilePath)); Assert.Equal(1, leases.Completes);
    }
}
