using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using Npgsql;

namespace HeadlessScreenReader.Worker;

/// <summary>A storage-only capability. It cannot renew or complete an execution lease.</summary>
public sealed class RecordedPublicationLease
{
    public RetainedRecordingOutput Output { get; }
    [JsonIgnore] internal Guid Token { get; }
    public RecordedPublicationLease(RetainedRecordingOutput output, Guid token)
    {
        output.Validate(); if (token == Guid.Empty) throw new ArgumentException("WORKER_PUBLICATION_CAPABILITY_INVALID");
        Output = output; Token = token;
    }
    public override string ToString() => $"RecordedPublicationLease({Output.Job.JobId:N}, generation {Output.Job.Generation}, capability redacted)";
}
public sealed record RecordedPublicationClaim(string State, RecordedPublicationLease? Lease);
public interface IRecordedPublications
{
    Task<RecordedPublicationClaim> ClaimAsync(RetainedRecordingOutput output, CancellationToken token);
    Task<bool> RenewAsync(RecordedPublicationLease lease, CancellationToken token);
    Task<bool> CompleteAsync(RecordedPublicationLease lease, StoredObservation stored, CancellationToken token);
    Task<bool> FailAsync(RecordedPublicationLease lease, string code, CancellationToken token);
}

/// <summary>Four private storage functions; no queue claim, browser or execution capability.</summary>
public sealed class PostgresRecordedPublications : IRecordedPublications, IAsyncDisposable
{
    private readonly NpgsqlDataSource _source;
    public PostgresRecordedPublications(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            IncludeErrorDetail = false,
            LogParameters = false,
            CommandTimeout = 10,
            Timeout = 10,
            MaxPoolSize = 3,
            ApplicationName = "hsr-recording-publication"
        };
        var loopback = settings.Host is "localhost" or "127.0.0.1" or "::1";
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password)
            || !loopback && settings.SslMode != SslMode.VerifyFull)
            throw new ArgumentException("WORKER_DATABASE_CONFIGURATION_INVALID");
        _source = NpgsqlDataSource.Create(settings.ConnectionString);
    }
    public async Task<RecordedPublicationClaim> ClaimAsync(RetainedRecordingOutput output, CancellationToken token)
    {
        output.Validate(); var capability = Guid.NewGuid();
        await using var command = _source.CreateCommand("select * from hsr.claim_recorded_publication($1,$2,$3,$4,$5,$6,$7,30)");
        foreach (var value in new object[] { output.Job.OrganizationId, output.Job.ScanId, output.Job.JobId,
            output.Job.Generation, output.WorkerId, output.BundleSha256, capability }) command.Parameters.AddWithValue(value);
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await rows.ReadAsync(token).ConfigureAwait(false)) throw new InvalidDataException("WORKER_PUBLICATION_CLAIM_UNCONFIRMED");
        var state = rows.GetString(0);
        if (state is not ("Claimed" or "AlreadyCompleted" or "Rejected" or "Busy"))
            throw new InvalidDataException("WORKER_PUBLICATION_CLAIM_UNCONFIRMED");
        if (state is "Claimed" or "AlreadyCompleted")
        {
            if (rows.IsDBNull(2) || rows.GetBoolean(2) != output.Job.RecordObservation
                || rows.IsDBNull(3) || rows.GetInt32(3) != output.Job.TraversalRoute
                || state == "Claimed" && rows.IsDBNull(1)) throw new InvalidDataException("WORKER_PUBLICATION_SCOPE_UNCONFIRMED");
        }
        if (await rows.ReadAsync(token).ConfigureAwait(false)) throw new InvalidDataException("WORKER_PUBLICATION_CLAIM_UNCONFIRMED");
        return new(state, state == "Claimed" ? new(output, capability) : null);
    }
    public Task<bool> RenewAsync(RecordedPublicationLease lease, CancellationToken token) =>
        ExecuteAsync("select hsr.renew_recorded_publication($1,$2,$3,$4,$5,$6,30)", lease, [], token);
    public Task<bool> CompleteAsync(RecordedPublicationLease lease, StoredObservation stored, CancellationToken token)
    {
        var archive = stored.WorkerArchive ?? throw new InvalidDataException("WORKER_PUBLICATION_ARCHIVE_UNCONFIRMED");
        archive.Validate(lease.Output.Job, lease.Output.WorkerId);
        if (stored.Reference != lease.Output.Job.Reference) throw new InvalidDataException("WORKER_PUBLICATION_SCOPE_UNCONFIRMED");
        return ExecuteAsync("select hsr.complete_recorded_publication($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)", lease,
            [stored.Sha256, stored.Reference, archive.Receipt.Identity.IndexSha256, archive.Receipt.SourceManifestSha256, archive.Receipt.Bytes], token);
    }
    public Task<bool> FailAsync(RecordedPublicationLease lease, string code, CancellationToken token) =>
        ExecuteAsync("select hsr.fail_recorded_publication($1,$2,$3,$4,$5,$6,$7)", lease, [code], token);
    private async Task<bool> ExecuteAsync(string sql, RecordedPublicationLease lease, object[] values, CancellationToken token)
    {
        lease.Output.Validate();
        await using var command = _source.CreateCommand(sql);
        foreach (var value in new object[] { lease.Output.Job.OrganizationId, lease.Output.Job.JobId, lease.Output.Job.Generation,
            lease.Output.WorkerId, lease.Output.BundleSha256, lease.Token }) command.Parameters.AddWithValue(value);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }
    public ValueTask DisposeAsync() => _source.DisposeAsync();
}
