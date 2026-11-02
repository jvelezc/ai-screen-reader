using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;
using static HeadlessScreenReader.Tests.PortalProjectClientTests;

namespace HeadlessScreenReader.Tests;

public sealed class PortalPermissionProjectTests
{
    private static readonly DateTimeOffset Clock = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PortalIdentity Identity = new(Actor, "reader@example.invalid", Bearer);
    private static string JobRow(string state = "leased") => JsonSerializer.Serialize(new[] { new
    {
        organization_id = Organization, scan_id = Scan, id = Job, state, generation = 1,
        result_digest = (string?)null, result_reference = (string?)null, last_error_code = (string?)null,
        archive_index_sha256 = (string?)null, archive_manifest_sha256 = (string?)null,
        archive_bytes = (long?)null, archive_worker_id = (Guid?)null, record_observation = false, traversal_route = 0
    } });
    private static JsonNode Permission(string status = "Active") => JsonSerializer.SerializeToNode(new[] { new
    { organization_id = Organization, scan_id = Scan, job_id = Job, generation = 1, status, checked_at = Clock, expires_at = status == "Missing" ? (DateTimeOffset?)null : Clock.AddTicks(status == "Active" ? 1 : -1) } })!;

    [Theory]
    [InlineData("Active")]
    [InlineData("Expired")]
    [InlineData("Missing")]
    public async Task ProviderPermissionUsesOriginalBearerExactJobAndDatabaseTimestamp(string status)
    {
        var calls = 0;
        using var http = Client(async (request, token) =>
        {
            calls++; Assert.Equal(Bearer, request.Headers.Authorization!.Parameter);
            Assert.Equal(Key, Assert.Single(request.Headers.GetValues("apikey")));
            if (calls == 1) { Assert.Equal(HttpMethod.Get, request.Method); return Response(JobRow()); }
            Assert.Equal(2, calls); Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/rest/v1/rpc/read_scan_job_permission", request.RequestUri!.AbsolutePath);
            Assert.Equal("hsr", Assert.Single(request.Headers.GetValues("Content-Profile")));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(4, body.RootElement.EnumerateObject().Count());
            Assert.Equal(Organization, body.RootElement.GetProperty("p_org").GetGuid());
            Assert.Equal(Scan, body.RootElement.GetProperty("p_scan").GetGuid());
            Assert.Equal(Job, body.RootElement.GetProperty("p_job").GetGuid());
            Assert.Equal(1, body.RootElement.GetProperty("p_generation").GetInt64());
            return Response(Permission(status).ToJsonString());
        });
        var result = await new SupabasePortalClient(http, Key).JobAsync(Identity, Organization, Scan, default);
        Assert.Equal("leased", result!.State); Assert.Equal(status, result.WorkerPermission!.Status);
        Assert.Equal(Clock, result.WorkerPermission.CheckedAt); Assert.Equal(Job, result.WorkerPermission.JobId);
        Assert.Equal(2, calls); Assert.Null(result.WorkerArchive); Assert.Null(result.Digest); Assert.Null(result.Reference);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("multiple")]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation")]
    [InlineData("generationType")]
    [InlineData("unknownStatus")]
    [InlineData("contradiction")]
    [InlineData("clock")]
    [InlineData("missingExpiry")]
    [InlineData("privateField")]
    [InlineData("duplicate")]
    public async Task UnconfirmedProviderPermissionCannotBecomeAuthorityOrLeakPrivateFields(string change)
    {
        var node = Permission(); var row = node[0]!;
        switch (change)
        {
            case "organization": row["organization_id"] = Guid.NewGuid(); break;
            case "scan": row["scan_id"] = Guid.NewGuid(); break;
            case "job": row["job_id"] = Guid.NewGuid(); break;
            case "generation": row["generation"] = 2; break;
            case "generationType": row["generation"] = "1"; break;
            case "unknownStatus": row["status"] = "Running"; break;
            case "contradiction": row["expires_at"] = Clock; break;
            case "clock": row["checked_at"] = "private-upstream-canary"; break;
            case "missingExpiry": row.AsObject().Remove("expires_at"); break;
            case "privateField": row["token"] = "private-upstream-canary"; break;
        }
        var raw = change switch
        {
            "empty" => "[]",
            "multiple" => $"[{row},{row}]",
            "duplicate" => node.ToJsonString().Replace("\"status\":", "\"status\":\"Active\",\"status\":", StringComparison.Ordinal),
            _ => node.ToJsonString()
        };
        var calls = 0;
        using var http = Client((_, _) => Task.FromResult(Response(++calls == 1 ? JobRow() : raw)));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, Key).JobAsync(Identity, Organization, Scan, default));
        Assert.Equal(503, fault.Status);
        Assert.Equal(change == "duplicate" ? "CONTROL_DATABASE_UNAVAILABLE" : "CONTROL_PROVIDER_RESPONSE_UNCONFIRMED", fault.Code);
        Assert.DoesNotContain("private-upstream-canary", fault.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("blocked")]
    public async Task NonLeasedJobDoesNotQueryOrInventWorkerPermission(string state)
    {
        var calls = 0;
        using var http = Client((_, _) => { calls++; return Task.FromResult(Response(JobRow(state))); });
        var result = await new SupabasePortalClient(http, Key).JobAsync(Identity, Organization, Scan, default);
        Assert.Equal(state, result!.State); Assert.Null(result.WorkerPermission); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task PermissionReadDenialCannotFallbackToActiveOrCachedAuthority(int status)
    {
        var calls = 0;
        using var http = Client((_, _) => Task.FromResult(++calls == 1 ? Response(JobRow()) : Response("private-upstream-canary", status)));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, Key).JobAsync(Identity, Organization, Scan, default));
        Assert.Equal(status, fault.Status); Assert.DoesNotContain("private-upstream-canary", fault.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, calls);
    }
}
