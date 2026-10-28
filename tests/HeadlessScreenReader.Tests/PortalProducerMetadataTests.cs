using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class PortalProducerMetadataTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("legacy")]
    [InlineData("partial")]
    [InlineData("hash")]
    [InlineData("worker")]
    [InlineData("bytes")]
    [InlineData("pending")]
    public async Task ProviderProjectionRequiresAllProducerFieldsAndCompletedAttempt(string scenario)
    {
        var job = new HeadlessScreenReader.Domain.FixtureJob(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1);
        var worker = Guid.NewGuid();
        var row = JsonSerializer.SerializeToNode(new
        {
            organization_id = job.OrganizationId,
            scan_id = job.ScanId,
            id = job.JobId,
            state = "completed",
            generation = 1,
            result_digest = new string('c', 64),
            result_reference = job.Reference,
            last_error_code = (string?)null,
            archive_index_sha256 = new string('a', 64),
            archive_manifest_sha256 = new string('b', 64),
            archive_bytes = 800,
            archive_worker_id = worker
        })!;
        switch (scenario)
        {
            case "legacy": foreach (var field in new[] { "archive_index_sha256", "archive_manifest_sha256", "archive_bytes", "archive_worker_id" }) row[field] = null; break;
            case "partial": row["archive_manifest_sha256"] = null; break;
            case "hash": row["archive_index_sha256"] = new string('A', 64); break;
            case "worker": row["archive_worker_id"] = Guid.Empty; break;
            case "bytes": row["archive_bytes"] = 1052673; break;
            case "pending": row["state"] = "leased"; row["result_digest"] = null; row["result_reference"] = null; break;
        }
        using var http = PortalProjectClientTests.Client((request, _) =>
        {
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization!.Parameter);
            Assert.Contains("archive_index_sha256", request.RequestUri!.Query);
            Assert.Contains("organization_id=eq." + job.OrganizationId.ToString("D"), request.RequestUri.Query);
            return Task.FromResult(PortalProjectClientTests.Response(new JsonArray(row).ToJsonString()));
        });
        var client = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        var actor = new PortalIdentity(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
        if (scenario is "valid" or "legacy")
        {
            var result = await client.JobAsync(actor, job.OrganizationId, job.ScanId, default);
            Assert.NotNull(result); Assert.Equal(job.JobId, result.JobId);
            if (scenario == "legacy") Assert.Null(result.WorkerArchive);
            else
            {
                Assert.NotNull(result.WorkerArchive); result.WorkerArchive.Validate(job, worker);
                Assert.Equal(800, result.WorkerArchive.Receipt.Bytes);
                Assert.Equal(new string('a', 64), result.WorkerArchive.Receipt.Identity.IndexSha256);
            }
        }
        else Assert.Equal("CONTROL_PROVIDER_RESPONSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => client.JobAsync(actor, job.OrganizationId, job.ScanId, default))).Code);
    }
}
