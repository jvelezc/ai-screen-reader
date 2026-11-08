using System.Net;
using Npgsql;

namespace HeadlessScreenReader.Worker;

/// <summary>Private, input-free dependency checks. Never claims a job or opens a website.</summary>
internal static class WorkerHealth
{
    internal sealed record Check(string Component, string State);
    internal sealed record Receipt(string Schema, Check[] Components, int InputsDispatched);

    internal static async Task<Receipt> ProbeAsync(string root, string image, string? connection,
        S3EvidenceConfiguration? storage, CancellationToken token)
    {
        var checks = new List<Check>();
        await CheckAsync("DatabaseQueue", async () =>
        {
            await using var database = WorkerDatabase.Create(connection ?? "");
            await using var leases = new PostgresPublicWebsiteLeases(database);
            await leases.VerifySchemaAsync(token).ConfigureAwait(false);
        });
        var broker = new ReaderHostClient(root, image);
        checks.Add(new("ReaderBroker", await broker.ReadyAsync().ConfigureAwait(false) ? "Ready" : "Unavailable"));
        checks.Add(new("PublicProcessor", await broker.PublicJobsReadyAsync().ConfigureAwait(false) ? "Ready" : "Unavailable"));
        await CheckAsync("EvidenceDisk", async () =>
        {
            // A separate disposable file, never an original or ownership journal.
            var path = Path.Combine(root, ".health-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                await file.WriteAsync(new byte[] { 1 }, token).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
                file.Position = 0;
                if (file.ReadByte() != 1) throw new IOException();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
        try
        {
            var configuration = storage;
            if (configuration is null) checks.Add(new("EvidenceStorage", "NotConfigured"));
            else await CheckAsync("EvidenceStorage", async () =>
            {
                using var http = configuration.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(configuration.Endpoint, configuration.Bucket));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK) throw new IOException();
            });
        }
        catch { checks.Add(new("EvidenceStorage", "Unavailable")); }
        return new("hsr-worker-health-v1", [.. checks], 0);

        async Task CheckAsync(string name, Func<Task> probe)
        {
            try { await probe().ConfigureAwait(false); checks.Add(new(name, "Ready")); }
            catch { checks.Add(new(name, "Unavailable")); }
        }
    }
}

/// <summary>Closed diagnostic vocabulary; exception messages and arbitrary SQL details are never logged.</summary>
internal sealed record WorkerStopDiagnostic(string Schema, string Stage, string Category, string? SqlState, bool Retryable)
{
    internal static WorkerStopDiagnostic From(string stage, Exception error) => new("hsr-worker-stop-v1", stage,
        error switch
        {
            PostgresException => "DatabaseRejected",
            NpgsqlException => "DatabaseUnavailable",
            IOException => "IoUnavailable",
            OperationCanceledException => "Canceled",
            _ => "Unexpected"
        },
        error is PostgresException pg && pg.SqlState is "08006" or "08001" or "53300" or "57P01" or "57P03" ? pg.SqlState : null,
        error is NpgsqlException { IsTransient: true });
}
