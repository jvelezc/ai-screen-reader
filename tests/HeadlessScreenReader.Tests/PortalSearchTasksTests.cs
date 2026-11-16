using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

public sealed class PortalSearchTasksTests
{
    private static readonly PortalIdentity Actor = new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    internal static SyntheticSearchTaskStatus Queued() => new(SyntheticSearchTaskStatus.CurrentSchema,
        new(PortalProjectClientTests.Organization, Guid.NewGuid(), Guid.NewGuid(), 1, SyntheticSearchTaskJob.CurrentProfile,
            SyntheticSearchTaskSource.FromId("search-working")), Guid.NewGuid(), Actor.UserId,
        DateTimeOffset.Parse("2026-11-16T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-11-16T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), "queued", "Queued", null, null, null, null);

    [Fact]
    public async Task SearchAdmissionReconcilesTheSameIntentBeforeCheckingRuntimeAndNeverCreatesTwice()
    {
        var client = new SearchProject(); var checks = 0;
        var service = new PortalSearchTasks(client, null, () => { checks++; return Task.FromResult(true); });
        var request = new PortalCreateSearchTaskRequest(client.Value.RequestId, client.Value.Job.Source.Id);
        var first = await service.StartAsync(Actor, client.Value.Job.OrganizationId, request, default);
        Assert.Equal(client.Value, first); Assert.Equal(1, client.Creates); Assert.Equal(1, checks);
        var second = await service.StartAsync(Actor, client.Value.Job.OrganizationId, request, default);
        Assert.Equal(first, second); Assert.Equal(1, client.Creates); Assert.Equal(1, checks);
        Assert.All(client.Actors, value => Assert.Equal(Actor, value));
    }

    [Theory]
    [InlineData("unknown-source")]
    [InlineData("empty-request")]
    [InlineData("empty-workspace")]
    public async Task InvalidSearchAdmissionCannotReadOrCreateAJob(string change)
    {
        var client = new SearchProject(); var service = new PortalSearchTasks(client, null, () => Task.FromResult(true));
        var request = new PortalCreateSearchTaskRequest(change == "empty-request" ? Guid.Empty : client.Value.RequestId,
            change == "unknown-source" ? "https://customer.example/" : "search-working");
        var failure = await Assert.ThrowsAsync<PortalFault>(() => service.StartAsync(Actor,
            change == "empty-workspace" ? Guid.Empty : client.Value.Job.OrganizationId, request, default));
        Assert.Equal(400, failure.Status); Assert.Equal(0, client.Reads); Assert.Equal(0, client.Creates);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("creator")]
    [InlineData("request")]
    public async Task ReconciliationRejectsConflictingSearchIntentWithoutNewInput(string change)
    {
        var client = new SearchProject { Exists = true }; var expected = client.Value;
        client.Value = change switch
        {
            "source" => expected with { Job = expected.Job with { Source = SyntheticSearchTaskSource.FromId("search-keyboard-blocked") } },
            "creator" => expected with { CreatedBy = Guid.NewGuid() },
            _ => expected with { RequestId = Guid.NewGuid() }
        };
        var service = new PortalSearchTasks(client, null, () => throw new InvalidOperationException());
        var failure = await Assert.ThrowsAsync<PortalFault>(() => service.StartAsync(Actor, expected.Job.OrganizationId,
            new(expected.RequestId, expected.Job.Source.Id), default));
        Assert.Equal(409, failure.Status); Assert.Equal(0, client.Creates);
    }

    [Fact]
    public async Task UnavailableSearchRuntimeDoesNotAdmitAndMissingStatusStaysNull()
    {
        var client = new SearchProject(); var service = new PortalSearchTasks(client, null, () => Task.FromResult(false));
        Assert.Null(await service.StatusAsync(Actor, client.Value.Job.OrganizationId, client.Value.Job.JobId, default));
        var failure = await Assert.ThrowsAsync<PortalFault>(() => service.StartAsync(Actor, client.Value.Job.OrganizationId,
            new(client.Value.RequestId, "search-working"), default));
        Assert.Equal("CONTROL_SEARCH_TASK_RUNTIME_UNAVAILABLE", failure.Code); Assert.Equal(0, client.Creates);
    }

    [Fact]
    public async Task SearchHttpAdmissionLocationStatusCancelAndAuthenticationStayIndependentOfWebsiteReviews()
    {
        var client = new SearchProject(); var identity = new PortalProjectStub();
        await using var server = await Server(identity, new(client, null, () => Task.FromResult(true)));
        var root = $"/api/workspaces/{client.Value.Job.OrganizationId:D}/search-demonstrations";
        using var anonymous = new HttpClient { BaseAddress = server.BaseAddress };
        using var denied = await anonymous.GetAsync(root); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Equal(0, client.Reads);
        using var http = PortalHttpTests.Authorized(server);
        using var admitted = await http.PostAsJsonAsync(root, new { requestId = client.Value.RequestId, sourceId = "search-working" });
        Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode);
        Assert.Equal(root + $"/{client.Value.Job.JobId:D}", admitted.Headers.Location!.OriginalString);
        using var status = await http.GetAsync(admitted.Headers.Location); Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("no-store", status.Headers.CacheControl!.ToString());
        using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.Equal(client.Value.Job.JobId, json.RootElement.GetProperty("job").GetProperty("jobId").GetGuid());
        Assert.DoesNotContain("parentScanId", json.RootElement.GetRawText());
        using var invalid = await http.PostAsJsonAsync(root, new { requestId = Guid.NewGuid(), sourceId = "search-working", targetUrl = "https://customer.example/" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.Equal(1, client.Creates);
        using var cancelled = await http.PostAsync(admitted.Headers.Location + "/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode); Assert.Equal("cancelled", client.Value.State); Assert.Equal(1, client.Cancels);
        identity.Revoked = true; var reads = client.Reads;
        using var revoked = await http.GetAsync(root); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode); Assert.Equal(reads, client.Reads);
    }

    [Fact]
    public async Task SearchMetadataFromAnotherWorkspaceIsRejectedBeforeOriginalReads()
    {
        var client = new SearchProject { Exists = true }; var service = new PortalSearchTasks(client, null, null);
        var failure = await Assert.ThrowsAsync<PortalFault>(() => service.StatusAsync(Actor, Guid.NewGuid(), client.Value.Job.JobId, default));
        Assert.Equal("CONTROL_SEARCH_TASK_RESPONSE_INVALID", failure.Code);
    }

    [Fact]
    public async Task PrivateMcpSearchToolsReconcileTheSameIntentAndCancelWithoutHiddenRetries()
    {
        var client = new SearchProject(); client.Diagnostics = new("hsr-search-task-diagnostics-v1", client.Value.Job.JobId, []);
        await using var server = await Server(new(), new(client, null, () => Task.FromResult(true)));
        await using var mcp = await Connect(server.BaseAddress, client.Value.Job.OrganizationId);
        var tools = await mcp.ListToolsAsync(); Assert.Equal(7, tools.Count(tool => tool.Name.StartsWith("search_demonstration_", StringComparison.Ordinal)));
        var latest = await Call(mcp, "search_demonstration_status", []); Assert.Equal(JsonValueKind.Null, latest.GetProperty("data").ValueKind);
        var args = new Dictionary<string, object?> { ["requestId"] = client.Value.RequestId, ["sourceId"] = "search-working" };
        var admitted = await Call(mcp, "search_demonstration_start", args);
        Assert.Equal(client.Value.Job.JobId, admitted.GetProperty("data").GetProperty("job").GetProperty("jobId").GetGuid());
        await Call(mcp, "search_demonstration_start", args); Assert.Equal(1, client.Creates);
        args = new() { ["jobId"] = client.Value.Job.JobId };
        await Call(mcp, "search_demonstration_status", args);
        var diagnostics = await Call(mcp, "search_demonstration_diagnostics", args); Assert.Empty(diagnostics.GetProperty("data").GetProperty("publicationAttempts").EnumerateArray());
        await Call(mcp, "search_demonstration_result", args, error: true);
        var cancelled = await Call(mcp, "search_demonstration_cancel", args); Assert.Equal("cancelled", cancelled.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal(1, client.Cancels); Assert.Equal(1, client.Creates);
    }

    [Theory]
    [InlineData("legal")]
    [InlineData("scope")]
    [InlineData("schema")]
    public async Task SupabaseSearchRpcRejectsChangedScopeClaimsAndInvalidTypedSchemas(string change)
    {
        var status = Completed(Queued()); var raw = JsonSerializer.SerializeToNode(status, ReaderWorkerWire.Json)!;
        raw["Publication"]![change == "legal" ? "LegalCaseDetermined" : change == "scope" ? "Scope" : "Schema"] =
            change == "legal" ? JsonValue.Create(true) : JsonValue.Create("invented-provider-claim");
        using var http = PortalProjectClientTests.Client((request, _) =>
        {
            Assert.Equal("/rest/v1/rpc/read_search_task", request.RequestUri!.AbsolutePath);
            Assert.Equal("hsr", request.Headers.GetValues("Content-Profile").Single());
            Assert.Equal(Actor.Token, request.Headers.Authorization!.Parameter);
            return Task.FromResult(PortalProjectClientTests.Response(raw.ToJsonString()));
        });
        var rpc = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        var failure = await Assert.ThrowsAsync<PortalFault>(() => rpc.ReadSearchTaskAsync(Actor, status.Job.OrganizationId, status.Job.JobId, null, default));
        Assert.Equal("CONTROL_SEARCH_TASK_RESPONSE_INVALID", failure.Code); Assert.DoesNotContain("invented-provider-claim", failure.Message);
    }

    [Fact]
    public void SearchOpenApiDocumentsAllEightAuthenticatedOperationsAndTheirByteAndScopeBounds()
    {
        using var document = JsonDocument.Parse(PortalOpenApi.Document); var spec = document.RootElement;
        const string prefix = "/api/workspaces/{organization}/search-demonstrations";
        var paths = spec.GetProperty("paths"); var count = 0;
        foreach (var route in paths.EnumerateObject().Where(route => route.Name.StartsWith(prefix, StringComparison.Ordinal)))
            foreach (var operation in route.Value.EnumerateObject())
            {
                count++; Assert.True(operation.Value.GetProperty("security")[0].TryGetProperty("bearerAuth", out _));
                Assert.All(operation.Value.GetProperty("parameters").EnumerateArray(), parameter => Assert.Equal("path", parameter.GetProperty("in").GetString()));
            }
        Assert.Equal(8, count);
        var admission = paths.GetProperty(prefix).GetProperty("post");
        Assert.True(admission.GetProperty("responses").GetProperty("202").GetProperty("headers").TryGetProperty("Location", out _));
        Assert.True(admission.GetProperty("responses").TryGetProperty("429", out _));
        var schemas = spec.GetProperty("components").GetProperty("schemas");
        var source = schemas.GetProperty("CreateSearchTask").GetProperty("properties").GetProperty("sourceId");
        Assert.Equal(new[] { "search-working", "search-keyboard-blocked" }, source.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        var original = schemas.GetProperty("SearchTaskOriginal").GetProperty("properties");
        Assert.Equal(87384, original.GetProperty("data").GetProperty("maxLength").GetInt32());
        var result = schemas.GetProperty("SearchTaskResult").GetProperty("properties");
        Assert.Equal("NotAssessed", result.GetProperty("websiteEvidenceAssessment").GetProperty("const").GetString());
        Assert.False(result.GetProperty("legalCaseDetermined").GetProperty("const").GetBoolean());
        Assert.Equal(4, result.GetProperty("keyboardTrials").GetProperty("maxItems").GetInt32());
        Assert.DoesNotContain("lease_token", schemas.GetProperty("SearchTaskStatus").GetRawText());
        var originalRoute = paths.GetProperty(prefix + "/{job}/originals/{fileId}/{offset}/{digest}").GetProperty("get");
        var fileParameter = originalRoute.GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "fileId");
        Assert.Equal(1400, fileParameter.GetProperty("schema").GetProperty("maximum").GetInt32());
    }

    [Theory]
    [InlineData(1399, true)]
    [InlineData(1400, true)]
    [InlineData(1401, false)]
    public async Task PrivateMcpOriginalSupportsTheManifestInclusiveFinalIdAndRejectsTheNextId(int fileId, bool allowed)
    {
        var status = Completed(Queued()); status = status with { Publication = status.Publication! with { Files = 1400 } };
        var digest = status.Publication!.IndexSha256;
        var file = new PortalSearchFile(fileId, $"file-{fileId}.webm", 4, new('a', 64), "video/webm");
        var transport = new SearchBoundTransport(JsonSerializer.Serialize(new PortalSearchOriginalPage(status, digest, file, 4, [], TraversalArchive.Hash([]), null), PortalJson.Options));
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), status.Job.OrganizationId, Actor.Token), transport);
        var reply = await client.SearchTaskAsync("original", status.Job.JobId.ToString("D"), digest: digest, fileId: fileId, offset: 4);
        if (allowed)
        {
            Assert.Null(reply.ErrorCode); Assert.Equal(1, transport.Calls);
            var page = JsonSerializer.SerializeToElement(reply.Data, PortalJson.Options);
            Assert.Equal(fileId, page.GetProperty("file").GetProperty("id").GetInt32()); Assert.Empty(page.GetProperty("data").GetBytesFromBase64());
        }
        else { Assert.Equal("SEARCH_TASK_REQUEST_INVALID", reply.ErrorCode); Assert.Equal(0, transport.Calls); }
    }

    [Fact]
    public async Task PrivateMcpFinalInventoryPageIncludesEveryOriginalAndItsManifest()
    {
        var status = Completed(Queued()); status = status with { Publication = status.Publication! with { Files = 1400 } };
        var digest = status.Publication!.IndexSha256;
        var files = Enumerable.Range(1392, 9).Select(id => new PortalSearchFile(id, $"file-{id}.webm", 4, new('a', 64), "video/webm")).ToArray();
        var transport = new SearchBoundTransport(JsonSerializer.Serialize(new PortalSearchFilePage(status, digest, 1392, 1401, files, null), PortalJson.Options));
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), status.Job.OrganizationId, Actor.Token), transport);
        var reply = await client.SearchTaskAsync("files", status.Job.JobId.ToString("D"), digest: digest, offset: 1392);
        Assert.Null(reply.ErrorCode); Assert.Equal(1, transport.Calls);
        var page = JsonSerializer.SerializeToElement(reply.Data, PortalJson.Options);
        Assert.Equal(Enumerable.Range(1392, 9), page.GetProperty("files").EnumerateArray().Select(file => file.GetProperty("id").GetInt32()));
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextOffset").ValueKind);
    }

    private sealed class SearchBoundTransport(string body) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Assert.Equal(HttpMethod.Get, request.Method);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            response.Headers.CacheControl = new() { NoStore = true };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task FailedOriginalRetrievalStillChecksRevocationAndCannotMaskItAsStorageFailure()
    {
        var client = new SearchProject { Exists = true }; client.Value = Completed(client.Value);
        client.DenyAfter = 1; var service = new PortalSearchTasks(client, null, null);
        var failure = await Assert.ThrowsAsync<PortalFault>(() => service.ResultAsync(Actor, client.Value.Job.OrganizationId, client.Value.Job.JobId, default));
        Assert.Equal("CONTROL_WORKSPACE_ACCESS_DENIED", failure.Code); Assert.Equal(2, client.Reads); Assert.Equal(0, client.Creates);
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("producer")]
    [InlineData("number")]
    [InlineData("secret-reason")]
    public async Task SearchDiagnosticsCannotClaimInputRetryOrAcceptUnscopedPublicationHistory(string change)
    {
        var client = new SearchProject { Exists = true }; client.Value = Completed(client.Value);
        var attempt = new PortalSearchPublicationAttempt(1, client.Value.ProducerId!.Value, Guid.NewGuid(), client.Value.Publication!.ExecutionId,
            client.Value.Publication.IndexSha256, client.Value.CreatedAt, client.Value.FinishedAt, "Published", null, false);
        attempt = change switch
        {
            "retry" => attempt with { WebsiteInputRetried = true },
            "producer" => attempt with { ProducerId = Guid.NewGuid() },
            "number" => attempt with { Number = 2 },
            _ => attempt with { ErrorCode = "private-provider-message" }
        };
        client.Diagnostics = new("hsr-search-task-diagnostics-v1", client.Value.Job.JobId, [attempt]);
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new PortalSearchTasks(client, null, null).DiagnosticsAsync(Actor,
            client.Value.Job.OrganizationId, client.Value.Job.JobId, default));
        Assert.Equal("CONTROL_SEARCH_TASK_RESPONSE_INVALID", failure.Code); Assert.DoesNotContain("private-provider-message", failure.Message);
    }

    internal static async Task AssertOriginalDeliveryAsync(SyntheticSearchTaskArchive archive, SyntheticSearchTaskStart start,
        SyntheticSearchTaskPublication publication, ImmutableDictionary<string, byte[]> originals)
    {
        var client = new SearchProject
        {
            Exists = true,
            Value = Completed(Queued()) with
            { Job = start.Job, ProducerId = start.WorkerId, Publication = publication }
        };
        var service = new PortalSearchTasks(client, archive, () => Task.FromResult(true));
        await using var server = await Server(new(), service); using var http = PortalHttpTests.Authorized(server);
        var root = $"/api/workspaces/{start.Job.OrganizationId:D}/search-demonstrations/{start.Job.JobId:D}";
        using var resultResponse = await http.GetAsync(root + "/result"); Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        var result = JsonSerializer.Deserialize<PortalSearchResult>(await resultResponse.Content.ReadAsStringAsync(), PortalJson.Options)!;
        Assert.Equal(publication.IndexSha256, result.IndexSha256); Assert.Equal(4, result.KeyboardTrials.Length);
        Assert.Equal("NotAssessed", result.WebsiteEvidenceAssessment); Assert.False(result.LegalCaseDetermined);
        var video = result.KeyboardTrials[0].Video;
        var original = originals[video.Path]; var copied = new List<byte>(); var offset = 0;
        while (true)
        {
            using var response = await http.GetAsync(root + $"/originals/{video.Id}/{offset}/{publication.IndexSha256}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = JsonSerializer.Deserialize<PortalSearchOriginalPage>(await response.Content.ReadAsStringAsync(), PortalJson.Options)!;
            Assert.Equal(offset, page.Offset); Assert.Equal(video, page.File); Assert.Equal(TraversalArchive.Hash(page.Data), page.PageSha256);
            copied.AddRange(page.Data); if (page.NextOffset is not { } next) break; offset = next;
        }
        Assert.Equal(original, copied.ToArray()); Assert.Equal(video.Sha256, TraversalArchive.Hash(copied.ToArray()));
        using var eof = await http.GetAsync(root + $"/originals/{video.Id}/{original.Length}/{publication.IndexSha256}");
        Assert.Equal(HttpStatusCode.OK, eof.StatusCode);
        var end = JsonSerializer.Deserialize<PortalSearchOriginalPage>(await eof.Content.ReadAsStringAsync(), PortalJson.Options)!;
        Assert.Empty(end.Data); Assert.Null(end.NextOffset); Assert.Equal(original.Length, end.Offset);
        var names = new List<string>(); offset = 0;
        while (true)
        {
            var page = await service.FilesAsync(Actor, start.Job.OrganizationId, start.Job.JobId, offset, publication.IndexSha256, default);
            Assert.InRange(page.Files.Length, 1, 24); Assert.Equal(originals.Count, page.Total);
            names.AddRange(page.Files.Select(file => file.Path)); if (page.NextOffset is not { } next) break; offset = next;
        }
        Assert.Equal(originals.Keys.Order(StringComparer.Ordinal), names);
        await using var mcp = await Connect(server.BaseAddress, start.Job.OrganizationId);
        var mcpArgs = new Dictionary<string, object?> { ["jobId"] = start.Job.JobId };
        var mcpResult = (await Call(mcp, "search_demonstration_result", mcpArgs)).GetProperty("data");
        Assert.Equal(publication.Observation, mcpResult.GetProperty("observation").GetString());
        Assert.Equal(4, mcpResult.GetProperty("keyboardTrials").GetArrayLength());
        mcpArgs["indexSha256"] = publication.IndexSha256;
        var filesPage = (await Call(mcp, "search_demonstration_files", mcpArgs)).GetProperty("data");
        Assert.Equal(originals.Count, filesPage.GetProperty("total").GetInt32());
        mcpArgs["fileId"] = video.Id;
        var mcpOriginal = (await Call(mcp, "search_demonstration_original", mcpArgs)).GetProperty("data");
        Assert.Equal(original.AsSpan(0, Math.Min(original.Length, 65536)).ToArray(), mcpOriginal.GetProperty("data").GetBytesFromBase64());
        mcpArgs["offset"] = original.Length;
        var mcpEof = (await Call(mcp, "search_demonstration_original", mcpArgs)).GetProperty("data"); Assert.Empty(mcpEof.GetProperty("data").GetBytesFromBase64());
        var changed = await Assert.ThrowsAsync<PortalFault>(() => service.OriginalAsync(Actor, start.Job.OrganizationId, start.Job.JobId,
            video.Id, 0, new('e', 64), default)); Assert.Equal(409, changed.Status);
        client.DenyAfter = client.Reads + 1;
        var revoked = await Assert.ThrowsAsync<PortalFault>(() => service.OriginalAsync(Actor, start.Job.OrganizationId, start.Job.JobId,
            video.Id, original.Length, publication.IndexSha256, default));
        Assert.Equal("CONTROL_WORKSPACE_ACCESS_DENIED", revoked.Code); Assert.Equal(0, client.Creates);
        var deniedMcp = await Call(mcp, "search_demonstration_original", mcpArgs, error: true);
        Assert.DoesNotContain(Convert.ToBase64String(original), deniedMcp.GetRawText()); Assert.Equal(0, client.Creates);
    }

    private static SyntheticSearchTaskStatus Completed(SyntheticSearchTaskStatus queued) => queued with
    {
        State = "completed",
        Phase = "Finished",
        FinishedAt = queued.CreatedAt.AddSeconds(30),
        UpdatedAt = queued.CreatedAt.AddSeconds(30),
        ProducerId = Guid.NewGuid(),
        Publication = new(SyntheticSearchTaskPublication.CurrentSchema, Guid.NewGuid(), new('a', 64), 10000,
            new('b', 64), 1000000, 100, 900000, "AllTestedKeyboardGoalsObserved", true, true, new('c', 64))
    };
    private static Task<HostedControlServer> Server(PortalProjectStub identity, PortalSearchTasks service) =>
        HostedControlServer.StartWithSearchAsync(new(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/portal-validation/evidence"),
            new('a', 40), 0), identity, new PortalObservationStub(), service);

    private static Task<McpClient> Connect(Uri origin, Guid organization)
    {
        var assembly = typeof(PortalSearchTasksTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "controlled search API protocol test",
            EnvironmentVariables = new Dictionary<string, string?>
            { ["HSR_MCP_API_ORIGIN"] = origin.ToString(), ["HSR_MCP_ORGANIZATION"] = organization.ToString(), ["HSR_MCP_ACCESS_TOKEN"] = Actor.Token }
        }));
    }
    private static async Task<JsonElement> Call(McpClient mcp, string name, Dictionary<string, object?> args, bool error = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var reply = await mcp.CallToolAsync(name, args, cancellationToken: deadline.Token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(reply.Content)).Text;
        Assert.Equal(error, reply.IsError ?? false); Assert.DoesNotContain(Actor.Token, text);
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(text), 1, 131072);
        using var json = JsonDocument.Parse(text); Assert.True(JsonElement.DeepEquals(json.RootElement, reply.StructuredContent!.Value));
        return json.RootElement.Clone();
    }

    internal sealed class SearchProject : IPortalSearchTaskProject
    {
        internal SyntheticSearchTaskStatus Value { get; set; } = Queued();
        internal bool Exists { get; set; }
        internal int Reads { get; private set; }
        internal int Creates { get; private set; }
        internal int Cancels { get; private set; }
        internal int DenyAfter { get; set; } = int.MaxValue;
        internal List<PortalIdentity> Actors { get; } = [];
        internal PortalSearchDiagnostics? Diagnostics { get; set; }
        public Task<SyntheticSearchTaskStatus?> ReadSearchTaskAsync(PortalIdentity actor, Guid organization, Guid? job, Guid? request, CancellationToken token)
        {
            Reads++; Actors.Add(actor); if (Reads > DenyAfter) throw new PortalFault(403, "CONTROL_WORKSPACE_ACCESS_DENIED");
            return Task.FromResult(Exists ? Value : null);
        }
        public Task<SyntheticSearchTaskStatus> CreateSearchTaskAsync(PortalIdentity actor, Guid organization, PortalCreateSearchTaskRequest request, CancellationToken token)
        { Creates++; Actors.Add(actor); Exists = true; return Task.FromResult(Value); }
        public Task<SyntheticSearchTaskStatus?> CancelSearchTaskAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token)
        {
            Cancels++; Value = Value with { State = "cancelled", FinishedAt = Value.CreatedAt.AddSeconds(1), UpdatedAt = Value.CreatedAt.AddSeconds(1), ErrorCode = "SEARCH_TASK_CANCELLED" };
            return Task.FromResult<SyntheticSearchTaskStatus?>(Value);
        }
        public Task<PortalSearchDiagnostics?> ReadSearchDiagnosticsAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token) => Task.FromResult(Diagnostics);
    }
}
