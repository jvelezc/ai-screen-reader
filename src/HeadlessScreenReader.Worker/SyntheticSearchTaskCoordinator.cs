using System.Text.Json;
using HeadlessScreenReader.Domain;
using Npgsql;
using NpgsqlTypes;

namespace HeadlessScreenReader.Worker;

internal sealed record SearchTaskClaim(string Status, Guid? Token = null, SyntheticSearchTaskJob? Job = null,
    Guid? ProducerId = null, SyntheticSearchTaskPublication? Prepared = null, Guid? RejectedJobId = null);
internal sealed record SearchTaskLease(SyntheticSearchTaskJob Job, Guid WorkerId, Guid Token, Guid ProducerId);
internal sealed record SearchTaskRunResult(string State, Guid? JobId = null, Guid? ProducerId = null,
    Guid? ExecutionId = null, string? ErrorCode = null, string? ArchiveFailure = null, bool PublicationOnly = false)
{
    public string Schema => "hsr-search-task-run-v1";
    public bool WebsiteInputReplayed => false;
}

internal sealed class SearchTaskLeases(NpgsqlDataSource database)
{
    internal async Task<bool> AvailableAsync(CancellationToken token)
    {
        await using var command = database.CreateCommand("""
            select bool_and(coalesce(has_function_privilege(to_regprocedure(signature),'EXECUTE'),false))
            from unnest(array['hsr.claim_search_task(uuid)','hsr.renew_search_task(uuid,uuid,uuid,uuid,text)',
                'hsr.prepare_search_task_publication(uuid,uuid,uuid,uuid,jsonb)','hsr.claim_search_publication(uuid)',
                'hsr.renew_search_publication(uuid,uuid,uuid,uuid)','hsr.finish_search_task(uuid,uuid,uuid,uuid,jsonb)',
                'hsr.block_search_task(uuid,uuid,uuid,uuid,text)']) signature
            """);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }
    internal async Task<SearchTaskClaim> ClaimAsync(Guid worker, bool publicationOnly, CancellationToken token)
    {
        await using var command = database.CreateCommand(publicationOnly ? "select hsr.claim_search_publication($1)" : "select hsr.claim_search_task($1)");
        command.Parameters.AddWithValue(worker);
        var text = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
        if (text is null || text.Length > 16384) throw Invalid();
        var claim = JsonSerializer.Deserialize<SearchTaskClaim>(text, ReaderWorkerWire.Json) ?? throw Invalid();
        if (claim.Status is "Claimed" or "PublicationOnly")
        {
            if (claim.Job is null || claim.Token is null || claim.Token == Guid.Empty || claim.RejectedJobId is not null || publicationOnly != (claim.Status == "PublicationOnly")) throw Invalid();
            claim.Job.Validate();
            if (publicationOnly)
            {
                if (claim.ProducerId is null || claim.ProducerId == Guid.Empty || claim.Prepared is null) throw Invalid();
                claim.Prepared.ValidatePrepared();
            }
            else if (claim.Prepared is not null || claim.ProducerId is not null) throw Invalid();
        }
        else if (claim.Status is not ("Empty" or "AtCapacity" or "InvalidMessage" or "Deferred" or "AuthorityLost")
            || claim.Job is not null || claim.Token is not null || claim.ProducerId is not null || claim.Prepared is not null
            || (claim.Status == "AuthorityLost" ? claim.RejectedJobId is null || claim.RejectedJobId == Guid.Empty : claim.RejectedJobId is not null)) throw Invalid();
        return claim;
    }
    internal Task<bool> RenewAsync(SearchTaskLease lease, string phase, bool publicationOnly, CancellationToken token) => publicationOnly
        ? ExecuteAsync("select hsr.renew_search_publication($1,$2,$3,$4)", lease, null, false, token)
        : ExecuteAsync("select hsr.renew_search_task($1,$2,$3,$4,$5)", lease, phase, false, token);
    internal Task<bool> PrepareAsync(SearchTaskLease lease, SyntheticSearchTaskPublication prepared, CancellationToken token) =>
        ExecuteAsync("select hsr.prepare_search_task_publication($1,$2,$3,$4,$5)", lease, JsonSerializer.Serialize(prepared, ReaderWorkerWire.Json), true, token);
    internal Task<bool> FinishAsync(SearchTaskLease lease, SyntheticSearchTaskPublication publication, CancellationToken token) =>
        ExecuteAsync("select hsr.finish_search_task($1,$2,$3,$4,$5)", lease, JsonSerializer.Serialize(publication, ReaderWorkerWire.Json), true, token);
    internal Task<bool> BlockAsync(SearchTaskLease lease, string code, CancellationToken token) =>
        ExecuteAsync("select hsr.block_search_task($1,$2,$3,$4,$5)", lease, code, false, token);
    private async Task<bool> ExecuteAsync(string sql, SearchTaskLease lease, string? value, bool json, CancellationToken token)
    {
        await using var command = database.CreateCommand(sql);
        foreach (var parameter in new[] { lease.Job.OrganizationId, lease.Job.JobId, lease.WorkerId, lease.Token }) command.Parameters.AddWithValue(parameter);
        if (value is not null) command.Parameters.AddWithValue(json ? NpgsqlDbType.Jsonb : NpgsqlDbType.Text, value);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }
    private static BrowserOperationException Invalid() => new("SEARCH_TASK_CLAIM_UNCONFIRMED");
}

internal sealed class SyntheticSearchTaskCoordinator(SearchTaskLeases leases, IsolatedFixtureExecutor executor, SyntheticSearchTaskArchive archive)
{
    internal async Task<SearchTaskRunResult> RunOnceAsync(Guid worker, CancellationToken token)
    {
        var claim = await leases.ClaimAsync(worker, publicationOnly: true, token).ConfigureAwait(false);
        if (claim.Status == "AuthorityLost") return new("AuthorityLost", claim.RejectedJobId, ErrorCode: "SEARCH_TASK_AUTHORITY_LOST", PublicationOnly: true);
        if (claim.Status != "PublicationOnly") claim = await leases.ClaimAsync(worker, publicationOnly: false, token).ConfigureAwait(false);
        if (claim.Job is not { } job) return new(claim.Status, claim.RejectedJobId, ErrorCode: claim.Status == "AuthorityLost" ? "SEARCH_TASK_AUTHORITY_LOST" : null);
        var publicationOnly = claim.Status == "PublicationOnly";
        var lease = new SearchTaskLease(job, worker, claim.Token!.Value, claim.ProducerId ?? worker);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var heartbeatStop = new CancellationTokenSource();
        using var leaseGate = new SemaphoreSlim(1, 1);
        var storageLease = publicationOnly; var finalizing = publicationOnly; var phase = "Collecting";
        SyntheticSearchTaskPublication? prepared = claim.Prepared;
        var heartbeat = Heartbeat();
        async Task<bool> Authorize(CancellationToken cancellation)
        {
            await leaseGate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var allowed = await leases.RenewAsync(lease, phase, storageLease, cancellation).ConfigureAwait(false);
                if (!allowed) await execution.CancelAsync().ConfigureAwait(false);
                return allowed;
            }
            finally { leaseGate.Release(); }
        }
        try
        {
            if (!publicationOnly)
            {
                var result = await executor.ExecuteSearchTaskAsync(job, worker, Authorize, execution.Token).ConfigureAwait(false);
                if (!result.ContainerAbsenceConfirmed) throw new BrowserOperationException("SEARCH_TASK_CLEANUP_UNCONFIRMED");
                finalizing = true; Volatile.Write(ref phase, "Publishing");
                prepared = await archive.PrepareAsync(result, Authorize, execution.Token).ConfigureAwait(false);
                // Serialize the transition with heartbeats: an old-mode renew must not cancel a newly acquired storage lease.
                await leaseGate.WaitAsync(execution.Token).ConfigureAwait(false);
                try
                {
                    if (!await leases.PrepareAsync(lease, prepared, execution.Token).ConfigureAwait(false)) throw new BrowserOperationException("SEARCH_TASK_AUTHORITY_LOST");
                    storageLease = true;
                }
                finally { leaseGate.Release(); }
            }
            var publication = await archive.PublishAsync(job, lease.ProducerId, prepared!, Authorize, execution.Token).ConfigureAwait(false);
            if (!await leases.FinishAsync(lease, publication, execution.Token).ConfigureAwait(false)) throw new BrowserOperationException("SEARCH_TASK_PUBLICATION_UNCONFIRMED");
            return new("Observed", job.JobId, lease.ProducerId, publication.ExecutionId, PublicationOnly: publicationOnly);
        }
        catch (Exception error)
        {
            var cleanup = error is BrowserOperationException { Code: "WORKER_CONTAINER_CLEANUP_UNCONFIRMED" or "WORKER_NATIVE_OWNERSHIP_UNCONFIRMED" or "SEARCH_TASK_CLEANUP_UNCONFIRMED" };
            var invalid = error is TraversalArchiveException { Failure: TraversalArchiveFailure.Invalid }
                or BrowserOperationException { Code: "SEARCH_TASK_ARCHIVE_INVALID" or "SEARCH_TASK_BUNDLE_UNCONFIRMED" or "SEARCH_TASK_PUBLICATION_INVALID" };
            var code = cleanup ? "SEARCH_TASK_CLEANUP_UNCONFIRMED" : execution.IsCancellationRequested ? "SEARCH_TASK_AUTHORITY_LOST"
                : finalizing ? invalid ? "SEARCH_TASK_PUBLICATION_INVALID" : "SEARCH_TASK_PUBLICATION_UNCONFIRMED" : "SEARCH_TASK_EXECUTION_UNCONFIRMED";
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await leases.BlockAsync(lease, code, deadline.Token).ConfigureAwait(false); } catch { }
            return new(cleanup ? "Unconfirmed" : "Blocked", job.JobId, lease.ProducerId, prepared?.ExecutionId, code,
                error is TraversalArchiveException failure ? failure.Failure.ToString() : finalizing ? "Unconfirmed" : null, publicationOnly);
        }
        finally { heartbeatStop.Cancel(); await heartbeat.ConfigureAwait(false); }

        async Task Heartbeat()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                while (await timer.WaitForNextTickAsync(heartbeatStop.Token).ConfigureAwait(false))
                    if (!await Authorize(heartbeatStop.Token).ConfigureAwait(false)) return;
            }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
            catch { await execution.CancelAsync().ConfigureAwait(false); }
        }
    }
}
