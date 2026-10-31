using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record RecordedPublicationResult(string State, FixtureJob Job, Guid Producer, string BundleSha256,
    string? Reference = null, string? ErrorCode = null)
{
    public string Schema => "hsr-recorded-publication-recovery-v1";
    public int InputsDispatchedByRecovery => 0;
    public bool CanReplay => false;
    public bool IsCompletionReceipt => State == "Published";
    public string GoalVerification => "NotAssessed";
}

/// <summary>Only storage finalization. Deliberately has no executor, reader or input capability.</summary>
public sealed class RecordedPublicationRecovery(IRecordedPublications publications, ObservationStore store, TimeProvider time)
{
    public async Task<RecordedPublicationResult> RunAsync(RecordedOutputSnapshot snapshot, CancellationToken token)
    {
        var output = RecordedOutputProjection.Manifest(snapshot, snapshot.Header.Job, snapshot.Header.WorkerId);
        RecordedPublicationResult Result(string state, string? code = null, string? reference = null) =>
            new(state, snapshot.Header.Job, snapshot.Header.WorkerId, snapshot.Seal.BundleSha256, reference, code);
        if (output is null) return Result("Unconfirmed", "WORKER_PUBLICATION_CLEANUP_UNCONFIRMED");
        RecordedPublicationClaim claim;
        try { claim = await publications.ClaimAsync(output, token).ConfigureAwait(false); }
        catch (Exception) { return Result("Unconfirmed", "WORKER_PUBLICATION_CLAIM_UNCONFIRMED"); }
        if (claim.State is "AlreadyCompleted" or "Rejected" or "Busy" && claim.Lease is null) return Result(claim.State);
        if (claim.State != "Claimed" || claim.Lease is not { } lease || lease.Output != output)
            return Result("Unconfirmed", "WORKER_PUBLICATION_SCOPE_UNCONFIRMED");
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task<bool> Authorize(CancellationToken cancellation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12), time);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token);
            return await publications.RenewAsync(lease, stop.Token).ConfigureAwait(false);
        }
        try
        {
            if (!await Authorize(operation.Token).ConfigureAwait(false)) return Result("Unconfirmed", "WORKER_PUBLICATION_FENCE_UNCONFIRMED");
        }
        catch (Exception) { return Result("Unconfirmed", "WORKER_PUBLICATION_FENCE_UNCONFIRMED"); }
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(lease, heartbeatStop.Token, operation);
        StoredObservation? stored = null; string? error = null;
        try { stored = await store.RecoverAsync(snapshot, Authorize, operation.Token).ConfigureAwait(false); }
        catch (Exception exception)
        {
            error = token.IsCancellationRequested ? "WORKER_PUBLICATION_STOP_REQUESTED" : exception switch
            {
                BrowserOperationException failure when SafeCode(failure.Code) => failure.Code,
                TraversalArchiveException => "WORKER_PUBLICATION_STORAGE_UNCONFIRMED",
                ObservationArchiveException => "WORKER_PUBLICATION_STORAGE_UNCONFIRMED",
                IOException or UnauthorizedAccessException => "WORKER_PUBLICATION_STORAGE_UNCONFIRMED",
                _ => "WORKER_PUBLICATION_UNCONFIRMED"
            };
        }
        finally { await heartbeatStop.CancelAsync().ConfigureAwait(false); }
        // Join the actual renewal callback before completion/failure; cancelling a waiter cannot release ownership.
        try { await heartbeat.ConfigureAwait(false); }
        catch (Exception) { return Result("Unconfirmed", "WORKER_PUBLICATION_FENCE_UNCONFIRMED", stored?.Reference); }
        using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(12), time);
        if (error is not null)
        {
            try
            {
                return Result(await publications.FailAsync(lease, error, finish.Token).ConfigureAwait(false) ? "Failed" : "Unconfirmed", error);
            }
            catch (Exception) { return Result("Unconfirmed", "WORKER_PUBLICATION_FAILURE_UNCONFIRMED"); }
        }
        bool completed;
        try { completed = await publications.CompleteAsync(lease, stored!, finish.Token).ConfigureAwait(false); }
        catch (Exception) { return Result("Unconfirmed", "WORKER_PUBLICATION_COMPLETION_UNCONFIRMED", stored!.Reference); }
        return Result(completed ? "Published" : "Unconfirmed", completed ? null : "WORKER_PUBLICATION_COMPLETION_UNCONFIRMED", stored!.Reference);
    }

    private async Task HeartbeatAsync(RecordedPublicationLease lease, CancellationToken stop, CancellationTokenSource operation)
    {
        try
        {
            while (true)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), time, stop).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12), time);
                if (!await publications.RenewAsync(lease, timeout.Token).ConfigureAwait(false))
                    throw new InvalidDataException("WORKER_PUBLICATION_FENCE_UNCONFIRMED");
                if (stop.IsCancellationRequested) return;
            }
        }
        catch { await operation.CancelAsync().ConfigureAwait(false); throw; }
    }
    private static bool SafeCode(string code) => code.StartsWith("WORKER_PUBLICATION_", StringComparison.Ordinal)
        && code.Length <= 128 && code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
}
