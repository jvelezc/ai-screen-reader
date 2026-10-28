using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using Npgsql;

namespace HeadlessScreenReader.Worker;

/// <summary>The bearer is available only to the trusted database adapter.</summary>
public sealed class WorkerLease(FixtureJob job, Guid workerId, Guid token)
{
    public FixtureJob Job { get; } = job;
    public Guid WorkerId { get; } = workerId;
    [JsonIgnore] internal Guid Token { get; } = token;
    public override string ToString() => $"WorkerLease({Job.JobId:N}, generation {Job.Generation}, capability redacted)";
}

public sealed record WorkerClaim(string Status, WorkerLease? Lease);
public interface IWorkerLeases
{
    Task<WorkerClaim> ClaimAsync(Guid worker, CancellationToken token);
    Task<bool> RenewAsync(WorkerLease lease, CancellationToken token);
    Task<bool> CompleteAsync(WorkerLease lease, StoredObservation stored, CancellationToken token);
    Task<bool> BlockAsync(WorkerLease lease, string code, CancellationToken token);
}

/// <summary>Only four fenced functions; no queue or tenant table access.</summary>
public sealed class PostgresWorkerLeases : IWorkerLeases, IAsyncDisposable
{
    private readonly NpgsqlDataSource _source;
    public PostgresWorkerLeases(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            IncludeErrorDetail = false,
            LogParameters = false,
            CommandTimeout = 10,
            Timeout = 10,
            MaxPoolSize = 3,
            ApplicationName = "hsr-observation-worker"
        };
        var loopback = settings.Host is "localhost" or "127.0.0.1" or "::1";
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password)
            || !loopback && settings.SslMode != SslMode.VerifyFull)
            throw new ArgumentException("WORKER_DATABASE_CONFIGURATION_INVALID");
        _source = NpgsqlDataSource.Create(settings.ConnectionString);
    }

    public async Task<WorkerClaim> ClaimAsync(Guid worker, CancellationToken token)
    {
        await using var command = _source.CreateCommand("select * from hsr.claim_scan_job($1,30)");
        command.Parameters.AddWithValue(worker);
        await using var rows = await command.ExecuteReaderAsync(token);
        if (!await rows.ReadAsync(token)) throw new InvalidDataException("WORKER_CLAIM_UNCONFIRMED");
        var status = rows.GetString(0);
        WorkerLease? lease = null;
        if (status == "Claimed")
        {
            var job = new FixtureJob(rows.GetGuid(1), rows.GetGuid(2), rows.GetGuid(3), rows.GetInt64(4));
            job.Validate();
            lease = new(job, worker, rows.GetGuid(5));
        }
        else if (status is not ("Empty" or "Deferred" or "Deduplicated" or "Rejected" or "Blocked"))
            throw new InvalidDataException("WORKER_CLAIM_UNCONFIRMED");
        if (await rows.ReadAsync(token)) throw new InvalidDataException("WORKER_CLAIM_UNCONFIRMED");
        return new(status, lease);
    }

    public Task<bool> RenewAsync(WorkerLease lease, CancellationToken token) =>
        ExecuteAsync("select hsr.renew_scan_job($1,$2,$3,$4,$5,30)", lease, [], token);
    public Task<bool> CompleteAsync(WorkerLease lease, StoredObservation stored, CancellationToken token)
    {
        if (stored.WorkerArchive is not { } producer)
            return ExecuteAsync("select hsr.complete_scan_job($1,$2,$3,$4,$5,$6,$7)", lease, [stored.Sha256, stored.Reference], token);
        producer.Validate(lease.Job, lease.WorkerId);
        return ExecuteAsync("select hsr.complete_scan_job_with_archive($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)", lease,
            [stored.Sha256, stored.Reference, producer.Receipt.Identity.IndexSha256, producer.Receipt.SourceManifestSha256, producer.Receipt.Bytes], token);
    }
    public Task<bool> BlockAsync(WorkerLease lease, string code, CancellationToken token) =>
        ExecuteAsync("select hsr.block_scan_job($1,$2,$3,$4,$5,$6)", lease, [code], token);

    private async Task<bool> ExecuteAsync(string sql, WorkerLease lease, object[] values, CancellationToken token)
    {
        await using var command = _source.CreateCommand(sql);
        command.Parameters.AddWithValue(lease.Job.OrganizationId);
        command.Parameters.AddWithValue(lease.Job.JobId);
        command.Parameters.AddWithValue(lease.WorkerId);
        command.Parameters.AddWithValue(lease.Job.Generation);
        command.Parameters.AddWithValue(lease.Token);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return await command.ExecuteScalarAsync(token) is true;
    }
    public ValueTask DisposeAsync() => _source.DisposeAsync();
}
