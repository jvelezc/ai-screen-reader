using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ObservationCoordinatorTests
{
    private sealed class Leases(WorkerLease lease) : IWorkerLeases
    {
        public WorkerClaim Claim { get; set; } = new("Claimed", lease);
        public Func<Task<bool>> Renew { get; set; } = () => Task.FromResult(true);
        public Func<StoredObservation, bool> Complete { get; set; } = _ => true;
        public bool BlockThrows { get; set; }
        public int Completes { get; private set; }
        public List<string> Blocks { get; } = [];
        public Task<WorkerClaim> ClaimAsync(Guid worker, CancellationToken token) => Task.FromResult(Claim);
        public Task<bool> RenewAsync(WorkerLease current, CancellationToken token) { Assert.Same(lease, current); return Renew(); }
        public Task<bool> CompleteAsync(WorkerLease current, StoredObservation stored, CancellationToken token)
        { Assert.Same(lease, current); Completes++; return Task.FromResult(Complete(stored)); }
        public Task<bool> BlockAsync(WorkerLease current, string code, CancellationToken token)
        { Assert.Same(lease, current); Blocks.Add(code); return BlockThrows ? Task.FromException<bool>(new IOException("private-database-detail")) : Task.FromResult(true); }
    }
    private sealed class Executor(Func<FixtureJob, CancellationToken, Task<byte[]>> execute) : IFixtureExecutor
    {
        public int Calls { get; private set; }
        public Task<byte[]> ExecuteAsync(FixtureJob job, CancellationToken token) { Calls++; return execute(job, token); }
    }
    private static byte[] Bytes(FixtureJob job) => JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(job), ObservationStore.Json);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData("Empty")]
    [InlineData("Deferred")]
    [InlineData("Deduplicated")]
    [InlineData("Rejected")]
    [InlineData("Blocked")]
    public async Task NonclaimsDoNotLaunchBrowserOrInventCompletion(string status)
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid());
        var database = new Leases(lease) { Claim = new(status, null) };
        var executor = new Executor((_, _) => throw new InvalidOperationException());
        var result = await new ObservationCoordinator(database, executor, new("unused")).RunOnceAsync(lease.WorkerId, CancellationToken.None);
        Assert.Equal(status, result.State); Assert.Null(result.JobId); Assert.Equal(0, executor.Calls);
        Assert.Equal(0, database.Completes); Assert.Empty(database.Blocks);
    }
    [Fact]
    public async Task InterruptedKeyboardNonclaimRetainsJobAndReasonWithoutAllocatingInputOrLeakingCapability()
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 }, Guid.NewGuid(), Guid.NewGuid());
        var database = new Leases(lease) { Claim = new("Blocked", null, lease.Job.JobId, "WORKER_INPUT_OUTCOME_UNCONFIRMED") };
        var executor = new Executor((_, _) => throw new InvalidOperationException("No input may run"));
        var result = await new ObservationCoordinator(database, executor, new("unused")).RunOnceAsync(lease.WorkerId, default);
        Assert.Equal("Blocked", result.State); Assert.Equal(lease.Job.JobId, result.JobId);
        Assert.Equal("WORKER_INPUT_OUTCOME_UNCONFIRMED", result.ErrorCode); Assert.Null(result.Reference);
        Assert.Equal(0, executor.Calls); Assert.Equal(0, database.Completes); Assert.Empty(database.Blocks);
        Assert.DoesNotContain(lease.Token.ToString(), JsonSerializer.Serialize(result));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletionRequiresPreviouslyPersistedExactBytesAndRetainsRejectedReceipt(bool accepted)
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid());
        var root = Path.Combine(Path.GetTempPath(), "hsr-coordinator-" + Guid.NewGuid().ToString("N"));
        var bytes = Bytes(lease.Job); var database = new Leases(lease);
        database.Complete = stored =>
        {
            Assert.Equal(lease.Job.Reference, stored.Reference);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, stored.Reference)));
            return accepted;
        };
        try
        {
            var result = await new ObservationCoordinator(database, new Executor((_, _) => Task.FromResult(bytes)), new(root))
                .RunOnceAsync(lease.WorkerId, CancellationToken.None);
            Assert.Equal(accepted ? "Observed" : "Unconfirmed", result.State);
            Assert.Equal(accepted ? null : "WORKER_COMPLETION_UNCONFIRMED", result.ErrorCode);
            Assert.Equal(lease.Job.Reference, result.Reference); Assert.Equal(1, database.Completes); Assert.Empty(database.Blocks);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, result.Reference!)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidObservationBlocksSafelyButUnconfirmedCleanupRetainsOwnership(bool cleanupFailure)
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid()); var database = new Leases(lease);
        var executor = new Executor((_, _) => cleanupFailure
            ? Task.FromException<byte[]>(new BrowserOperationException("WORKER_CONTAINER_CLEANUP_UNCONFIRMED"))
            : Task.FromResult(Bytes(lease.Job with { Generation = 2 })));
        var result = await new ObservationCoordinator(database, executor, new("unused")).RunOnceAsync(lease.WorkerId, CancellationToken.None);
        Assert.Equal(cleanupFailure ? "Unconfirmed" : "Blocked", result.State); Assert.Equal(0, database.Completes);
        if (cleanupFailure) Assert.Empty(database.Blocks); else Assert.Equal(new[] { "FIXTURE_OBSERVATION_UNCONFIRMED" }, database.Blocks);
        Assert.Null(result.Reference);
    }
    [Fact]
    public async Task LostHeartbeatCancelsExecutionAndJoinsActualCleanupWithoutCompletionOrRelease()
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid()); var renews = 0;
        var database = new Leases(lease) { Renew = () => Task.FromResult(++renews == 1) };
        var canceled = Signal(); var cleanup = Signal();
        var executor = new Executor(async (_, token) =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { canceled.TrySetResult(); }
            await cleanup.Task; token.ThrowIfCancellationRequested(); return [];
        });
        var run = new ObservationCoordinator(database, executor, new("unused")).RunOnceAsync(lease.WorkerId, CancellationToken.None);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(run.IsCompleted); Assert.Equal(0, database.Completes); Assert.Empty(database.Blocks);
        cleanup.SetResult(); var result = await run;
        Assert.Equal("Unconfirmed", result.State); Assert.Equal("WORKER_LEASE_UNCONFIRMED", result.ErrorCode);
        Assert.Equal(0, database.Completes); Assert.Empty(database.Blocks);
    }
    [Fact]
    public async Task ActualRenewalMustSettleBeforeCompletionEvenAfterBrowserReturns()
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid()); var database = new Leases(lease);
        var renewalEntered = Signal(); var releaseRenewal = Signal(); var browserReturned = Signal();
        var renews = 0;
        database.Renew = async () => { if (++renews == 1) return true; renewalEntered.SetResult(); await releaseRenewal.Task; return true; };
        var root = Path.Combine(Path.GetTempPath(), "hsr-renewal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executor = new Executor(async (job, _) => { await renewalEntered.Task; browserReturned.SetResult(); return Bytes(job); });
            var run = new ObservationCoordinator(database, executor, new(root)).RunOnceAsync(lease.WorkerId, CancellationToken.None);
            await browserReturned.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(run.IsCompleted); Assert.Equal(0, database.Completes);
            releaseRenewal.SetResult(); var result = await run;
            Assert.Equal("Observed", result.State); Assert.Equal(1, database.Completes); Assert.Empty(database.Blocks);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task LostInitialAuthorityNeverAllocatesBrowserResourcesOrReleasesJob()
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid());
        var database = new Leases(lease) { Renew = () => Task.FromResult(false) };
        var executor = new Executor((_, _) => throw new InvalidOperationException());
        var result = await new ObservationCoordinator(database, executor, new("unused")).RunOnceAsync(lease.WorkerId, CancellationToken.None);
        Assert.Equal("Unconfirmed", result.State); Assert.Equal("WORKER_LEASE_UNCONFIRMED", result.ErrorCode);
        Assert.Equal(0, executor.Calls); Assert.Equal(0, database.Completes); Assert.Empty(database.Blocks);
    }
    [Fact]
    public async Task NativeOwnershipWriteFailureCannotReleaseAJobAsSafelyStopped()
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid()); var database = new Leases(lease);
        var executor = new Executor((_, _) => Task.FromException<byte[]>(new BrowserOperationException("WORKER_NATIVE_OWNERSHIP_UNCONFIRMED")));
        var result = await new ObservationCoordinator(database, executor, new("unused")).RunOnceAsync(lease.WorkerId, CancellationToken.None);
        Assert.Equal("Unconfirmed", result.State); Assert.Equal("WORKER_NATIVE_OWNERSHIP_UNCONFIRMED", result.ErrorCode);
        Assert.Empty(database.Blocks); Assert.Equal(0, database.Completes); Assert.Null(result.Reference);
    }
    [Fact]
    public async Task RequestedStopWaitsForActualCleanupBeforeRecordingSafeBlock()
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid()); var database = new Leases(lease);
        using var stop = new CancellationTokenSource(); var entered = Signal(); var canceled = Signal(); var cleanup = Signal();
        var executor = new Executor(async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { canceled.SetResult(); }
            await cleanup.Task; token.ThrowIfCancellationRequested(); return [];
        });
        var run = new ObservationCoordinator(database, executor, new("unused")).RunOnceAsync(lease.WorkerId, stop.Token);
        await entered.Task; await stop.CancelAsync(); await canceled.Task;
        Assert.False(run.IsCompleted); Assert.Empty(database.Blocks); Assert.Equal(0, database.Completes);
        cleanup.SetResult(); var result = await run;
        Assert.Equal("Blocked", result.State); Assert.Equal("WORKER_STOP_REQUESTED", result.ErrorCode);
        Assert.Equal(new[] { "WORKER_STOP_REQUESTED" }, database.Blocks); Assert.Equal(0, database.Completes);
    }
    [Theory]
    [InlineData("renew")]
    [InlineData("complete")]
    [InlineData("block")]
    public async Task DatabaseFailuresRemainUnconfirmedAndDoNotLeakPrivateErrors(string operation)
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job(), Guid.NewGuid(), Guid.NewGuid()); var database = new Leases(lease);
        if (operation == "renew") database.Renew = () => Task.FromException<bool>(new IOException("private-database-detail"));
        if (operation == "complete") database.Complete = _ => throw new IOException("private-database-detail");
        database.BlockThrows = operation == "block";
        var root = Path.Combine(Path.GetTempPath(), "hsr-db-fault-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executor = new Executor((job, _) => Task.FromResult(operation == "block" ? new byte[] { 1 } : Bytes(job)));
            var result = await new ObservationCoordinator(database, executor, new(root)).RunOnceAsync(lease.WorkerId, CancellationToken.None);
            Assert.Equal("Unconfirmed", result.State); Assert.DoesNotContain("private-database-detail", JsonSerializer.Serialize(result));
            Assert.Equal(operation switch { "renew" => "WORKER_LEASE_UNCONFIRMED", "complete" => "WORKER_COMPLETION_UNCONFIRMED", _ => "WORKER_BLOCK_UNCONFIRMED" }, result.ErrorCode);
            Assert.Equal(operation == "renew" ? 0 : 1, executor.Calls); Assert.Equal(operation == "complete" ? 1 : 0, database.Completes);
            if (operation == "complete") Assert.True(File.Exists(Path.Combine(root, result.Reference!)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
