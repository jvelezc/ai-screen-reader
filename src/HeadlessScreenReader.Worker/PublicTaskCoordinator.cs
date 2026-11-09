using System.Text.Json;
using HeadlessScreenReader.Domain;
using Npgsql;
using NpgsqlTypes;

namespace HeadlessScreenReader.Worker;

internal sealed record PublicTaskLease(PublicTaskJob Job, Guid WorkerId, Guid Token);
internal sealed record PublicTaskClaim(string Status, Guid? Token = null, PublicTaskJob? Job = null);

internal sealed class PublicTaskLeases(NpgsqlDataSource database)
{
    internal async Task<bool> AvailableAsync(CancellationToken token)
    {
        await using var command = database.CreateCommand("select to_regprocedure('hsr.claim_public_task(uuid)') is not null");
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }
    internal async Task<PublicTaskClaim> ClaimAsync(Guid worker, CancellationToken token)
    {
        await using var command = database.CreateCommand("select hsr.claim_public_task($1)"); command.Parameters.AddWithValue(worker);
        var text = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
        if (text is null || text.Length > 16384) throw new BrowserOperationException("PUBLIC_TASK_CLAIM_UNCONFIRMED");
        var claim = JsonSerializer.Deserialize<PublicTaskClaim>(text, ReaderWorkerWire.Json) ?? throw new IOException();
        if (claim.Status == "Claimed") { claim.Job!.Validate(); if (claim.Token is null || claim.Token == Guid.Empty) throw new IOException(); }
        else if (claim.Job is not null || claim.Token is not null || claim.Status is not ("Empty" or "AtCapacity" or "InvalidMessage" or "Deferred" or "AuthorityLost")) throw new IOException();
        return claim;
    }
    internal Task<bool> RenewAsync(PublicTaskLease lease, string phase, CancellationToken token) =>
        ExecuteAsync("select hsr.renew_public_task($1,$2,$3,$4,$5)", lease, phase, false, token);
    internal Task<bool> FinishAsync(PublicTaskLease lease, PublicTaskPublication value, CancellationToken token) =>
        ExecuteAsync("select hsr.finish_public_task($1,$2,$3,$4,$5)", lease, JsonSerializer.Serialize(value, ReaderWorkerWire.Json), true, token);
    internal Task<bool> BlockAsync(PublicTaskLease lease, string code, CancellationToken token) =>
        ExecuteAsync("select hsr.block_public_task($1,$2,$3,$4,$5)", lease, code, false, token);
    private async Task<bool> ExecuteAsync(string sql, PublicTaskLease lease, string value, bool json, CancellationToken token)
    {
        await using var command = database.CreateCommand(sql);
        foreach (var parameter in new[] { lease.Job.OrganizationId, lease.Job.JobId, lease.WorkerId, lease.Token }) command.Parameters.AddWithValue(parameter);
        command.Parameters.AddWithValue(json ? NpgsqlDbType.Jsonb : NpgsqlDbType.Text, value);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }
}

internal sealed class PublicTaskCoordinator(PublicTaskLeases leases, PublicTaskExecutor executor, PublicTaskEvidenceStore evidence)
{
    internal async Task<PublicWebsiteRunResult> RunOnceAsync(Guid worker, CancellationToken token)
    {
        var claim = await leases.ClaimAsync(worker, token).ConfigureAwait(false);
        if (claim.Job is not { } job) return new(claim.Status);
        var lease = new PublicTaskLease(job, worker, claim.Token!.Value);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var heartbeatStop = new CancellationTokenSource(); var phase = "Starting"; var publishing = false;
        async Task<bool> Authorize(string stage, CancellationToken cancellation)
        {
            Volatile.Write(ref phase, stage);
            var allowed = await leases.RenewAsync(lease, stage, cancellation).ConfigureAwait(false);
            if (!allowed) await execution.CancelAsync().ConfigureAwait(false); return allowed;
        }
        var heartbeat = Heartbeat();
        try
        {
            var result = await executor.ExecuteAsync(job, worker, Authorize, execution.Token).ConfigureAwait(false);
            if (!result.ResourcesAbsent) throw new BrowserOperationException("PUBLIC_TASK_CLEANUP_UNCONFIRMED");
            publishing = true;
            foreach (var file in result.Bundle.Files)
            {
                if (!await Authorize("Finalizing", execution.Token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_TASK_AUTHORITY_LOST");
                await evidence.WriteAsync(job, PublicTaskRules.Hash(file.Value), file.Value, execution.Token).ConfigureAwait(false);
            }
            var report = result.Bundle.Report; var bytes = result.Bundle.Files["report.json"];
            var publication = new PublicTaskPublication("hsr-public-task-publication-v1", job.ParentOriginalSha256, PublicTaskRules.Hash(bytes), bytes.Length,
                result.Start.ExecutionId, true, true, PublicTaskAssessment.Evaluate(report, true, true)); publication.Validate(job);
            if (!await leases.FinishAsync(lease, publication, execution.Token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_TASK_PUBLICATION_UNCONFIRMED");
            return new("Observed", job.JobId);
        }
        catch (Exception error)
        {
            var code = error is BrowserOperationException { Code: "PUBLIC_TASK_CLEANUP_UNCONFIRMED" } ? "PUBLIC_TASK_CLEANUP_UNCONFIRMED"
                : execution.IsCancellationRequested ? "PUBLIC_TASK_AUTHORITY_LOST" : publishing ? "PUBLIC_TASK_PUBLICATION_UNCONFIRMED" : "PUBLIC_TASK_EXECUTION_UNCONFIRMED";
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await leases.BlockAsync(lease, code, deadline.Token).ConfigureAwait(false); } catch { }
            return new(code == "PUBLIC_TASK_CLEANUP_UNCONFIRMED" ? "Unconfirmed" : "Blocked", job.JobId, code);
        }
        finally { heartbeatStop.Cancel(); await heartbeat.ConfigureAwait(false); }

        async Task Heartbeat()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                while (await timer.WaitForNextTickAsync(heartbeatStop.Token).ConfigureAwait(false))
                    if (!await leases.RenewAsync(lease, Volatile.Read(ref phase), heartbeatStop.Token).ConfigureAwait(false)) { await execution.CancelAsync().ConfigureAwait(false); return; }
            }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
            catch { await execution.CancelAsync().ConfigureAwait(false); }
        }
    }
}
