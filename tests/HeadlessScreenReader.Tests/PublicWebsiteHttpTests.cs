using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class PublicWebsiteHttpTests
{
    [Theory]
    [InlineData(false, false, 401)]
    [InlineData(true, true, 400)]
    [InlineData(true, false, 200)]
    public async Task HttpScreeningUsesExistingAuthorizationAndRejectsQueryScope(bool authenticated, bool forgedQuery, int expected)
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        WebsiteScreeningApiTests.Seed(rig, [new("action", "button", ""), new("catalog", "link", "View Course Catalog")]);
        await using var server = await Start(rig); using var client = Client(server, authenticated);
        var path = Collection(rig) + $"/{rig.Job.ScanId:D}/screening/{rig.Digest}" + (forgedQuery ? "?actor=forged" : "");
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        if (expected == 200)
        {
            using var json = JsonDocument.Parse(text); var report = json.RootElement.GetProperty("screening");
            Assert.Equal("PotentialIssue", report.GetProperty("issues")[0].GetProperty("classification").GetString());
            Assert.Equal("DiscoverServices", report.GetProperty("suggestedTasks")[0].GetProperty("purpose").GetString());
            Assert.Equal("NotStarted", report.GetProperty("taskExecution").GetString()); Assert.False(report.GetProperty("legalCaseDetermined").GetBoolean());
        }
        else { Assert.Equal(0, rig.OriginalReads); Assert.DoesNotContain("catalog", text); }
    }

    private static Task<HostedControlServer> Start(PublicWebsiteApiTests.Rig rig) => HostedControlServer.StartAsync(
        new(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/public-website-http"), new string('a', 40), 0),
        rig.Project, new PortalObservationStub(), publicWebsiteRuntimeReady: () => Task.FromResult(rig.ProcessorReady));

    private static HttpClient Client(HostedControlServer server, bool authenticated = true)
    {
        var client = new HttpClient { BaseAddress = server.BaseAddress, Timeout = TimeSpan.FromSeconds(15) };
        if (authenticated) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PortalProjectClientTests.Bearer);
        return client;
    }
    private static string Collection(PublicWebsiteApiTests.Rig rig) => $"/api/workspaces/{rig.Job.OrganizationId:D}/website-scans";

    [Fact]
    public async Task HttpAdmissionReturnsAcceptedLocationAndFreshScopedStatusWithoutAnExecutionClaim()
    {
        using var rig = new PublicWebsiteApiTests.Rig { State = "queued" };
        await using var server = await Start(rig); using var client = Client(server);
        using var response = await client.PostAsync(Collection(rig), new StringContent(JsonSerializer.Serialize(new
        { requestId = rig.RequestId, url = rig.Job.Source.TargetUrl }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(Collection(rig) + "/" + rig.Job.ScanId.ToString("D"), response.Headers.Location!.OriginalString);
        var value = JsonSerializer.Deserialize<PortalPublicWebsiteCreatedResponse>(await response.Content.ReadAsStringAsync(), PortalJson.Options)!;
        Assert.Equal(rig.Job.ScanId, value.ScanId); Assert.Equal(rig.Job.JobId, value.JobId);
        Assert.Equal(rig.Job.Source.TargetUrl, value.TargetUrl); Assert.Equal(1, rig.Creates);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var status = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var saved = JsonSerializer.Deserialize<PortalPublicWebsiteStatusResponse>(await status.Content.ReadAsStringAsync(), PortalJson.Options)!;
        Assert.Equal("queued", saved.State); Assert.Equal(0, saved.Generation);
        Assert.Null(saved.OriginalSha256); Assert.Null(saved.ProducerId); Assert.Null(saved.WorkerPermission);
        Assert.Equal(0, rig.OriginalReads);
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("extra-authority", 400)]
    [InlineData("duplicate-url", 400)]
    [InlineData("missing-url", 400)]
    [InlineData("invalid-target", 400)]
    [InlineData("query-selector", 400)]
    public async Task HttpRejectsUntrustedScopeMalformedBodyAndQueriesBeforePublicData(string scenario, int expected)
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        await using var server = await Start(rig); using var client = Client(server, scenario != "anonymous");
        var body = JsonSerializer.Serialize(new { requestId = rig.RequestId, url = rig.Job.Source.TargetUrl });
        if (scenario == "extra-authority") body = body[..^1] + ",\"organizationId\":\"11111111-1111-1111-1111-111111111111\"}";
        if (scenario == "duplicate-url") body = body[..^1] + ",\"url\":\"https://example.com/\"}";
        if (scenario == "missing-url") body = JsonSerializer.Serialize(new { requestId = rig.RequestId });
        if (scenario == "invalid-target") body = JsonSerializer.Serialize(new { requestId = rig.RequestId, url = "https://127.0.0.1/" });
        using var response = await client.PostAsync(Collection(rig) + (scenario == "query-selector" ? "?actor=forged" : ""),
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, rig.Memberships); Assert.Equal(0, rig.Creates); Assert.Equal(0, rig.StatusReads); Assert.Equal(0, rig.OriginalReads);
        Assert.DoesNotContain(rig.Job.Source.TargetUrl, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpNodePagesAndOriginalEofSurviveReconnectAndCurrentDenialDropsTheirData()
    {
        using var rig = new PublicWebsiteApiTests.Rig(); await using var server = await Start(rig);
        var root = Collection(rig) + "/" + rig.Job.ScanId.ToString("D");
        using (var first = Client(server))
        {
            using var response = await first.GetAsync(root + "/observations/1/0/100");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = JsonSerializer.Deserialize<PortalPublicWebsiteNodePageResponse>(await response.Content.ReadAsStringAsync(), PortalJson.Options)!;
            Assert.Equal("Actual exposed name", Assert.Single(page.Nodes).Name);
            Assert.Equal("NotAssessed", page.GoalVerification); Assert.Equal(0, page.DispatchedInputs);
            Assert.Equal(rig.Digest, page.OriginalSha256);
        }
        using var reconnect = Client(server);
        using (var pinned = await reconnect.GetAsync(root + "/observations/1/1/100/" + rig.Digest))
        {
            Assert.Equal(HttpStatusCode.OK, pinned.StatusCode);
            var page = JsonSerializer.Deserialize<PortalPublicWebsiteNodePageResponse>(await pinned.Content.ReadAsStringAsync(), PortalJson.Options)!;
            Assert.Empty(page.Nodes); Assert.Equal(1, page.Offset); Assert.Null(page.NextOffset);
        }
        using (var original = await reconnect.GetAsync(root + "/original/0/" + rig.Digest))
        {
            Assert.Equal(HttpStatusCode.OK, original.StatusCode);
            var page = JsonSerializer.Deserialize<PortalPublicWebsiteOriginalPageResponse>(await original.Content.ReadAsStringAsync(), PortalJson.Options)!;
            Assert.Equal(Encoding.UTF8.GetBytes(rig.Text), page.Data); Assert.Equal(rig.Digest, page.PageSha256);
        }
        rig.Visible = false;
        using var denied = await reconnect.GetAsync(root + "/original/" + Encoding.UTF8.GetByteCount(rig.Text) + "/" + rig.Digest);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var error = await denied.Content.ReadAsStringAsync();
        Assert.Contains("CONTROL_WORKSPACE_NOT_FOUND", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual exposed name", error, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Digest, error, StringComparison.Ordinal); Assert.Equal(3, rig.OriginalReads);
    }
}
