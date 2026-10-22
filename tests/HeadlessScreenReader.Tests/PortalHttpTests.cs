using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class PortalHttpTests
{
    [Theory]
    [InlineData("session")]
    [InlineData("workspace")]
    [InlineData("scans")]
    [InlineData("report")]
    public async Task UnauthenticatedHttpCannotReadOrCreateTenantResources(string resource)
    {
        var project = new PortalProjectStub(); var reader = new PortalObservationStub();
        await using var server = await Start(project, reader);
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        var path = resource switch
        {
            "session" => "/api/session",
            "workspace" => "/api/workspace",
            "scans" => Prefix,
            "report" => Prefix + "/" + PortalProjectClientTests.Scan.ToString("D"),
            _ => throw new InvalidOperationException()
        };
        using var response = resource == "workspace" ? await http.PostAsJsonAsync(path, new { }) : await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", json.RootElement.GetProperty("title").GetString());
        Assert.Matches("^[a-f0-9]{32}$", json.RootElement.GetProperty("supportReference").GetString()!);
        Assert.Equal(0, project.AuthCalls); Assert.Equal(0, project.DataCalls); Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task HostAndCrossOriginRequestsAreDeniedBeforeIdentityOrTenantWork()
    {
        var project = new PortalProjectStub();
        await using var server = await Start(project);
        using var http = Authorized(server);
        using var badHost = new HttpRequestMessage(HttpMethod.Get, "/api/session"); badHost.Headers.Host = "unexpected.example";
        using var first = await http.SendAsync(badHost);
        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        using var badOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/session"); badOrigin.Headers.Add("Origin", "https://unexpected.example");
        using var second = await http.SendAsync(badOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.False(second.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(0, project.AuthCalls); Assert.Equal(0, project.DataCalls);
    }

    [Fact]
    public async Task BearerInQueryIsDeniedAndNeverReachesIdentityAdapter()
    {
        var project = new PortalProjectStub();
        await using var server = await Start(project);
        using var http = Authorized(server);
        using var response = await http.GetAsync("/api/session?access_token=private-query-canary");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("private-query-canary", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, project.AuthCalls);
    }

    [Fact]
    public async Task WorkspaceCreateReturnsRetrievableLocationAndDuplicateReturnsSameResource()
    {
        var project = new PortalProjectStub();
        await using var server = await Start(project);
        using var http = Authorized(server);
        using var first = await http.PostAsJsonAsync("/api/workspace", new { });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("/api/workspaces/" + PortalProjectClientTests.Organization.ToString("D"), first.Headers.Location!.ToString());
        using var read = await http.GetAsync(first.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var duplicate = await http.PostAsJsonAsync("/api/workspace", new { });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(3, project.AuthCalls);
        Assert.All(project.SeenBearers, value => Assert.Equal(PortalProjectClientTests.Bearer, value));
    }

    [Theory]
    [InlineData("caller_identity")]
    [InlineData("arbitrary_target")]
    [InlineData("missing_request")]
    [InlineData("empty_request")]
    [InlineData("duplicate_property")]
    public async Task InvalidAdmissionBodyHasNoDataSideEffect(string scenario)
    {
        var request = Guid.NewGuid().ToString("D");
        var body = scenario switch
        {
            "caller_identity" => "{\"requestId\":\"" + request + "\",\"userId\":\"private-actor-canary\"}",
            "arbitrary_target" => "{\"requestId\":\"" + request + "\",\"url\":\"https://example.invalid\"}",
            "missing_request" => "{}",
            "empty_request" => "{\"requestId\":\"" + Guid.Empty + "\"}",
            "duplicate_property" => "{\"requestId\":\"" + request + "\",\"requestId\":\"" + request + "\"}",
            _ => throw new InvalidOperationException()
        };
        var project = new PortalProjectStub();
        await using var server = await Start(project);
        using var http = Authorized(server);
        using var response = await http.PostAsync(Prefix, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, project.CreateCalls); Assert.Equal(0, project.DataCalls);
        Assert.DoesNotContain("private-actor-canary", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OversizedAndWrongMediaBodiesRemainExplicitAndCannotAdmit()
    {
        var project = new PortalProjectStub();
        await using var server = await Start(project);
        using var http = Authorized(server);
        using var oversized = await http.PostAsync(Prefix, new StringContent(new string(' ', 5000) + "{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var wrongMedia = await http.PostAsync(Prefix, new StringContent("{}", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongMedia.StatusCode);
        Assert.Equal(0, project.CreateCalls); Assert.Equal(0, project.DataCalls);
    }

    [Fact]
    public async Task AdmittedFixtureReturnsReportLocationAndPendingEvidenceDoesNotVerifyGoal()
    {
        var project = new PortalProjectStub(); var reader = new PortalObservationStub();
        await using var server = await Start(project, reader);
        using var http = Authorized(server);
        var request = Guid.NewGuid();
        using var response = await http.PostAsJsonAsync(Prefix, new { requestId = request });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Prefix + "/" + PortalProjectClientTests.Scan.ToString("D"), response.Headers.Location!.ToString());
        Assert.Equal(request, project.LastRequest);
        using var report = await http.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using var json = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
        Assert.Equal("Pending", json.RootElement.GetProperty("evidenceState").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("observation").ValueKind);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task UnknownOrPeerWorkspaceReportCannotReadArtifact()
    {
        var project = new PortalProjectStub(); var reader = new PortalObservationStub();
        await using var server = await Start(project, reader);
        using var http = Authorized(server);
        using var response = await http.GetAsync("/api/workspaces/" + Guid.NewGuid().ToString("D") + "/scans/" + PortalProjectClientTests.Scan.ToString("D"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task RecentHistoryIsBoundedAndReportsAdditionalStoredResults()
    {
        var project = new PortalProjectStub { HistoryCount = 51 };
        await using var server = await Start(project);
        using var http = Authorized(server);
        using var response = await http.GetAsync(Prefix);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(50, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(json.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task RevokedIdentityIsRecheckedOnEveryRequestAndHasNoRawProviderDisclosure()
    {
        var project = new PortalProjectStub();
        await using var server = await Start(project);
        using var http = Authorized(server);
        using var first = await http.GetAsync("/api/session"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        project.Revoked = true;
        using var denied = await http.GetAsync(Prefix);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(2, project.AuthCalls);
        Assert.Equal(0, project.DataCalls);
        var body = await denied.Content.ReadAsStringAsync(); Assert.DoesNotContain(PortalProjectClientTests.Bearer, body);
    }

    [Fact]
    public async Task PublicSettingsAndActualListenerReadinessMakeNoTenantAcceptanceClaim()
    {
        var project = new PortalProjectStub();
        await using var server = await Start(project);
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        using var response = await http.GetAsync("/internal/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Listening", json.RootElement.GetProperty("state").GetString());
        Assert.False(json.RootElement.GetProperty("appJwtValidated").GetBoolean());
        Assert.False(json.RootElement.GetProperty("publicMcpEnabled").GetBoolean());
        Assert.Equal(new string('a', 40), json.RootElement.GetProperty("releaseCommit").GetString());
        Assert.Equal(0, project.AuthCalls); Assert.Equal(0, project.DataCalls);
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(server.BaseAddress.Host)));
        using var schema = await http.GetAsync("/openapi/v1.json");
        using var spec = JsonDocument.Parse(await schema.Content.ReadAsStringAsync());
        Assert.Equal("3.1.0", spec.RootElement.GetProperty("openapi").GetString());
        Assert.True(spec.RootElement.GetProperty("paths").TryGetProperty("/api/workspaces/{organization}/scans/{scan}", out _));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    internal static string Prefix => "/api/workspaces/" + PortalProjectClientTests.Organization.ToString("D") + "/scans";
    internal static Task<HostedControlServer> Start(PortalProjectStub project, PortalObservationStub? reader = null) =>
        HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/portal-validation/evidence"), new string('a', 40), 0), project, reader ?? new());
    internal static HttpClient Authorized(HostedControlServer server)
    {
        var http = new HttpClient { BaseAddress = server.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PortalProjectClientTests.Bearer);
        return http;
    }
}

internal sealed class PortalProjectStub : IPortalProjectClient
{
    public int AuthCalls { get; private set; }
    public int DataCalls { get; private set; }
    public int CreateCalls { get; private set; }
    public Guid LastRequest { get; private set; }
    public bool Revoked { get; set; }
    public int HistoryCount { get; set; } = 1;
    public List<string> SeenBearers { get; } = [];
    public PortalScanResponse Record { get; set; } = new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan,
        "synthetic-shop", "hsr-desktop-reader-v13", "queued", DateTimeOffset.Parse("2026-10-21T00:00:00Z"));
    public PortalJobResult Job { get; set; } = new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan,
        PortalProjectClientTests.Job, "queued", 0, null, null, null);
    public Func<CancellationToken, Task>? BeforeAuth { get; set; }
    public Func<int, PortalScanResponse?>? ScanResult { get; set; }
    public int ScanCalls { get; private set; }
    public bool MissingJob { get; set; }
    public async Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token)
    {
        AuthCalls++;
        if (BeforeAuth is not null) await BeforeAuth(token);
        if (Revoked || bearer != PortalProjectClientTests.Bearer) throw new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED");
        return new(PortalProjectClientTests.Actor, "reader@example.invalid", bearer);
    }
    public Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token)
    { DataCalls++; SeenBearers.Add(actor.Token); return Task.FromResult(new PortalWorkspaceBootstrap(PortalProjectClientTests.Organization, DataCalls == 1)); }
    public Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    { DataCalls++; SeenBearers.Add(actor.Token); return Task.FromResult(organization == PortalProjectClientTests.Organization ? new PortalWorkspaceResponse(organization, "Personal workspace") : null); }
    public Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    { DataCalls++; return Task.FromResult(Enumerable.Range(0, HistoryCount).Select(_ => Record).ToArray()); }
    public Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token)
    { DataCalls++; CreateCalls++; LastRequest = request; SeenBearers.Add(actor.Token); return Task.FromResult(new PortalScanCreatedResponse(organization, Record.Id, Job.JobId)); }
    public Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    { DataCalls++; ScanCalls++; return Task.FromResult(organization == PortalProjectClientTests.Organization && scan == PortalProjectClientTests.Scan ? ScanResult is null ? Record : ScanResult(ScanCalls) : null); }
    public Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    { DataCalls++; return Task.FromResult<PortalJobResult?>(MissingJob ? null : Job); }
}
internal sealed class PortalObservationStub : IPortalObservationReader
{
    public int Calls { get; private set; }
    public PortalObservationResponse? Value { get; set; }
    public Task<PortalObservationResponse?> ReadAsync(PortalJobResult job, CancellationToken token) { Calls++; return Task.FromResult(Value); }
}
