using System.Net;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PortalSearchHistoryTests
{
    private static readonly PortalIdentity Actor = new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static readonly Guid Organization = PortalProjectClientTests.Organization;

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    public async Task HistoryKeepsBoundedOrderedMetadataWithoutStartingInput(int count, bool continuation)
    {
        var project = new Project { Page = Page(count, continuation) };
        using var cancellation = new CancellationTokenSource();
        var page = await new PortalSearchTasks(project, null, null).HistoryAsync(Actor, Organization,
            "search-working", null, cancellation.Token);
        Assert.Equal(project.Page, page); Assert.Equal(count, page.Items.Length);
        Assert.False(page.WebsiteInputStarted); Assert.False(page.IsAssessment);
        Assert.Equal(1, project.Reads); Assert.Equal((Actor, Organization, "search-working", (Guid?)null, cancellation.Token), project.Last);
        Assert.Equal(0, project.InputCalls);
    }

    [Theory]
    [InlineData("unknown-source")]
    [InlineData("empty-organization")]
    [InlineData("empty-cursor")]
    public async Task InvalidHistorySelectorsAreDeniedBeforeAnyProjectCall(string change)
    {
        var project = new Project();
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new PortalSearchTasks(project, null, null).HistoryAsync(Actor,
            change == "empty-organization" ? Guid.Empty : Organization,
            change == "unknown-source" ? "https://customer.invalid/" : "search-working",
            change == "empty-cursor" ? Guid.Empty : null, default));
        Assert.Equal(400, failure.Status); Assert.Equal(0, project.Reads); Assert.Equal(0, project.InputCalls);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("page-source")]
    [InlineData("row-source")]
    [InlineData("creator")]
    [InlineData("duplicate-job")]
    [InlineData("duplicate-request")]
    [InlineData("date-order")]
    [InlineData("tie-order")]
    [InlineData("overfull")]
    [InlineData("short-continuation")]
    [InlineData("wrong-continuation")]
    [InlineData("cursor-repeated")]
    [InlineData("null-item")]
    [InlineData("null-items")]
    public async Task HistoryRejectsForeignAmbiguousAndUnboundedMetadata(string change)
    {
        var page = Page(change == "overfull" ? 11 : 10, true);
        Guid? before = null;
        switch (change)
        {
            case "organization": page = page with { OrganizationId = Guid.NewGuid() }; break;
            case "page-source": page = page with { SourceId = "search-keyboard-blocked" }; break;
            case "row-source": page.Items[0] = page.Items[0] with { Job = page.Items[0].Job with { Source = SyntheticSearchTaskSource.FromId("search-keyboard-blocked") } }; break;
            case "creator": page.Items[0] = page.Items[0] with { CreatedBy = Guid.NewGuid() }; break;
            case "duplicate-job": page.Items[1] = page.Items[1] with { Job = page.Items[1].Job with { JobId = page.Items[0].Job.JobId } }; break;
            case "duplicate-request": page.Items[1] = page.Items[1] with { RequestId = page.Items[0].RequestId }; break;
            case "date-order": Array.Reverse(page.Items); page = page with { NextBeforeJobId = page.Items[^1].Job.JobId }; break;
            case "tie-order":
                page.Items[0] = page.Items[0] with { CreatedAt = page.Items[1].CreatedAt, UpdatedAt = page.Items[1].UpdatedAt };
                (page.Items[0], page.Items[1]) = (page.Items[1], page.Items[0]); break;
            case "short-continuation": page = page with { Items = page.Items[..9] }; break;
            case "wrong-continuation": page = page with { NextBeforeJobId = Guid.NewGuid() }; break;
            case "cursor-repeated": before = page.Items[0].Job.JobId; break;
            case "null-item": page.Items[0] = null!; break;
            case "null-items": page = page with { Items = null! }; break;
        }
        var project = new Project { Page = page };
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new PortalSearchTasks(project, null, null)
            .HistoryAsync(Actor, Organization, "search-working", before, default));
        Assert.Equal(503, failure.Status); Assert.Equal(1, project.Reads); Assert.Equal(0, project.InputCalls);
    }

    [Fact]
    public async Task HistoryDenialIsNotAnEmptyListOrAReplacementTest()
    {
        var project = new Project { Denied = true };
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new PortalSearchTasks(project, null, null)
            .HistoryAsync(Actor, Organization, "search-working", null, default));
        Assert.Equal(403, failure.Status); Assert.Equal("CONTROL_WORKSPACE_ACCESS_DENIED", failure.Code);
        Assert.Equal(1, project.Reads); Assert.Equal(0, project.InputCalls);
    }

    [Fact]
    public async Task OlderProjectAdaptersRemainClosedWithoutImplementingHistoryOrStartingWork()
    {
        var project = new PortalSearchTasksTests.SearchProject();
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new PortalSearchTasks(project, null, null)
            .HistoryAsync(Actor, Organization, "search-working", null, default));
        Assert.Equal(503, failure.Status); Assert.Equal("CONTROL_SEARCH_HISTORY_UNAVAILABLE", failure.Code);
        Assert.Equal(0, project.Reads); Assert.Equal(0, project.Creates); Assert.Equal(0, project.Cancels);
    }

    [Fact]
    public async Task BothHistoryRoutesDenyUnauthenticatedRequestsBeforeReadingMetadata()
    {
        var project = new Project();
        await using var server = await HostedControlServer.StartWithSearchAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath("artifacts/portal-validation/evidence"), new('a', 40), 0), new PortalProjectStub(),
            new PortalObservationStub(), new PortalSearchTasks(project, null, null));
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        var root = $"/api/workspaces/{Organization:D}/search-demonstrations/history/search-working";
        using var first = await http.GetAsync(root); Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        using var next = await http.GetAsync(root + "/a2000000-0000-4000-8000-000000000001"); Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        Assert.Equal(0, project.Reads); Assert.Equal(0, project.InputCalls);
    }

    [Fact]
    public void HistoryOpenApiDeclaresClosedSourceProtectedPathsAndMetadataOnlyClaims()
    {
        using var spec = JsonDocument.Parse(PortalOpenApi.Document);
        var root = "/api/workspaces/{organization}/search-demonstrations/history/{sourceId}";
        foreach (var path in new[] { root, root + "/{beforeJob}" })
        {
            var operation = spec.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get");
            Assert.Contains(operation.GetProperty("security").EnumerateArray(), value => value.TryGetProperty("bearerAuth", out _));
            var selectors = operation.GetProperty("parameters").EnumerateArray().ToArray();
            Assert.All(selectors, item => { Assert.Equal("path", item.GetProperty("in").GetString()); Assert.True(item.GetProperty("required").GetBoolean()); });
            Assert.Equal(new[] { "search-working", "search-keyboard-blocked" }, selectors.Single(item => item.GetProperty("name").GetString() == "sourceId")
                .GetProperty("schema").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(path == root ? 2 : 3, selectors.Length);
        }
        var fields = spec.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SearchTaskHistory").GetProperty("properties");
        Assert.Equal(10, fields.GetProperty("items").GetProperty("maxItems").GetInt32());
        Assert.False(fields.GetProperty("websiteInputStarted").GetProperty("const").GetBoolean());
        Assert.False(fields.GetProperty("isAssessment").GetProperty("const").GetBoolean());
    }

    [Fact]
    public async Task HttpHistoryUsesTheOriginalCallerAndDeniesQueriesAndLaterRevocation()
    {
        var project = new Project { Page = Page(1, false) };
        var diagnostics = new ConcurrentQueue<PortalRequestDiagnostic>();
        await using var server = await HostedControlServer.StartWithSearchAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath("artifacts/portal-validation/evidence"), new('a', 40), 0), new PortalProjectStub(),
            new PortalObservationStub(), new PortalSearchTasks(project, null, null), diagnostic: diagnostics.Enqueue);
        using var http = PortalHttpTests.Authorized(server);
        var path = $"/api/workspaces/{Organization:D}/search-demonstrations/history/search-working";
        using var first = await http.GetAsync(path); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = JsonSerializer.Deserialize<PortalSearchHistory>(await first.Content.ReadAsStringAsync(), PortalJson.Options)!;
        Assert.Equal(project.Page.Items[0].Job, Assert.Single(page.Items).Job); Assert.False(page.WebsiteInputStarted);
        Assert.Equal(Actor.UserId, project.Last.Item1.UserId); Assert.Equal(Actor.Token, project.Last.Item1.Token);
        Assert.Equal("ReadSearchTaskHistory", Assert.Single(diagnostics).Operation);
        using var query = await http.GetAsync(path + "?sourceId=search-keyboard-blocked"); Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(1, project.Reads);
        var cursor = Guid.NewGuid();
        using var continued = await http.GetAsync(path + $"/{cursor:D}"); Assert.Equal(HttpStatusCode.OK, continued.StatusCode);
        var logged = diagnostics.Single(item => item.Operation == "ReadSearchTaskHistoryBefore");
        Assert.Equal(cursor, logged.JobId); Assert.Equal(Organization, logged.OrganizationId);
        Assert.Equal(continued.Headers.GetValues("X-HSR-Request-Id").Single(), logged.SupportReference);
        Assert.DoesNotContain(Actor.Token, JsonSerializer.Serialize(logged));
        Assert.DoesNotContain(project.Page.Items[0].Job.JobId.ToString(), JsonSerializer.Serialize(logged));
        project.Denied = true;
        using var denied = await http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain(project.Page.Items[0].Job.JobId.ToString(), await denied.Content.ReadAsStringAsync());
        Assert.Equal(0, project.InputCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryRpcPinsTheOriginalActorAndSerializesItsTypedWorkerShape(bool continued)
    {
        var expected = Page(1, false); Guid? before = continued ? Guid.NewGuid() : null;
        using var http = PortalProjectClientTests.Client(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/rest/v1/rpc/read_search_task_history", request.RequestUri!.AbsolutePath);
            Assert.Equal("hsr", request.Headers.GetValues("Content-Profile").Single());
            Assert.Equal(Actor.Token, request.Headers.Authorization!.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(Organization, json.RootElement.GetProperty("p_organization_id").GetGuid());
            Assert.Equal("search-working", json.RootElement.GetProperty("p_source_id").GetString());
            Assert.Equal(before, json.RootElement.GetProperty("p_before_job_id").ValueKind == JsonValueKind.Null ? null : json.RootElement.GetProperty("p_before_job_id").GetGuid());
            Assert.Equal(3, json.RootElement.EnumerateObject().Count());
            return PortalProjectClientTests.Response(JsonSerializer.Serialize(expected, ReaderWorkerWire.Json));
        });
        var actual = await new SupabasePortalClient(http, PortalProjectClientTests.Key).ReadSearchHistoryAsync(Actor, Organization, "search-working", before, default);
        Assert.Equal(expected.Items[0], Assert.Single(actual.Items)); Assert.False(actual.WebsiteInputStarted); Assert.False(actual.IsAssessment);
    }

    [Theory]
    [InlineData("WebsiteInputStarted")]
    [InlineData("IsAssessment")]
    [InlineData("unknown")]
    public async Task HistoryRpcCannotAcceptPromotedClaimsOrUnexpectedFields(string field)
    {
        var node = JsonSerializer.SerializeToNode(Page(1, false), ReaderWorkerWire.Json)!; node[field] = true;
        using var http = PortalProjectClientTests.Client((_, _) => Task.FromResult(PortalProjectClientTests.Response(node.ToJsonString())));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, PortalProjectClientTests.Key)
            .ReadSearchHistoryAsync(Actor, Organization, "search-working", null, default));
        Assert.Equal(503, fault.Status); Assert.Equal("CONTROL_SEARCH_TASK_RESPONSE_INVALID", fault.Code);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("assessment")]
    [InlineData("organization")]
    [InlineData("source")]
    [InlineData("row-source")]
    [InlineData("duplicate")]
    [InlineData("duplicate-request")]
    [InlineData("creator")]
    [InlineData("row-organization")]
    [InlineData("ordering")]
    [InlineData("continuation")]
    public async Task PrivateHistoryRejectsPromotedAndMixedPagesWithoutLeakingTheOriginal(string change)
    {
        var page = Page(2, false);
        var node = JsonSerializer.SerializeToNode(page, PortalJson.Options)!;
        switch (change)
        {
            case "input": node["websiteInputStarted"] = true; break;
            case "assessment": node["isAssessment"] = true; break;
            case "organization": node["organizationId"] = Guid.NewGuid(); break;
            case "source": node["sourceId"] = "search-keyboard-blocked"; break;
            case "row-source": node["items"]![0]!["job"]!["source"] = JsonSerializer.SerializeToNode(SyntheticSearchTaskSource.FromId("search-keyboard-blocked"), PortalJson.Options); break;
            case "duplicate": node["items"]![1] = node["items"]![0]!.DeepClone(); break;
            case "duplicate-request": node["items"]![1]!["requestId"] = node["items"]![0]!["requestId"]!.DeepClone(); break;
            case "creator": node["items"]![1]!["createdBy"] = Guid.NewGuid(); break;
            case "row-organization": node["items"]![1]!["job"]!["organizationId"] = Guid.NewGuid(); break;
            case "ordering": node["items"] = new JsonArray(node["items"]![1]!.DeepClone(), node["items"]![0]!.DeepClone()); break;
            case "continuation": node["nextBeforeJobId"] = Guid.NewGuid(); break;
        }
        var transport = new Transport(node.ToJsonString());
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), Organization, Actor.Token), transport);
        var reply = await client.SearchHistoryAsync("search-working");
        Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode); Assert.Equal(1, transport.Calls);
        Assert.DoesNotContain(page.Items[0].Job.JobId.ToString(), JsonSerializer.Serialize(reply.Data));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    public async Task PrivateHistoryKeepsEmptyAndFullPagesAndPinsTheContinuationPath(int count, bool continued)
    {
        var page = Page(count, count == 10); var before = continued ? Guid.NewGuid().ToString("D") : null;
        var path = $"/api/workspaces/{Organization:D}/search-demonstrations/history/search-working" + (before is null ? "" : "/" + before);
        var transport = new Transport(JsonSerializer.Serialize(page, PortalJson.Options), path);
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), Organization, Actor.Token), transport);
        var reply = await client.SearchHistoryAsync("search-working", before);
        Assert.Null(reply.ErrorCode); Assert.Equal(1, transport.Calls);
        var raw = JsonSerializer.SerializeToElement(reply.Data, PortalJson.Options);
        Assert.Equal(count, raw.GetProperty("items").GetArrayLength());
        Assert.Equal(page.NextBeforeJobId, raw.GetProperty("nextBeforeJobId").ValueKind == JsonValueKind.Null ? null : raw.GetProperty("nextBeforeJobId").GetGuid());
        Assert.False(raw.GetProperty("websiteInputStarted").GetBoolean()); Assert.False(raw.GetProperty("isAssessment").GetBoolean());
    }

    [Theory]
    [InlineData("https://customer.invalid/", null)]
    [InlineData("search-working", "00000000-0000-0000-0000-000000000000")]
    [InlineData("search-working", "not-a-job")]
    [InlineData("search-working", "")]
    public async Task PrivateHistoryInvalidSelectorsNeverReachTransport(string source, string? before)
    {
        var transport = new Transport("unexpected");
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), Organization, Actor.Token), transport);
        var reply = await client.SearchHistoryAsync(source, before);
        Assert.Equal("SEARCH_TASK_REQUEST_INVALID", reply.ErrorCode); Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task RealStdioHistoryReopensMetadataAndDeniesLaterAuthorityWithoutAnyNewTest()
    {
        var project = new Project { Page = Page(1, false) };
        await using var server = await HostedControlServer.StartWithSearchAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath("artifacts/portal-validation/evidence"), new('a', 40), 0), new PortalProjectStub(),
            new PortalObservationStub(), new PortalSearchTasks(project, null, null));
        await using var mcp = await PortalSearchTasksTests.Connect(server.BaseAddress, Organization);
        var args = new Dictionary<string, object?> { ["sourceId"] = "search-working" };
        var result = (await PortalSearchTasksTests.Call(mcp, "search_demonstration_history", args)).GetProperty("data");
        Assert.Equal(project.Page.Items[0].Job.JobId, result.GetProperty("items")[0].GetProperty("job").GetProperty("jobId").GetGuid());
        Assert.False(result.GetProperty("websiteInputStarted").GetBoolean()); Assert.False(result.GetProperty("isAssessment").GetBoolean());
        project.Denied = true;
        var denied = await PortalSearchTasksTests.Call(mcp, "search_demonstration_history", args, error: true);
        Assert.DoesNotContain(project.Page.Items[0].Job.JobId.ToString(), denied.GetRawText());
        Assert.Equal(2, project.Reads); Assert.Equal(0, project.InputCalls);
    }

    private sealed class Transport(string body, string? path = null) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(path ?? $"/api/workspaces/{Organization:D}/search-demonstrations/history/search-working", request.RequestUri!.AbsolutePath);
            var response = PortalProjectClientTests.Response(body); response.Headers.CacheControl = new() { NoStore = true };
            return Task.FromResult(response);
        }
    }

    private static PortalSearchHistory Page(int count, bool continuation)
    {
        var items = Enumerable.Range(0, count).Select(index =>
        {
            var value = PortalSearchTasksTests.Queued(); var time = value.CreatedAt.AddSeconds(-index);
            return value with { Job = value.Job with { JobId = Guid.Parse($"a2000000-0000-4000-8000-{index + 1:000000000000}") }, CreatedAt = time, UpdatedAt = time };
        }).ToArray();
        return new(Organization, "search-working", items, continuation ? items[^1].Job.JobId : null);
    }

    private sealed class Project : IPortalSearchTaskProject
    {
        internal PortalSearchHistory Page { get; set; } = PortalSearchHistoryTests.Page(0, false);
        internal int Reads { get; private set; }
        internal int InputCalls { get; private set; }
        internal bool Denied { get; set; }
        internal (PortalIdentity, Guid, string, Guid?, CancellationToken) Last { get; private set; }
        public Task<PortalSearchHistory> ReadSearchHistoryAsync(PortalIdentity actor, Guid organization, string sourceId, Guid? beforeJob, CancellationToken token)
        {
            Reads++; Last = (actor, organization, sourceId, beforeJob, token);
            if (Denied) throw new PortalFault(403, "CONTROL_WORKSPACE_ACCESS_DENIED");
            return Task.FromResult(Page);
        }
        public Task<SyntheticSearchTaskStatus?> ReadSearchTaskAsync(PortalIdentity actor, Guid organization, Guid? job, Guid? request, CancellationToken token) => throw new InvalidOperationException();
        public Task<SyntheticSearchTaskStatus> CreateSearchTaskAsync(PortalIdentity actor, Guid organization, PortalCreateSearchTaskRequest request, CancellationToken token)
        { InputCalls++; throw new InvalidOperationException(); }
        public Task<SyntheticSearchTaskStatus?> CancelSearchTaskAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token)
        { InputCalls++; throw new InvalidOperationException(); }
        public Task<PortalSearchDiagnostics?> ReadSearchDiagnosticsAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token) => throw new InvalidOperationException();
    }
}
