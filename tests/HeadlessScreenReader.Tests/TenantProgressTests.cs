using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

public sealed class TenantProgressTests
{
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-tenant-progress-" + Guid.NewGuid().ToString("N"));
        public FixtureJob Job { get; } = new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1)
        { RecordObservation = true, TraversalRoute = 1 };
        public Guid Worker { get; } = Guid.NewGuid();
        public NativeOwnership Native { get; }
        public string ProgressRoot => Path.Combine(Root, ".runtime", "command-progress");
        public string Scope => Path.Combine(ProgressRoot, $"{Job.OrganizationId:N}-{Job.JobId:N}-1");
        public Files()
        {
            Directory.CreateDirectory(Root);
            Native = new(NativeOwnership.CurrentSchema, Guid.NewGuid(), Job with { RecordObservation = false, TraversalRoute = 0 }, "podman", "sha256:" + new string('a', 64));
        }
        public Task<CommandProgressJournal> Begin() => CommandProgressJournal.BeginAsync(ProgressRoot, Native, Job, Worker, default);
        public RetainedCommandProgress Sample() => new(RetainedCommandProgress.CurrentSchema, Job, Worker,
            2, 1, 0, "Intent", 1, "Tab", null, "o1", null, null, null, new string('a', 64), false, true);
        public FixtureProgressMessage Baseline() => new(FixtureProgressProtocol.Schema, Native.Owner, 1,
            new(TraversalCheckpointPhase.Baseline, 0, new("o1", FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Snapshot with { DocumentUrl = null },
                ReaderMode.Browse, null, new string('a', 64), 0)));
        public void Dispose()
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(Root)));
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
    private sealed class Project : IPortalProjectClient
    {
        public PortalProjectStub Base { get; } = new();
        public Guid? Worker { get; set; }
        public int WorkerReads { get; private set; }
        public Project(Files files)
        {
            Worker = files.Worker;
            Base.Record = Base.Record with { State = "blocked" };
            Base.Job = new(files.Job.OrganizationId, files.Job.ScanId, files.Job.JobId, "blocked", 1, null, null, "WORKER_INPUT_OUTCOME_UNCONFIRMED")
            { RecordObservation = true, TraversalRoute = 1 };
        }
        public Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token) => Base.AuthenticateAsync(bearer, token);
        public Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token) => Base.EnsureWorkspaceAsync(actor, token);
        public Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token) => Base.WorkspaceAsync(actor, organization, token);
        public Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token) => Base.ScansAsync(actor, organization, token);
        public Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token) => Base.CreateScanAsync(actor, organization, request, token);
        public Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => Base.ScanAsync(actor, organization, scan, token);
        public Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => Base.JobAsync(actor, organization, scan, token);
        public Task<Guid?> AttemptWorkerAsync(PortalIdentity actor, FixtureJob job, CancellationToken token)
        {
            WorkerReads++; Assert.Equal(PortalProjectClientTests.Bearer, actor.Token);
            Assert.Equal(Base.Job.JobId, job.JobId); return Task.FromResult(Worker);
        }
    }
    private sealed class Reader(RetainedCommandProgress? value) : IPortalProgressReader
    {
        public int Calls { get; private set; }
        public Action? During { get; set; }
        public PortalFault? Failure { get; set; }
        public Task<RetainedCommandProgress?> ReadAsync(FixtureJob job, Guid worker, CancellationToken token)
        {
            Calls++; During?.Invoke();
            return Failure is null ? Task.FromResult(value) : Task.FromException<RetainedCommandProgress?>(Failure);
        }
    }
    private static PortalIdentity Actor => new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static string PathFor(Files f) => $"/api/workspaces/{f.Job.OrganizationId:D}/scans/{f.Job.ScanId:D}/progress";

    [Theory]
    [InlineData("phase")]
    [InlineData("counts")]
    [InlineData("command")]
    [InlineData("source")]
    [InlineData("hash")]
    [InlineData("completion")]
    [InlineData("replay")]
    [InlineData("goal")]
    [InlineData("input")]
    public void PublicProgressCannotAcceptInventedPhasesCountsCommandsOrAuthority(string change)
    {
        using var f = new Files(); var valid = f.Sample(); valid.Validate();
        var changed = change switch
        {
            "phase" => valid with { LastPhase = "Observed" },
            "counts" => valid with { Results = 1 },
            "command" => valid with { Command = "Activate" },
            "source" => valid with { SourceObservationId = null },
            "hash" => valid with { PrefixSha256 = "invalid" },
            "completion" => valid with { IsCompletionReceipt = true },
            "replay" => valid with { CanReplay = true },
            "goal" => valid with { GoalVerification = "Verified" },
            "input" => valid with { InputMayHaveOccurred = false },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("WORKER_PROGRESS_INVALID", Assert.Throws<BrowserOperationException>(changed.Validate).Code);
    }

    [Fact]
    public async Task LocalProjectionRetainsHeaderAndIntentWithoutLeakingNativeOwnershipOrSnapshots()
    {
        using var f = new Files(); var journal = await f.Begin(); var reader = new PortalProgressReader(f.Root);
        var header = await reader.ReadAsync(f.Job, f.Worker, default);
        Assert.Equal(0, header!.Records); Assert.Null(header.LastPhase); Assert.False(header.InputMayHaveOccurred);
        var baseline = f.Baseline(); await journal.AppendAsync(baseline, default);
        await journal.AppendAsync(baseline with { Sequence = 2, Checkpoint = new(TraversalCheckpointPhase.Intent, 1, baseline.Checkpoint.Source, AuthoredKeyboardRoutes.Plans[0].Instructions[0]) }, default);
        var value = await reader.ReadAsync(f.Job, f.Worker, default);
        Assert.Equal(2, value!.Records); Assert.Equal("Intent", value.LastPhase); Assert.Equal("Tab", value.Command);
        Assert.Equal(1, value.Intents); Assert.Equal(0, value.Results); Assert.True(value.InputMayHaveOccurred);
        Assert.Null(value.LastCompletedInputCalls); Assert.Equal("o1", value.SourceObservationId);
        Assert.False(value.CanReplay); Assert.False(value.IsCompletionReceipt); Assert.Equal("NotAssessed", value.GoalVerification);
        var json = JsonSerializer.Serialize(value, PortalJson.Options);
        Assert.DoesNotContain(f.Native.Owner.ToString("D"), json); Assert.DoesNotContain("Snapshot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Capability", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("sha256:", json);
        var wrongWorker = await Assert.ThrowsAsync<PortalFault>(() => reader.ReadAsync(f.Job, Guid.NewGuid(), default));
        Assert.Equal(409, wrongWorker.Status); Assert.Equal("CONTROL_PROGRESS_INVALID", wrongWorker.Code);
        File.AppendAllText(Directory.GetFiles(f.Scope, "0001-*.json").Single(), " ");
        Assert.Equal("CONTROL_PROGRESS_INVALID", (await Assert.ThrowsAsync<PortalFault>(() => reader.ReadAsync(f.Job, f.Worker, default))).Code);
    }

    [Theory]
    [InlineData("NotApplicable")]
    [InlineData("NotStarted")]
    [InlineData("NotRetained")]
    public async Task MissingProgressStatesRemainDistinctAndDoNotReadUnstartedOrUnrelatedStorage(string state)
    {
        using var f = new Files(); var project = new Project(f); var reader = new Reader(null);
        if (state == "NotApplicable") project.Base.Job = project.Base.Job with { TraversalRoute = 0 };
        if (state == "NotStarted") { project.Base.Job = project.Base.Job with { State = "queued", Generation = 0 }; project.Base.Record = project.Base.Record with { State = "queued" }; }
        var value = await new PortalProgressService(project, reader).ReadAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default);
        Assert.Equal(state, value.ProgressState); Assert.Null(value.Progress); Assert.Equal(f.Job.JobId, value.JobId);
        Assert.Equal(state == "NotRetained" ? 1 : 0, reader.Calls);
        Assert.Equal(state == "NotRetained" ? 2 : 0, project.WorkerReads);
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("generation")]
    [InlineData("worker")]
    [InlineData("cancel")]
    [InlineData("foreign")]
    [InlineData("missingworker")]
    [InlineData("payloadworker")]
    [InlineData("storage")]
    [InlineData("membershipstorage")]
    [InlineData("state")]
    public async Task LostAuthorityChangedAttemptAndStorageFailureCannotReleaseOldProgress(string scenario)
    {
        using var f = new Files(); var project = new Project(f); var reader = new Reader(scenario == "payloadworker" ? f.Sample() with { WorkerId = Guid.NewGuid() } : f.Sample());
        using var stop = new CancellationTokenSource();
        reader.During = () =>
        {
            if (scenario is "membership" or "membershipstorage") project.Base.WorkspaceHidden = true;
            if (scenario == "generation") project.Base.Job = project.Base.Job with { Generation = 2 };
            if (scenario == "worker") project.Worker = Guid.NewGuid();
            if (scenario == "cancel") stop.Cancel();
        };
        if (scenario is "storage" or "membershipstorage") reader.Failure = new(503, "CONTROL_PROGRESS_UNAVAILABLE");
        if (scenario == "missingworker") project.Worker = null;
        if (scenario == "state") project.Base.Record = project.Base.Record with { State = "queued" };
        var organization = scenario == "foreign" ? Guid.NewGuid() : f.Job.OrganizationId;
        var task = new PortalProgressService(project, reader).ReadAsync(Actor, organization, f.Job.ScanId, stop.Token);
        if (scenario == "cancel") await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        else
        {
            var error = await Assert.ThrowsAsync<PortalFault>(() => task);
            Assert.Equal(scenario switch { "membership" or "membershipstorage" or "foreign" => 404, "generation" or "worker" or "payloadworker" => 409, _ => 503 }, error.Status);
            Assert.Equal(scenario switch
            {
                "membership" or "membershipstorage" or "foreign" => "CONTROL_WORKSPACE_NOT_FOUND",
                "generation" or "worker" => "CONTROL_PROGRESS_ATTEMPT_CHANGED",
                "payloadworker" or "missingworker" => "CONTROL_PROGRESS_IDENTITY_UNCONFIRMED",
                "state" => "CONTROL_STATE_UNCONFIRMED",
                _ => "CONTROL_PROGRESS_UNAVAILABLE"
            }, error.Code);
        }
        Assert.Equal(scenario is "foreign" or "missingworker" or "state" ? 0 : 1, reader.Calls);
        if (scenario is not ("foreign" or "missingworker" or "membership" or "membershipstorage" or "state")) Assert.Equal(2, project.WorkerReads);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    public async Task AttemptLookupSelectsOnlyTheActualScopedWorkerUsingOriginalRlsBearer(string shape)
    {
        using var f = new Files(); var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            requests++;
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization!.Parameter);
            Assert.Equal("hsr", request.Headers.GetValues("Accept-Profile").Single());
            Assert.Equal($"/rest/v1/job_attempts?select=worker_id&organization_id=eq.{f.Job.OrganizationId:D}&job_id=eq.{f.Job.JobId:D}&generation=eq.1&limit=2", request.RequestUri!.PathAndQuery);
            var row = "{\"worker_id\":\"" + f.Worker.ToString("D") + "\"}";
            var json = shape switch { "empty" => "[]", "duplicate" => "[" + row + "," + row + "]", "invalid" => "[{\"worker_id\":\"not-a-guid\"}]", _ => "[" + row + "]" };
            return JsonResponse(json);
        }));
        var client = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        if (shape is "duplicate" or "invalid") Assert.Equal("CONTROL_PROVIDER_RESPONSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => client.AttemptWorkerAsync(Actor, f.Job, default))).Code);
        else Assert.Equal(shape == "empty" ? null : f.Worker, await client.AttemptWorkerAsync(Actor, f.Job, default));
        Assert.Equal(1, requests);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }
    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json"), Headers = { CacheControl = new() { NoStore = true } } };

    [Theory]
    [InlineData("valid")]
    [InlineData("scope")]
    [InlineData("replay")]
    [InlineData("missing")]
    [InlineData("changedworker")]
    [InlineData("input")]
    public async Task McpProgressRejectsContradictoryScopeAndFlagsAndUsesFreshReadOnlyRequests(string change)
    {
        using var f = new Files(); var calls = 0;
        var body = JsonSerializer.SerializeToNode(new PortalProgressResponse(new(f.Job.OrganizationId, f.Job.ScanId, "synthetic-shop", FixtureObservation.Profile, "blocked", DateTimeOffset.UtcNow),
            f.Job.JobId, 1, f.Worker, "Available", f.Sample()), PortalJson.Options)!;
        if (change == "scope") body["progress"]!["job"]!["scanId"] = Guid.NewGuid();
        if (change == "replay") body["progress"]!["canReplay"] = true;
        if (change == "missing") body["progress"] = null;
        if (change == "changedworker") body["workerId"] = Guid.NewGuid();
        if (change == "input") body["progress"]!["inputMayHaveOccurred"] = false;
        using var handler = new Handler(request =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal(PathFor(f), request.RequestUri!.PathAndQuery);
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization!.Parameter); Assert.Null(request.Content);
            return JsonResponse(body.ToJsonString());
        });
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        Assert.Equal("SCAN_ID_REQUIRED", (await client.ProgressAsync("../other")).ErrorCode); Assert.Equal(0, calls);
        var reply = await client.ProgressAsync(f.Job.ScanId.ToString("D"));
        if (change == "valid")
        {
            Assert.True(reply.Status == "scan-progress", reply.ToJson()); Assert.Null(reply.ErrorCode);
            Assert.DoesNotContain(PortalProjectClientTests.Bearer, reply.ToJson());
            Assert.Equal("scan-progress", (await client.ProgressAsync(f.Job.ScanId.ToString("D"))).Status); Assert.Equal(2, calls);
        }
        else { Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode); Assert.Equal(1, calls); }
    }

    [Fact]
    public async Task ActualChromiumInterruptedPrefixSurvivesHttpAndPrivateStdioReconnectWithoutBecomingACompletedRecording()
    {
        // Provider identity is synthetic; Chromium, journal, HTTP and stdio are actual.
        using var f = new Files(); var journal = await f.Begin(); var sequence = 0; var completedInputSeen = false;
        await Assert.ThrowsAsync<BrowserOperationException>(() => KeyboardFixtureWorker.RecordAsync(f.Job, checkpoint: async (cp, token) =>
        {
            await journal.AppendAsync(new(FixtureProgressProtocol.Schema, f.Native.Owner, ++sequence, cp), token);
            if (cp.Phase == TraversalCheckpointPhase.Observed)
                completedInputSeen |= cp.Attempt!.Events.Any(e => e.Kind == ReaderEventKind.InputCompleted);
            if (cp.Phase == TraversalCheckpointPhase.Intent && cp.CommandIndex == 2) throw new IOException("stop after retained intent");
        }));
        Assert.True(completedInputSeen); Assert.Equal(4, sequence);
        var project = new Project(f); var observations = new PortalObservationStub();
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, f.Root, new string('a', 40), 0), project, observations);
        using var anonymous = new HttpClient { BaseAddress = server.BaseAddress };
        using var anonymousReply = await anonymous.GetAsync(PathFor(f)); Assert.Equal(HttpStatusCode.Unauthorized, anonymousReply.StatusCode);
        Assert.Equal(0, project.Base.AuthCalls); Assert.Equal(0, project.WorkerReads);
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.GetAsync(PathFor(f)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore); var payload = await response.Content.ReadFromJsonAsync<PortalProgressResponse>(PortalJson.Options);
        Assert.Equal("Available", payload!.ProgressState); Assert.Equal("blocked", payload.Scan.State);
        Assert.Equal(4, payload.Progress!.Records); Assert.Equal(1, payload.Progress.Results); Assert.Equal("Intent", payload.Progress.LastPhase);
        Assert.Equal("Activate", payload.Progress.Command); Assert.Equal("o2", payload.Progress.SourceObservationId);
        Assert.True(payload.Progress.InputMayHaveOccurred); Assert.Null(payload.Progress.LastCompletedInputCalls);
        Assert.False(payload.Progress.IsCompletionReceipt); Assert.False(payload.Progress.CanReplay); Assert.Equal("NotAssessed", payload.Progress.GoalVerification);
        for (var i = 0; i < 2; i++)
        {
            await using var mcp = await Connect(server.BaseAddress);
            var reply = await mcp.CallToolAsync("scan_progress", new Dictionary<string, object?> { ["scanId"] = f.Job.ScanId.ToString("D") });
            Assert.False(reply.IsError ?? false);
            using var json = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(reply.Content)).Text);
            Assert.True(JsonElement.DeepEquals(json.RootElement, reply.StructuredContent!.Value));
            Assert.Equal("Intent", json.RootElement.GetProperty("data").GetProperty("progress").GetProperty("lastPhase").GetString());
            Assert.False(json.RootElement.GetProperty("data").GetProperty("progress").GetProperty("canReplay").GetBoolean());
            Assert.InRange(Encoding.UTF8.GetByteCount(json.RootElement.GetRawText()), 1, 131072);
        }
        using var query = await http.GetAsync(PathFor(f) + "?workerId=untrusted"); Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        project.Base.Revoked = true; using var denied = await http.GetAsync(PathFor(f)); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await using var deniedMcp = await Connect(server.BaseAddress);
        var deniedReply = await deniedMcp.CallToolAsync("scan_progress", new Dictionary<string, object?> { ["scanId"] = f.Job.ScanId.ToString("D") });
        Assert.True(deniedReply.IsError);
        using var deniedJson = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(deniedReply.Content)).Text);
        Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", deniedJson.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(0, observations.Calls); Assert.Equal(0, project.Base.CreateCalls);
    }

    [Fact]
    public async Task ActualCompletedRouteStillReturnsDiagnosticsOnlyWhenItsLastCommandReachedTheGoal()
    {
        using var f = new Files(); var journal = await f.Begin(); var sequence = 0;
        var bytes = await KeyboardFixtureWorker.RecordAsync(f.Job, checkpoint: (cp, token) => journal.AppendAsync(new(FixtureProgressProtocol.Schema, f.Native.Owner, ++sequence, cp), token));
        var original = KeyboardRecordingBundle.Decode(bytes, f.Job).Validate(f.Job);
        Assert.Equal(TraversalReachability.Reached, original.Receipt.Reachability);
        var value = await new PortalProgressReader(f.Root).ReadAsync(f.Job, f.Worker, default);
        Assert.Equal(5, value!.Records); Assert.Equal(2, value.Results); Assert.Equal("Observed", value.LastPhase);
        Assert.Equal("Activate", value.Command); Assert.Equal(1, value.LastCompletedInputCalls);
        Assert.Equal(original.Receipt.Attempts[^1].Step!.Verification.ToString(), value.LastCommandVerification);
        Assert.Equal("NotAssessed", value.GoalVerification); Assert.False(value.IsCompletionReceipt); Assert.False(value.CanReplay);
    }

    [Theory]
    [InlineData("NotApplicable")]
    [InlineData("NotStarted")]
    [InlineData("NotRetained")]
    public async Task McpNonCollectedProgressNeverInventsAPrefixOrLosesAttemptIdentity(string state)
    {
        using var f = new Files();
        var body = new PortalProgressResponse(new(f.Job.OrganizationId, f.Job.ScanId, "synthetic-shop", FixtureObservation.Profile, "blocked", DateTimeOffset.UtcNow),
            f.Job.JobId, state == "NotStarted" ? 0 : 1, state == "NotRetained" ? f.Worker : null, state, null);
        using var handler = new Handler(_ => JsonResponse(JsonSerializer.Serialize(body, PortalJson.Options)));
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        var reply = await client.ProgressAsync(f.Job.ScanId.ToString("D")); Assert.Equal("scan-progress", reply.Status);
        using var json = JsonDocument.Parse(reply.ToJson()); var data = json.RootElement.GetProperty("data");
        Assert.Equal(state, data.GetProperty("progressState").GetString()); Assert.Equal(f.Job.JobId, data.GetProperty("jobId").GetGuid());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("progress").ValueKind);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("root-file")]
    [InlineData("parent-file")]
    [InlineData("scope-file")]
    [InlineData("pending")]
    public async Task MissingCorruptAndUncommittedLocalStorageRemainDistinct(string state)
    {
        using var f = new Files(); var reader = new PortalProgressReader(f.Root);
        if (state == "missing")
        {
            Assert.Null(await reader.ReadAsync(f.Job, f.Worker, default));
            Assert.Equal("CONTROL_PROGRESS_UNAVAILABLE", (await Assert.ThrowsAsync<PortalFault>(() => new PortalProgressReader(Path.Combine(f.Root, "absent")).ReadAsync(f.Job, f.Worker, default))).Code);
        }
        else if (state == "pending")
        {
            var journal = await f.Begin(); await journal.AppendAsync(f.Baseline(), default);
            File.WriteAllText(Path.Combine(f.Scope, "0002-" + new string('a', 64) + ".json.pending"), "unfinished");
            var value = await reader.ReadAsync(f.Job, f.Worker, default);
            Assert.True(value!.HasUncommittedWrite); Assert.Equal(1, value.Records); Assert.Equal("Baseline", value.LastPhase);
            Assert.False(value.InputMayHaveOccurred); Assert.False(value.IsCompletionReceipt); Assert.False(value.CanReplay);
        }
        else
        {
            var path = state == "parent-file" ? Path.Combine(f.Root, ".runtime") : state == "root-file" ? f.ProgressRoot : f.Scope;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "unrelated");
            Assert.Equal("CONTROL_PROGRESS_INVALID", (await Assert.ThrowsAsync<PortalFault>(() => reader.ReadAsync(f.Job, f.Worker, default))).Code);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyScopeIdentifiersCannotReachProviderOrEvidence(bool organization)
    {
        using var f = new Files(); var project = new Project(f); var reader = new Reader(f.Sample());
        var error = await Assert.ThrowsAsync<PortalFault>(() => new PortalProgressService(project, reader)
            .ReadAsync(Actor, organization ? Guid.Empty : f.Job.OrganizationId, organization ? f.Job.ScanId : Guid.Empty, default));
        Assert.Equal(400, error.Status); Assert.Equal("CONTROL_REQUEST_INVALID", error.Code);
        Assert.Equal(0, project.Base.DataCalls); Assert.Equal(0, reader.Calls);
    }
    private static Task<McpClient> Connect(Uri origin)
    {
        var assembly = typeof(TenantProgressTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "retained progress integration",
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HSR_MCP_API_ORIGIN"] = origin.ToString(),
                ["HSR_MCP_ORGANIZATION"] = PortalProjectClientTests.Organization.ToString(),
                ["HSR_MCP_ACCESS_TOKEN"] = PortalProjectClientTests.Bearer
            }
        }));
    }
}
