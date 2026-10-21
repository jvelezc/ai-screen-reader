using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public interface IFixtureExecutor
{
    /// <summary>Return only after actual owned container removal is confirmed.</summary>
    Task<byte[]> ExecuteAsync(FixtureJob job, CancellationToken token);
}
public sealed record WorkerResult(string State, Guid? JobId = null, string? Reference = null, string? ErrorCode = null);

public sealed class ObservationCoordinator(IWorkerLeases leases, IFixtureExecutor executor, ObservationStore store)
{
    public async Task<WorkerResult> RunOnceAsync(Guid workerId, CancellationToken token)
    {
        var claim = await leases.ClaimAsync(workerId, token);
        if (claim.Lease is not { } lease) return new(claim.Status);
        // Reconfirm server authority before allocating any browser resources.
        try
        {
            if (!await leases.RenewAsync(lease, token))
                return new("Unconfirmed", lease.Job.JobId, ErrorCode: "WORKER_LEASE_UNCONFIRMED");
        }
        catch (Exception) { return new("Unconfirmed", lease.Job.JobId, ErrorCode: "WORKER_LEASE_UNCONFIRMED"); }
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(lease, heartbeatStop.Token, execution);
        StoredObservation? stored = null;
        string? error = null;
        try
        {
            var bytes = await executor.ExecuteAsync(lease.Job, execution.Token);
            execution.Token.ThrowIfCancellationRequested();
            stored = await store.PersistAsync(bytes, lease.Job, execution.Token);
        }
        catch (Exception exception)
        {
            error = exception is BrowserOperationException { Code: "WORKER_NATIVE_OWNERSHIP_UNCONFIRMED" }
                ? "WORKER_NATIVE_OWNERSHIP_UNCONFIRMED"
                : exception is BrowserOperationException { Code: "WORKER_CONTAINER_CLEANUP_UNCONFIRMED" }
                ? "OWNED_BROWSER_CLOSE_UNCONFIRMED"
                : token.IsCancellationRequested ? "WORKER_STOP_REQUESTED" : "FIXTURE_OBSERVATION_UNCONFIRMED";
        }
        finally { await heartbeatStop.CancelAsync(); }
        // Join the real database callback. Cancellation of a waiter does not release ownership.
        try { await heartbeat; }
        catch (Exception) { return new("Unconfirmed", lease.Job.JobId, stored?.Reference, "WORKER_LEASE_UNCONFIRMED"); }
        using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        if (error is not null)
        {
            // Never release a job as safely stopped while owned cleanup is uncertain.
            if (error is "OWNED_BROWSER_CLOSE_UNCONFIRMED" or "WORKER_NATIVE_OWNERSHIP_UNCONFIRMED") return new("Unconfirmed", lease.Job.JobId, ErrorCode: error);
            bool blocked;
            try { blocked = await leases.BlockAsync(lease, error, finish.Token); }
            catch (Exception) { return new("Unconfirmed", lease.Job.JobId, ErrorCode: "WORKER_BLOCK_UNCONFIRMED"); }
            return new(blocked ? "Blocked" : "Unconfirmed", lease.Job.JobId, ErrorCode: error);
        }
        // Persisted bytes can remain orphaned after lease loss; they do not imply database completion.
        bool completed;
        try { completed = await leases.CompleteAsync(lease, stored!, finish.Token); }
        catch (Exception) { return new("Unconfirmed", lease.Job.JobId, stored!.Reference, "WORKER_COMPLETION_UNCONFIRMED"); }
        return new(completed ? "Observed" : "Unconfirmed", lease.Job.JobId, stored!.Reference,
            completed ? null : "WORKER_COMPLETION_UNCONFIRMED");
    }

    private async Task HeartbeatAsync(WorkerLease lease, CancellationToken stop, CancellationTokenSource execution)
    {
        try
        {
            while (true)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), stop); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                // Do not cancel the actual renew callback when finishing a browser observation.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                if (!await leases.RenewAsync(lease, timeout.Token)) throw new InvalidDataException("WORKER_LEASE_UNCONFIRMED");
                if (stop.IsCancellationRequested) return;
            }
        }
        catch { await execution.CancelAsync(); throw; }
    }
}
