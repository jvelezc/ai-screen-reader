using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using Npgsql;

namespace HeadlessScreenReader.Worker;

public sealed class PublicWebsiteLease
{
    public PublicWebsiteJob Job { get; }
    public Guid WorkerId { get; }
    [JsonIgnore] internal Guid Token { get; }
    public PublicWebsiteLease(PublicWebsiteJob job, Guid worker, Guid token)
    {
        job.Validate();
        if (worker == Guid.Empty || token == Guid.Empty) throw new ArgumentException("PUBLIC_WEBSITE_CAPABILITY_INVALID");
        Job = job; WorkerId = worker; Token = token;
    }
    public override string ToString() => $"PublicWebsiteLease({Job.JobId:N}, generation {Job.Generation}, capability redacted)";
}
public sealed record PublicWebsiteClaim(string Status, PublicWebsiteLease? Lease);
public interface IPublicWebsiteLeases
{
    Task<PublicWebsiteClaim> ClaimAsync(Guid worker, CancellationToken token);
    Task<bool> RenewAsync(PublicWebsiteLease lease, CancellationToken token);
    Task<bool> CompleteAsync(PublicWebsiteLease lease, PublicWebsiteObservation value, CancellationToken token);
    Task<bool> BlockAsync(PublicWebsiteLease lease, string code, CancellationToken token);
}

public sealed class PostgresPublicWebsiteLeases(string connectionString) : IPublicWebsiteLeases, IAsyncDisposable
{
    private readonly NpgsqlDataSource _source = WorkerDatabase.Create(connectionString);
    private sealed record ClaimWire([property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("token")] Guid? Token = null, [property: JsonPropertyName("job")] PublicWebsiteJob? Job = null);

    public async Task VerifySchemaAsync(CancellationToken token)
    {
        await using var command = _source.CreateCommand("select to_regprocedure('hsr.claim_public_scan(uuid)') is not null and to_regprocedure('hsr.renew_public_scan(uuid,uuid,bigint,uuid,uuid)') is not null and to_regprocedure('hsr.finish_public_scan(uuid,uuid,bigint,uuid,uuid,text,text)') is not null and to_regprocedure('hsr.block_public_scan(uuid,uuid,bigint,uuid,uuid,text)') is not null");
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not true) throw new InvalidDataException("PUBLIC_WEBSITE_SCHEMA_REQUIRED");
    }

    public async Task<PublicWebsiteClaim> ClaimAsync(Guid worker, CancellationToken token)
    {
        if (worker == Guid.Empty) throw new ArgumentException("PUBLIC_WEBSITE_WORKER_INVALID");
        await using var command = _source.CreateCommand("select hsr.claim_public_scan($1)");
        command.Parameters.AddWithValue(worker);
        var result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        if (result is not string json || json.Length is 0 or > 16384) throw new InvalidDataException("PUBLIC_WEBSITE_CLAIM_UNCONFIRMED");
        try
        {
            var wire = JsonSerializer.Deserialize<ClaimWire>(json, ObservationStore.Json) ?? throw new InvalidDataException();
            if (wire.Status == "Claimed")
            {
                if (wire.Job is null || wire.Token is not { } capability || capability == Guid.Empty) throw new InvalidDataException();
                return new(wire.Status, new(wire.Job, worker, capability));
            }
            if (wire.Token is not null || wire.Job is not null || wire.Status is not ("Empty" or "AtCapacity" or "InvalidMessage"
                or "Unconfirmed" or "AuthorityLost" or "WorkspaceBusy" or "Deferred")) throw new InvalidDataException();
            return new(wire.Status, null);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or BrowserOperationException or ArgumentException)
        {
            throw new InvalidDataException("PUBLIC_WEBSITE_CLAIM_UNCONFIRMED");
        }
    }

    public Task<bool> RenewAsync(PublicWebsiteLease lease, CancellationToken token) =>
        ExecuteAsync("select hsr.renew_public_scan($1,$2,$3,$4,$5)", lease, [], token);

    public Task<bool> CompleteAsync(PublicWebsiteLease lease, PublicWebsiteObservation value, CancellationToken token)
    {
        var stored = PublicWebsiteObservationCodec.Encode(value, lease.Job, lease.WorkerId);
        return ExecuteAsync("select hsr.finish_public_scan($1,$2,$3,$4,$5,$6,$7)", lease, [stored.Text, stored.Sha256], token);
    }

    public Task<bool> BlockAsync(PublicWebsiteLease lease, string code, CancellationToken token)
    {
        if (code is not ("PUBLIC_WEBSITE_SOURCE_UNCONFIRMED" or "PUBLIC_WEBSITE_AUTHORITY_LOST" or "PUBLIC_WEBSITE_CLEANUP_UNCONFIRMED"
            or "PUBLIC_WEBSITE_PUBLICATION_UNCONFIRMED" or "PUBLIC_WEBSITE_EXECUTION_CANCELLED"))
            throw new ArgumentException("PUBLIC_WEBSITE_FAILURE_CODE_INVALID");
        return ExecuteAsync("select hsr.block_public_scan($1,$2,$3,$4,$5,$6)", lease, [code], token);
    }

    private async Task<bool> ExecuteAsync(string sql, PublicWebsiteLease lease, object[] values, CancellationToken token)
    {
        lease.Job.Validate();
        await using var command = _source.CreateCommand(sql);
        foreach (var value in new object[] { lease.Job.OrganizationId, lease.Job.JobId, lease.Job.Generation, lease.WorkerId, lease.Token })
            command.Parameters.AddWithValue(value);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }

    public ValueTask DisposeAsync() => _source.DisposeAsync();
}
