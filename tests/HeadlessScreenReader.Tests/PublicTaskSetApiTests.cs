using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskSetApiTests
{
    private static readonly Guid Request = Guid.Parse("60000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task AdmissionPlansOnlyTheExactVerifiedPageAndReturnsIndependentBoundedJobs(int count)
    {
        using var rig = Seed(count); PublicTaskSet? stored = null;
        rig.ReadSet = json => { AssertScope(rig, json, 3); return stored; };
        rig.CreateSet = json =>
        {
            AssertScope(rig, json, 6);
            Assert.Equal(rig.Digest, json.GetProperty("p_parent_original_sha256").GetString());
            Assert.Equal(4, json.GetProperty("p_maximum_tasks").GetInt32());
            Assert.Equal(PublicTaskSet.SupportedKinds.Take(count), json.GetProperty("p_goal_kinds").EnumerateArray().Select(p => p.GetString()));
            return stored = Value(rig, count);
        };
        var result = await Start(rig);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(stored), JsonSerializer.SerializeToElement(result))); Assert.Equal(count, result.Tasks.Length);
        Assert.Equal(1, rig.SetCreates); Assert.Equal(2, rig.SetReads);
        Assert.Equal(1, rig.CoverageReads); Assert.True(rig.OriginalReads > 0);
        Assert.False(result.IsAssessment); Assert.True(result.TaskResultsRequireOriginalRead);
        Assert.Equal(PublicTaskSet.SupportedKinds.Take(count), result.Tasks.Select(t => t.Job.GoalKind));
    }

    [Fact]
    public async Task LostAdmissionResponseReconcilesTheSameSetWhileTheWorkerIsOffline()
    {
        using var rig = Seed(4); PublicTaskSet? stored = null;
        rig.ReadSet = _ => stored;
        rig.CreateSet = _ => { stored = Value(rig, 4); throw new HttpRequestException("PRIVATE_TRANSPORT_CANARY"); };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Start(rig));
        Assert.Equal(503, fault.Status); Assert.NotNull(stored);
        Assert.DoesNotContain("PRIVATE_TRANSPORT_CANARY", fault.ToString());
        rig.ProcessorReady = false;
        var reads = rig.OriginalReads; var readiness = rig.ReadinessReads;
        var recovered = await Start(rig);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(stored), JsonSerializer.SerializeToElement(recovered))); Assert.Equal(1, rig.SetCreates);
        Assert.Equal(reads, rig.OriginalReads); Assert.Equal(readiness, rig.ReadinessReads);
    }

    [Theory]
    [InlineData("Digest")]
    [InlineData("Budget")]
    public async Task ExistingRequestRejectsAChangedPlanWithoutReadingOriginalsOrStartingMoreJobs(string change)
    {
        using var rig = Seed(4); rig.ReadSet = _ => Value(rig, 4);
        var request = new PortalCreatePublicTaskSetRequest(Request, change == "Digest" ? new('b', 64) : rig.Digest, change == "Budget" ? 3 : 4);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Start(rig, request));
        Assert.Equal(409, fault.Status); Assert.Equal("CONTROL_REQUEST_CONFLICT", fault.Code);
        Assert.Equal(0, rig.SetCreates); Assert.Equal(0, rig.OriginalReads); Assert.Equal(0, rig.ReadinessReads);
    }

    [Theory]
    [InlineData("Request")]
    [InlineData("Digest")]
    [InlineData("Zero")]
    [InlineData("Five")]
    public async Task MalformedRequestsAreRejectedBeforeAnyProjectRead(string boundary)
    {
        using var rig = Seed(4);
        var request = new PortalCreatePublicTaskSetRequest(boundary == "Request" ? Guid.Empty : Request,
            boundary == "Digest" ? new('A', 64) : rig.Digest, boundary == "Zero" ? 0 : boundary == "Five" ? 5 : 4);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Start(rig, request));
        Assert.Equal(400, fault.Status); Assert.Equal(0, rig.SetReads); Assert.Equal(0, rig.SetCreates); Assert.Equal(0, rig.OriginalReads);
    }

    [Fact]
    public async Task NewAdmissionRequiresAReadyWorkerAndCurrentVerifiedOriginal()
    {
        using var rig = Seed(4); rig.ProcessorReady = false;
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Start(rig));
        Assert.Equal("CONTROL_PUBLIC_TASK_RUNTIME_UNAVAILABLE", fault.Code);
        Assert.Equal(0, rig.OriginalReads); Assert.Equal(0, rig.SetCreates);
        rig.ProcessorReady = true; rig.Visible = false;
        fault = await Assert.ThrowsAsync<PortalFault>(() => Start(rig));
        Assert.Equal(404, fault.Status); Assert.Equal(0, rig.OriginalReads); Assert.Equal(0, rig.SetCreates);
    }

    [Theory]
    [InlineData("Digest")]
    [InlineData("Source")]
    [InlineData("Kinds")]
    [InlineData("Budget")]
    [InlineData("Creator")]
    [InlineData("Parent")]
    [InlineData("Request")]
    public async Task AdmissionCannotReleaseAProviderSetThatDiffersFromItsOriginalPlan(string change)
    {
        using var rig = Seed(1);
        rig.CreateSet = _ =>
        {
            var value = Value(rig, 1);
            return change switch
            {
                "Digest" => value with { ParentOriginalSha256 = new('b', 64), Tasks = value.Tasks.Select(t => t with { Job = t.Job with { ParentOriginalSha256 = new('b', 64) } }).ToImmutableArray() },
                "Source" => value with { Source = PublicReaderSource.FromUrl("https://elsewhere.example/"), Tasks = value.Tasks.Select(t => t with { Job = t.Job with { Source = PublicReaderSource.FromUrl("https://elsewhere.example/") } }).ToImmutableArray() },
                "Kinds" => value with { GoalKinds = ["Help"], Tasks = [value.Tasks[0] with { Job = value.Tasks[0].Job with { GoalKind = "Help" } }] },
                "Budget" => value with { MaximumTasks = 2 },
                "Creator" => value with { CreatedBy = Guid.NewGuid() },
                "Parent" => value with { ParentScanId = Guid.NewGuid() },
                "Request" => value with { RequestId = Guid.NewGuid() },
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
        };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Start(rig));
        Assert.Equal(503, fault.Status); Assert.Equal("CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID", fault.Code);
        Assert.Equal(1, rig.SetCreates);
    }

    [Theory]
    [InlineData("Denied")]
    [InlineData("Missing")]
    [InlineData("Replaced")]
    [InlineData("Progress")]
    public async Task FinalCurrentReadReauthorizesAndPreservesImmutableAdmissionIdentity(string outcome)
    {
        using var rig = Seed(1); var value = Value(rig, 1); var exists = false;
        rig.CreateSet = _ => { exists = true; return value; };
        rig.ReadSet = _ => !exists ? null : outcome switch
        {
            "Denied" => throw new PortalFault(403, "CONTROL_WORKSPACE_DENIED"),
            "Missing" => null,
            "Replaced" => value with { SetId = Guid.NewGuid() },
            "Progress" => value with { Tasks = [value.Tasks[0] with { State = "leased", Phase = "Starting" }] },
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        if (outcome == "Progress") Assert.Equal("leased", (await Start(rig)).Tasks[0].State);
        else
        {
            var fault = await Assert.ThrowsAsync<PortalFault>(() => Start(rig));
            Assert.Equal(outcome == "Denied" ? 403 : 503, fault.Status);
        }
        Assert.Equal(1, rig.SetCreates); Assert.Equal(2, rig.SetReads);
    }

    [Theory]
    [InlineData(false, false, 401)]
    [InlineData(true, true, 400)]
    [InlineData(true, false, 200)]
    public async Task StatusHttpUsesCurrentAuthenticationAndNeverAdmitsOrLoadsOriginals(bool authenticated, bool query, int expected)
    {
        using var rig = Seed(1); rig.ReadSet = _ => Value(rig, 1);
        var diagnostics = new List<PortalRequestDiagnostic>(); await using var server = await Server(rig, diagnostics);
        using var http = PortalHttpTests.Authorized(server);
        if (!authenticated) http.DefaultRequestHeaders.Authorization = null;
        using var response = await http.GetAsync(Path(rig) + $"/{Request:D}" + (query ? "?actor=forged" : ""));
        Assert.Equal(expected, (int)response.StatusCode); Assert.Equal(0, rig.SetCreates); Assert.Equal(0, rig.OriginalReads);
        Assert.Equal(expected == 200 ? 1 : 0, rig.SetReads);
        Assert.True(response.Headers.CacheControl!.NoStore); Assert.NotEmpty(response.Headers.GetValues("X-HSR-Request-Id"));
        Assert.Equal("ReadPublicTaskSet", Assert.Single(diagnostics).Operation);
        if (expected == 200)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var member = json.RootElement.GetProperty("tasks")[0];
            Assert.False(member.TryGetProperty("publication", out _)); Assert.False(member.TryGetProperty("producerId", out _));
            Assert.False(json.RootElement.GetProperty("isAssessment").GetBoolean());
        }
    }

    [Fact]
    public async Task HttpAdmissionReturnsTheStableRequestLocationAndQuotaRemainsAClosed429()
    {
        using var rig = Seed(1); PublicTaskSet? stored = null; rig.ReadSet = _ => stored; rig.CreateSet = _ => stored = Value(rig, 1);
        var diagnostics = new List<PortalRequestDiagnostic>(); await using var server = await Server(rig, diagnostics);
        using var http = PortalHttpTests.Authorized(server);
        using var admitted = await http.PostAsJsonAsync(Path(rig), new PortalCreatePublicTaskSetRequest(Request, rig.Digest, 4));
        Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode); Assert.Equal(Path(rig) + $"/{Request:D}", admitted.Headers.Location?.ToString());
        Assert.Equal("AdmitPublicTaskSet", Assert.Single(diagnostics).Operation);
        stored = null; rig.RejectSetQuota = true;
        using var denied = await http.PostAsJsonAsync(Path(rig), new PortalCreatePublicTaskSetRequest(Request, rig.Digest, 4));
        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
        var text = await denied.Content.ReadAsStringAsync(); Assert.DoesNotContain("PRIVATE_PROVIDER_DETAIL", text);
        Assert.Contains("CONTROL_PUBLIC_WEBSITE_QUEUE_FULL", text); Assert.Equal(2, rig.SetCreates);
    }

    [Fact]
    public void OpenApiDeclaresAtomicAdmissionNullableRecoveryAndNoAggregateAssessment()
    {
        using var json = JsonDocument.Parse(PortalOpenApi.Document); var root = json.RootElement;
        const string path = "/api/workspaces/{organization}/website-scans/{scan}/tasks/sets";
        var post = root.GetProperty("paths").GetProperty(path).GetProperty("post");
        Assert.Equal("AdmitPublicTaskSet", post.GetProperty("operationId").GetString());
        Assert.True(post.GetProperty("responses").GetProperty("202").GetProperty("headers").TryGetProperty("Location", out _));
        Assert.True(post.GetProperty("security")[0].TryGetProperty("bearerAuth", out _));
        var get = root.GetProperty("paths").GetProperty(path + "/{request}").GetProperty("get");
        Assert.Equal("ReadPublicTaskSet", get.GetProperty("operationId").GetString());
        Assert.Equal(new[] { "organization", "request", "scan" }, get.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).Order());
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("CreatePublicTaskSet");
        Assert.Equal(new[] { "maximumTasks", "parentOriginalSha256", "requestId" }, request.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Order());
        Assert.Equal(1, request.GetProperty("properties").GetProperty("maximumTasks").GetProperty("minimum").GetInt32());
        Assert.Equal(4, request.GetProperty("properties").GetProperty("maximumTasks").GetProperty("maximum").GetInt32());
        var properties = schemas.GetProperty("PublicTaskSet").GetProperty("properties");
        Assert.False(properties.GetProperty("isAssessment").GetProperty("const").GetBoolean());
        Assert.True(properties.GetProperty("taskResultsRequireOriginalRead").GetProperty("const").GetBoolean());
        Assert.Equal(4, properties.GetProperty("tasks").GetProperty("maxItems").GetInt32());
        Assert.Equal("null", schemas.GetProperty("PublicTaskSetOrNull").GetProperty("oneOf")[1].GetProperty("type").GetString());
    }

    private static PublicWebsiteApiTests.Rig Seed(int count)
    {
        var rig = new PublicWebsiteApiTests.Rig();
        WebsiteScreeningApiTests.Seed(rig, [new("root", "RootWebArea", "Test page"),
            .. PublicTaskSet.SupportedKinds.Take(count).Select((kind, i) => new AccessibleNode($"link-{i}", "link", kind))]);
        return rig;
    }

    [Theory]
    [InlineData("MissingRequired")]
    [InlineData("WrongType")]
    [InlineData("UnexpectedPublication")]
    [InlineData("Scope")]
    [InlineData("Assessment")]
    [InlineData("OriginalRead")]
    public async Task InvalidProviderSetJsonIsAClosedUpstreamFailureRatherThanACallerError(string boundary)
    {
        using var rig = Seed(1);
        var raw = JsonSerializer.SerializeToNode(Value(rig, 1), ReaderWorkerWire.Json)!.AsObject();
        switch (boundary)
        {
            case "MissingRequired": raw.Remove("Source"); break;
            case "WrongType": raw["Tasks"] = "PRIVATE_PROVIDER_CANARY"; break;
            case "UnexpectedPublication": raw["Tasks"]![0]!["Publication"] = new JsonObject(); break;
            case "Scope": raw["Scope"] = "WholeSite"; break;
            case "Assessment": raw["IsAssessment"] = true; break;
            case "OriginalRead": raw["TaskResultsRequireOriginalRead"] = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(boundary));
        }
        rig.ReadSet = _ => raw;
        var service = new PortalPublicTasks(rig.Project, null, null);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => service.SetStatusAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, Request, default));
        Assert.Equal(503, fault.Status); Assert.Equal("CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID", fault.Code);
        Assert.DoesNotContain("PRIVATE_PROVIDER_CANARY", fault.ToString()); Assert.Equal(0, rig.SetCreates);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("ForeignParent")]
    [InlineData("Pending")]
    [InlineData("Digest")]
    [InlineData("ObservationDigest")]
    [InlineData("Capture")]
    [InlineData("Captures")]
    [InlineData("Received")]
    [InlineData("Coverage")]
    public async Task InvalidCoverageCannotAuthorizeAnySetOrInput(string boundary)
    {
        using var rig = Seed(1);
        var observed = await rig.Service.CoverageAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default);
        observed = boundary switch
        {
            "Missing" => null!,
            "ForeignParent" => observed with { Scan = observed.Scan with { Id = Guid.NewGuid() } },
            "Pending" => observed with { Scan = observed.Scan with { State = "queued", Generation = 0, OriginalSha256 = null, ProducerId = null } },
            "Digest" => observed with { Scan = observed.Scan with { OriginalSha256 = new('b', 64) } },
            "ObservationDigest" => observed with { OriginalSha256 = new('b', 64) },
            "Capture" => observed with { CaptureIndex = 0 },
            "Captures" => observed with { Captures = 1 },
            "Received" => observed with { ReceivedAt = default },
            "Coverage" => observed with { Coverage = observed.Coverage with { Groups = default } },
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        var service = new PortalPublicTasks(rig.Project, null, () => Task.FromResult(true));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => service.StartSetAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId,
            new(Request, rig.Digest, 4), _ => Task.FromResult(observed), default));
        Assert.Equal(503, fault.Status); Assert.Equal(0, rig.SetCreates);
    }

    [Fact]
    public async Task CancellationAfterInspectionCannotDispatchTheAtomicAdmission()
    {
        using var rig = Seed(1); using var cancelled = new CancellationTokenSource();
        var service = new PortalPublicTasks(rig.Project, null, () => Task.FromResult(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartSetAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId,
            new(Request, rig.Digest, 4), async token =>
            {
                var observed = await rig.Service.CoverageAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, token);
                cancelled.Cancel(); return observed;
            }, cancelled.Token));
        Assert.Equal(1, rig.CoverageReads); Assert.Equal(0, rig.SetCreates);
    }

    [Fact]
    public async Task PrivateStdioAndHttpReconcileTheSameTaskSetWithoutRepeatingAdmissionOrOriginalReads()
    {
        using var rig = Seed(4); PublicTaskSet? stored = null; rig.ReadSet = _ => stored; rig.CreateSet = _ => stored = Value(rig, 4);
        await using var server = await Server(rig, []); await using var mcp = await Connect(server.BaseAddress, rig.Job.OrganizationId);
        var tools = await mcp.ListToolsAsync(); Assert.Equal(46, tools.Count);
        Assert.Single(tools, t => t.Name == "website_task_set_start"); Assert.Single(tools, t => t.Name == "website_task_set_status");
        var args = new Dictionary<string, object?> { ["scanId"] = rig.Job.ScanId, ["requestId"] = Request, ["parentOriginalSha256"] = rig.Digest, ["maximumTasks"] = 4 };
        var admitted = (await Call(mcp, "website_task_set_start", args)).GetProperty("data");
        Assert.Equal(4, admitted.GetProperty("tasks").GetArrayLength()); Assert.False(admitted.GetProperty("isAssessment").GetBoolean());
        var reads = rig.OriginalReads; rig.ProcessorReady = false;
        var recovered = (await Call(mcp, "website_task_set_start", args)).GetProperty("data");
        Assert.True(JsonElement.DeepEquals(admitted, recovered)); Assert.Equal(1, rig.SetCreates); Assert.Equal(reads, rig.OriginalReads);
        args["maximumTasks"] = 3;
        Assert.Equal("CONTROL_REQUEST_CONFLICT", (await Call(mcp, "website_task_set_start", args, true)).GetProperty("errorCode").GetString());
        args.Remove("maximumTasks"); args.Remove("parentOriginalSha256");
        Assert.True(JsonElement.DeepEquals(admitted, (await Call(mcp, "website_task_set_status", args)).GetProperty("data")));
        rig.ReadSet = _ => throw new PortalFault(403, "CONTROL_WORKSPACE_DENIED");
        var denied = await Call(mcp, "website_task_set_status", args, true);
        Assert.Equal("CONTROL_WORKSPACE_DENIED", denied.GetProperty("errorCode").GetString());
        Assert.DoesNotContain(stored!.SetId.ToString("D"), denied.GetRawText()); Assert.Equal(1, rig.SetCreates); Assert.Equal(reads, rig.OriginalReads);
    }

    [Theory]
    [InlineData("Organization")]
    [InlineData("Parent")]
    [InlineData("Request")]
    [InlineData("Budget")]
    [InlineData("Digest")]
    [InlineData("Source")]
    [InlineData("Assessment")]
    [InlineData("Scope")]
    [InlineData("OriginalRead")]
    [InlineData("Publication")]
    [InlineData("MissingJob")]
    [InlineData("DuplicateJob")]
    [InlineData("MissingScope")]
    [InlineData("MissingAssessment")]
    [InlineData("MissingOriginalRead")]
    public async Task PrivateConsumerRejectsForeignOrContradictoryTaskSetMetadata(string boundary)
    {
        using var rig = Seed(2); var value = Value(rig, 2);
        var raw = JsonSerializer.SerializeToNode(value, PortalJson.Options)!;
        switch (boundary)
        {
            case "Organization": raw["organizationId"] = Guid.NewGuid(); break;
            case "Parent": raw["parentScanId"] = Guid.NewGuid(); break;
            case "Request": raw["requestId"] = Guid.NewGuid(); break;
            case "Budget": raw["maximumTasks"] = 3; break;
            case "Digest": raw["parentOriginalSha256"] = new string('b', 64); break;
            case "Source": raw["source"]!["allowedHost"] = "other.example"; break;
            case "Assessment": raw["isAssessment"] = true; break;
            case "Scope": raw["scope"] = "WholeSite"; break;
            case "OriginalRead": raw["taskResultsRequireOriginalRead"] = false; break;
            case "Publication": raw["tasks"]![0]!["publication"] = new JsonObject { ["outcome"] = "WebsiteEvidenceReady" }; break;
            case "MissingJob": raw["tasks"]![0]!["job"] = null; break;
            case "DuplicateJob": raw["tasks"]![1]!["job"] = raw["tasks"]![0]!["job"]!.DeepClone(); break;
            case "MissingScope": raw.AsObject().Remove("scope"); break;
            case "MissingAssessment": raw.AsObject().Remove("isAssessment"); break;
            case "MissingOriginalRead": raw.AsObject().Remove("taskResultsRequireOriginalRead"); break;
            default: throw new ArgumentOutOfRangeException(nameof(boundary));
        }
        using var handler = new MetadataHandler(raw.ToJsonString());
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), rig.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        var reply = await client.PublicTaskSetAsync("start", rig.Job.ScanId.ToString(), Request.ToString(), rig.Digest, 4);
        Assert.Equal("error", reply.Status); Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode); Assert.Single(handler.Requests);
        Assert.DoesNotContain("WebsiteEvidenceReady", reply.ToJson()); Assert.DoesNotContain(PortalProjectClientTests.Bearer, reply.ToJson());
    }

    [Theory]
    [InlineData("Operation")]
    [InlineData("Parent")]
    [InlineData("Request")]
    [InlineData("Digest")]
    [InlineData("Zero")]
    [InlineData("Five")]
    public async Task InvalidPrivateSetIntentNeverSendsHttp(string boundary)
    {
        using var rig = Seed(1); using var handler = new MetadataHandler("null");
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), rig.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        var reply = await client.PublicTaskSetAsync(boundary == "Operation" ? "replay" : "start",
            boundary == "Parent" ? Guid.Empty.ToString() : rig.Job.ScanId.ToString(), boundary == "Request" ? "bad" : Request.ToString(),
            boundary == "Digest" ? new('A', 64) : rig.Digest, boundary == "Zero" ? 0 : boundary == "Five" ? 5 : 4);
        Assert.Equal("PUBLIC_TASK_SET_REQUEST_INVALID", reply.ErrorCode); Assert.Empty(handler.Requests);
    }

    private sealed class MetadataHandler(string body) : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization?.Parameter);
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.CacheControl = new() { NoStore = true }; return Task.FromResult(response);
        }
    }

    private static Task<McpClient> Connect(Uri origin, Guid organization)
    {
        var assembly = typeof(PublicTaskSetApiTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = System.IO.Path.GetDirectoryName(assembly),
            Name = "task-set independent consumer",
            EnvironmentVariables = new Dictionary<string, string?>
            { ["HSR_MCP_API_ORIGIN"] = origin.ToString(), ["HSR_MCP_ORGANIZATION"] = organization.ToString(), ["HSR_MCP_ACCESS_TOKEN"] = PortalProjectClientTests.Bearer }
        }));
    }

    private static async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?> args, bool error = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await client.CallToolAsync(tool, args, cancellationToken: deadline.Token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, text); Assert.True(error == (result.IsError ?? false), text);
        Assert.InRange(Encoding.UTF8.GetByteCount(text), 1, 131072);
        using var json = JsonDocument.Parse(text); Assert.True(JsonElement.DeepEquals(json.RootElement, result.StructuredContent!.Value));
        return json.RootElement.Clone();
    }

    private static PublicTaskSet Value(PublicWebsiteApiTests.Rig rig, int count)
    {
        var sample = PublicTaskSetTests.Sample(PublicTaskSet.SupportedKinds.Take(count).ToArray());
        return sample with
        {
            OrganizationId = rig.Job.OrganizationId,
            ParentScanId = rig.Job.ScanId,
            CreatedBy = rig.Actor.UserId,
            RequestId = Request,
            ParentOriginalSha256 = rig.Digest,
            Source = rig.Job.Source,
            Tasks = sample.Tasks.Select(t => t with
            {
                CreatedBy = rig.Actor.UserId,
                Job = t.Job with { OrganizationId = rig.Job.OrganizationId, ParentScanId = rig.Job.ScanId, Source = rig.Job.Source, ParentOriginalSha256 = rig.Digest }
            }).ToImmutableArray()
        };
    }

    private static void AssertScope(PublicWebsiteApiTests.Rig rig, JsonElement json, int fields)
    {
        Assert.Equal(fields, json.EnumerateObject().Count());
        Assert.Equal(rig.Job.OrganizationId, json.GetProperty("p_organization_id").GetGuid());
        Assert.Equal(rig.Job.ScanId, json.GetProperty("p_parent_scan_id").GetGuid());
        Assert.Equal(Request, json.GetProperty("p_request_id").GetGuid());
    }

    private static Task<PublicTaskSet> Start(PublicWebsiteApiTests.Rig rig, PortalCreatePublicTaskSetRequest? request = null)
    {
        var service = new PortalPublicTasks(rig.Project, null, () => { rig.ReadinessReads++; return Task.FromResult(rig.ProcessorReady); });
        return service.StartSetAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId,
            request ?? new(Request, rig.Digest, 4), token => rig.Service.CoverageAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, token), default);
    }

    private static string Path(PublicWebsiteApiTests.Rig rig) => $"/api/workspaces/{rig.Job.OrganizationId:D}/website-scans/{rig.Job.ScanId:D}/tasks/sets";
    private static Task<HostedControlServer> Server(PublicWebsiteApiTests.Rig rig, List<PortalRequestDiagnostic> diagnostics) => HostedControlServer.StartAsync(
        new(PortalProjectClientTests.Key, System.IO.Path.GetFullPath("artifacts/public-task-set-http"), new('a', 40), 0), rig.Project,
        new PortalObservationStub(), publicWebsiteRuntimeReady: () => Task.FromResult(rig.ProcessorReady), diagnostic: diagnostics.Add);
}
