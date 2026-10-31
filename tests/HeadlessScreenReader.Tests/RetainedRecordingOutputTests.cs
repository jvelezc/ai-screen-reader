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

public sealed class RetainedRecordingOutputTests
{
    private static FixtureJob Job(bool passive = false) => new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1)
    { RecordObservation = true, TraversalRoute = passive ? 0 : 1 };
    private static readonly Lazy<Task<byte[]>> Keyboard = new(() => KeyboardFixtureWorker.RecordAsync(Job()));
    private static readonly Lazy<Task<byte[]>> Passive = new(() => FixtureWorker.RecordAsync(Job(true)));
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-retained-output-" + Guid.NewGuid().ToString("N"));
        public FixtureJob Job { get; }
        public Guid Worker { get; } = Guid.NewGuid();
        public string Outputs => Path.Combine(Root, ".runtime", "recorded-output");
        public Files(bool passive = false) { Job = RetainedRecordingOutputTests.Job(passive); Directory.CreateDirectory(Root); }
        public async Task<byte[]> Stage(bool seal = true, bool acknowledge = true)
        {
            var bytes = await (Job.TraversalRoute == 0 ? Passive : Keyboard).Value;
            var native = new OwnershipJournal(Path.Combine(Root, ".runtime"));
            var intent = await native.BeginAsync(Job with { RecordObservation = false, TraversalRoute = 0 }, "podman", "sha256:" + new string('a', 64), default);
            var journal = await RecordedOutputJournal.BeginAsync(Outputs, intent, Job, Worker, default);
            if (seal) await journal.SealAsync(bytes, _ => Task.FromResult(true), default);
            if (acknowledge) { await native.RecordRemovalAsync(intent, false, default); await journal.AcknowledgeRemovalAsync(native, default); }
            return bytes;
        }
        public RecordedOutputSnapshot Snapshot() => RecordedOutputJournal.Read(Outputs, Job, Worker, default);
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
            Worker = files.Worker; Base.Record = Base.Record with { State = "blocked" };
            Base.Job = new(files.Job.OrganizationId, files.Job.ScanId, files.Job.JobId, "blocked", 1, null, null, "WORKER_OUTPUT_PUBLICATION_UNCONFIRMED")
            { RecordObservation = true, TraversalRoute = files.Job.TraversalRoute };
        }
        public Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token) => Base.AuthenticateAsync(bearer, token);
        public Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token) => Base.EnsureWorkspaceAsync(actor, token);
        public Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token) =>
            Base.Revoked ? Task.FromException<PortalWorkspaceResponse?>(new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED")) : Base.WorkspaceAsync(actor, organization, token);
        public Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token) => Base.ScansAsync(actor, organization, token);
        public Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token) => Base.CreateScanAsync(actor, organization, request, token);
        public Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => Base.ScanAsync(actor, organization, scan, token);
        public Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => Base.JobAsync(actor, organization, scan, token);
        public Task<Guid?> AttemptWorkerAsync(PortalIdentity actor, FixtureJob job, CancellationToken token)
        { WorkerReads++; Assert.Equal(PortalProjectClientTests.Bearer, actor.Token); Assert.Equal(Base.Job.JobId, job.JobId); return Task.FromResult(Worker); }
    }
    private sealed class Reader(RecordedOutputRead value) : IPortalRecordedOutputReader
    {
        public int Calls { get; private set; }
        public Action? During { get; set; }
        public Exception? Failure { get; set; }
        public Task<RecordedOutputRead> ReadAsync(FixtureJob job, Guid worker, CancellationToken token)
        { Calls++; During?.Invoke(); return Failure is null ? Task.FromResult(value) : Task.FromException<RecordedOutputRead>(Failure); }
    }
    private static PortalIdentity Actor => new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static string PathFor(Files f) => $"/api/workspaces/{f.Job.OrganizationId:D}/scans/{f.Job.ScanId:D}/recorded-output";
    private static PortalProgressService Service(Files f, Project p, IPortalRecordedOutputReader? r = null) =>
        new(p, new PortalProgressReader(f.Root), recordedOutputReader: r ?? new PortalRecordedOutputReader(f.Root));

    [Theory]
    [InlineData("missing", "NotRetained")]
    [InlineData("header", "Incomplete")]
    [InlineData("sealed", "CleanupUnconfirmed")]
    [InlineData("acknowledged", "Available")]
    public async Task StatesCannotPromotePartialOrUnconfirmedOutputToCompletedExecution(string stored, string expected)
    {
        using var f = new Files(); if (stored != "missing") await f.Stage(stored != "header", stored == "acknowledged");
        var p = new Project(f); var value = await Service(f, p).OutputManifestAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default);
        Assert.Equal(expected, value.OutputState); Assert.Equal("blocked", value.Scan.State); Assert.Equal(f.Job.JobId, value.JobId);
        Assert.Equal(f.Worker, value.WorkerId); Assert.Equal(2, p.WorkerReads); Assert.Equal(0, p.Base.CreateCalls);
        Assert.All(p.Base.SeenBearers, token => Assert.Equal(Actor.Token, token));
        if (value.Output is { } output) { Assert.False(output.CanReplay); Assert.False(output.IsCompletionReceipt); Assert.Equal("NotAssessed", output.GoalVerification); }
        else
        {
            Assert.Equal(404, (await Assert.ThrowsAsync<PortalFault>(() => Service(f, p).OutputPageAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, new(new string('a', 64)), default))).Status);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrecordedOrUnstartedDoesNotReadStorageOrInventAProducer(bool unstarted)
    {
        using var f = new Files(); var p = new Project(f);
        p.Base.Job = p.Base.Job with { RecordObservation = unstarted, TraversalRoute = 0, Generation = unstarted ? 0 : 1, State = unstarted ? "queued" : "blocked" };
        p.Base.Record = p.Base.Record with { State = p.Base.Job.State };
        var r = new Reader(new(RecordedOutputStorageState.Missing)) { Failure = new IOException("must not read") };
        var result = await Service(f, p, r).OutputManifestAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default);
        Assert.Equal(unstarted ? "NotStarted" : "NotApplicable", result.OutputState); Assert.Null(result.Output); Assert.Null(result.WorkerId);
        Assert.Equal(0, r.Calls); Assert.Equal(0, p.WorkerReads); Assert.Equal(0, p.Base.CreateCalls);
    }
    [Theory]
    [InlineData("worker", false)]
    [InlineData("generation", false)]
    [InlineData("workspace", false)]
    [InlineData("revoke", false)]
    [InlineData("worker", true)]
    [InlineData("workspace", true)]
    [InlineData("revoke", true)]
    public async Task AuthorityAndActualProducerAreRecheckedEvenAfterStorageFailure(string change, bool failure)
    {
        using var f = new Files(); await f.Stage(); var p = new Project(f);
        var reader = new Reader(new(RecordedOutputStorageState.Sealed, f.Snapshot()))
        {
            Failure = failure ? new IOException("private storage details") : null,
            During = () =>
            {
                switch (change)
                {
                    case "worker": p.Worker = Guid.NewGuid(); break;
                    case "generation": p.Base.Job = p.Base.Job with { Generation = 2 }; break;
                    case "workspace": p.Base.WorkspaceHidden = true; break;
                    case "revoke": p.Base.Revoked = true; break;
                }
            }
        };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Service(f, p, reader).OutputManifestAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default));
        Assert.Equal(change is "worker" or "generation" ? "CONTROL_OUTPUT_ATTEMPT_CHANGED" : change == "workspace" ? "CONTROL_WORKSPACE_NOT_FOUND" : "CONTROL_AUTHENTICATION_REQUIRED", fault.Code);
        Assert.Equal(1, reader.Calls); Assert.Equal(0, p.Base.CreateCalls);
    }
    [Theory]
    [InlineData("job")]
    [InlineData("worker")]
    [InlineData("hash")]
    [InlineData("bytes")]
    [InlineData("native")]
    [InlineData("header")]
    public async Task ProducerSealAndNativeRemovalCannotBeSubstitutedByAReadProvider(string changed)
    {
        using var f = new Files(); await f.Stage(); var p = new Project(f); var value = f.Snapshot();
        value = changed switch
        {
            "job" => value with { Header = value.Header with { Job = value.Header.Job with { JobId = Guid.NewGuid() } } },
            "worker" => value with { Header = value.Header with { WorkerId = Guid.NewGuid() } },
            "hash" => value with { Seal = value.Seal with { BundleSha256 = new string('a', 64) } },
            "bytes" => value with { Bundle = [1, 2, 3] },
            "native" => value with { NativeRemoval = value.NativeRemoval! with { Intent = value.Header.Native with { Owner = Guid.NewGuid() } } },
            "header" => value with { Seal = value.Seal with { HeaderSha256 = new string('b', 64) } },
            _ => throw new InvalidOperationException()
        };
        var r = new Reader(new(RecordedOutputStorageState.Sealed, value));
        Assert.Equal("CONTROL_OUTPUT_INVALID", (await Assert.ThrowsAsync<PortalFault>(() => Service(f, p, r).OutputManifestAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default))).Code);
        Assert.Equal(2, p.WorkerReads); Assert.Equal(1, r.Calls); Assert.Equal(0, p.Base.CreateCalls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualChromiumZipSurvivesProtectedHttpAndPrivateMcpReconnectWithoutPublication(bool passive)
    {
        using var f = new Files(passive); var original = await f.Stage(); var p = new Project(f); var observations = new PortalObservationStub();
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, f.Root, new string('a', 40), 0), p, observations);
        using var http = PortalHttpTests.Authorized(server);
        using var manifest = await http.GetAsync(PathFor(f)); Assert.Equal(HttpStatusCode.OK, manifest.StatusCode); Assert.True(manifest.Headers.CacheControl!.NoStore);
        var status = (await manifest.Content.ReadFromJsonAsync<PortalRecordedOutputResponse>(PortalJson.Options))!;
        Assert.Equal("Available", status.OutputState); Assert.Equal("blocked", status.Scan.State); Assert.Equal(TraversalArchive.Hash(original), status.Output!.BundleSha256);
        Assert.Equal(passive ? "PassiveObservation" : "KeyboardTraversal", status.Output.Recording);
        var text = await manifest.Content.ReadAsStringAsync(); Assert.DoesNotContain("native-removal", text); Assert.DoesNotContain("containerAbsenceConfirmed", text);
        Assert.DoesNotContain("sha256:" + new string('a', 64), text); Assert.DoesNotContain(f.Snapshot().Header.Native.Owner.ToString("D"), text);
        if (passive) Assert.Equal("NotApplicable", (await Service(f, p).ReadAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default)).ProgressState);
        for (var connection = 0; connection < 2; connection++)
        {
            await using var mcp = await Connect(server.BaseAddress);
            var toolManifest = await mcp.CallToolAsync("scan_output_manifest", new Dictionary<string, object?> { ["scanId"] = f.Job.ScanId.ToString("D") });
            Assert.False(toolManifest.IsError ?? false); var declared = toolManifest.StructuredContent!.Value.GetProperty("data").GetProperty("output");
            Assert.Equal(status.Output.BundleSha256, declared.GetProperty("bundleSha256").GetString()); Assert.False(declared.GetProperty("canReplay").GetBoolean());
            using var bytes = new MemoryStream(); var offset = 0;
            while (true)
            {
                var reply = await mcp.CallToolAsync("scan_output_file", new Dictionary<string, object?>
                { ["scanId"] = f.Job.ScanId.ToString("D"), ["bundleSha256"] = status.Output.BundleSha256, ["offset"] = offset });
                Assert.False(reply.IsError ?? false); var body = Assert.IsType<TextContentBlock>(Assert.Single(reply.Content)).Text;
                Assert.InRange(Encoding.UTF8.GetByteCount(body), 1, 131072); using var json = JsonDocument.Parse(body);
                Assert.True(JsonElement.DeepEquals(json.RootElement, reply.StructuredContent!.Value));
                var page = json.RootElement.GetProperty("data").Deserialize<RetainedRecordingOutputPage>(PortalJson.Options)!;
                Assert.Equal(status.Output, page.Output); Assert.Equal(offset, page.Offset); Assert.Equal(TraversalArchive.Hash(page.Data), page.PageSha256);
                bytes.Write(page.Data); if (page.NextOffset is null) break; offset = page.NextOffset.Value;
            }
            Assert.Equal(original, bytes.ToArray()); Assert.Equal(status.Output.BundleSha256, TraversalArchive.Hash(bytes.ToArray()));
            using var eof = await http.PostAsJsonAsync(PathFor(f) + "/bytes", new PortalRecordedOutputRequest(status.Output.BundleSha256, original.Length));
            Assert.Equal(HttpStatusCode.OK, eof.StatusCode); var final = (await eof.Content.ReadFromJsonAsync<RetainedRecordingOutputPage>(PortalJson.Options))!;
            Assert.Empty(final.Data); Assert.Null(final.NextOffset); Assert.Equal(TraversalArchive.Hash([]), final.PageSha256);
        }
        p.Base.Revoked = true;
        using var denied = await http.PostAsJsonAsync(PathFor(f) + "/bytes", new PortalRecordedOutputRequest(status.Output.BundleSha256, original.Length));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Equal(0, p.Base.CreateCalls); Assert.Equal(0, observations.Calls);
    }
    [Theory]
    [InlineData("{\"bundleSha256\":\"HASH\",\"workerId\":\"owner\"}")]
    [InlineData("{\"bundleSha256\":\"HASH\",\"offset\":0,\"offset\":65536}")]
    [InlineData("{\"bundleSha256\":\"HASH\",\"generation\":1}")]
    [InlineData("{\"bundleSha256\":\"HASH\",\"path\":\"bundle.bin\"}")]
    [InlineData("{\"bundleSha256\":\"HASH\",\"offset\":-1}")]
    public async Task HttpRejectsAuthoritySelectorsDuplicateMembersAndInvalidOffsetsBeforeStorage(string request)
    {
        using var f = new Files(); var p = new Project(f); var r = new Reader(new(RecordedOutputStorageState.Missing));
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, f.Root, new string('a', 40), 0), p, new PortalObservationStub(), recordedOutputReader: r);
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.PostAsync(PathFor(f) + "/bytes", new StringContent(request.Replace("HASH", new string('a', 64), StringComparison.Ordinal), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, r.Calls); Assert.Equal(0, p.Base.CreateCalls);
    }
    [Theory]
    [InlineData("unaligned")]
    [InlineData("past-end")]
    [InlineData("changed")]
    public async Task BytesAreBoundToSelectedWholeBundleAndExactBoundedOffsets(string change)
    {
        using var f = new Files(); var bytes = await f.Stage(); var p = new Project(f);
        var request = new PortalRecordedOutputRequest(change == "changed" ? new string('b', 64) : TraversalArchive.Hash(bytes), change == "unaligned" ? 1 : change == "past-end" ? bytes.Length + 1 : 0);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Service(f, p).OutputPageAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, request, default));
        Assert.Equal(change == "changed" ? "CONTROL_OUTPUT_CHANGED" : "CONTROL_OUTPUT_PAGE_INVALID", fault.Code); Assert.Equal(2, p.WorkerReads); Assert.Equal(0, p.Base.CreateCalls);
    }
    [Theory]
    [InlineData("scope")]
    [InlineData("worker")]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("completion")]
    [InlineData("replay")]
    [InlineData("goal")]
    [InlineData("ack")]
    [InlineData("recording")]
    [InlineData("offset")]
    [InlineData("next")]
    [InlineData("short")]
    [InlineData("native-wrapper")]
    [InlineData("missing-flags")]
    public async Task McpRejectsForgedPageAuthorityFlagsBytesAndNativeWrappersWithoutRetry(string change)
    {
        using var f = new Files(); var original = await f.Stage(); var p = new Project(f);
        var page = await Service(f, p).OutputPageAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, new(TraversalArchive.Hash(original)), default);
        var value = JsonSerializer.SerializeToNode(page, PortalJson.Options)!;
        switch (change)
        {
            case "scope": value["output"]!["job"]!["organizationId"] = Guid.NewGuid(); break;
            case "worker": value["output"]!["workerId"] = Guid.Empty; break;
            case "hash": value["pageSha256"] = new string('b', 64); break;
            case "length": value["output"]!["bytes"] = RetainedRecordingOutput.MaximumBytes + 1; break;
            case "completion": value["output"]!["isCompletionReceipt"] = true; break;
            case "replay": value["output"]!["canReplay"] = true; break;
            case "goal": value["output"]!["goalVerification"] = "Satisfied"; break;
            case "ack": value["output"]!["nativeRemovalAcknowledged"] = false; break;
            case "recording": value["output"]!["recording"] = "PassiveObservation"; break;
            case "offset": value["offset"] = RetainedRecordingOutputPage.PageBytes; break;
            case "next": value["nextOffset"] = 1; break;
            case "short": value["data"] = Convert.ToBase64String([1, 2, 3]); break;
            case "native-wrapper": value["native"] = JsonSerializer.SerializeToNode(f.Snapshot().Header.Native, PortalJson.Options); break;
            case "missing-flags": value["output"]!.AsObject().Remove("canReplay"); break;
        }
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal(PathFor(f) + "/bytes", request.RequestUri!.AbsolutePath);
            Assert.Equal(Actor.Token, request.Headers.Authorization!.Parameter);
            return new(HttpStatusCode.OK) { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json"), Headers = { CacheControl = new() { NoStore = true } } };
        });
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, Actor.Token), handler);
        var reply = await client.OutputPageAsync(f.Job.ScanId.ToString(), TraversalArchive.Hash(original));
        Assert.Equal(change is "native-wrapper" or "missing-flags" ? "HOSTED_SCAN_OPERATION_UNCONFIRMED" : "HOSTED_RESPONSE_INVALID", reply.ErrorCode);
        Assert.Equal(1, handler.Calls); Assert.Equal(0, p.Base.CreateCalls);
    }
    [Theory]
    [InlineData("scope")]
    [InlineData("producer")]
    [InlineData("generation")]
    [InlineData("state")]
    [InlineData("missing-output")]
    [InlineData("unexpected-output")]
    public async Task McpManifestMustAgreeWithItsOriginalScanAndProducer(string change)
    {
        using var f = new Files(); await f.Stage(); var p = new Project(f);
        var manifest = await Service(f, p).OutputManifestAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default);
        var value = JsonSerializer.SerializeToNode(manifest, PortalJson.Options)!;
        switch (change)
        {
            case "scope": value["scan"]!["id"] = Guid.NewGuid(); break;
            case "producer": value["output"]!["workerId"] = Guid.NewGuid(); break;
            case "generation": value["output"]!["job"]!["generation"] = 2; break;
            case "state": value["outputState"] = "Ready"; break;
            case "missing-output": value["output"] = null; break;
            case "unexpected-output": value["outputState"] = "Incomplete"; break;
        }
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json"), Headers = { CacheControl = new() { NoStore = true } } });
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, Actor.Token), handler);
        var reply = await client.OutputManifestAsync(f.Job.ScanId.ToString());
        Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode); Assert.Equal(1, handler.Calls); Assert.Equal(0, p.Base.CreateCalls);
    }
    [Fact]
    public async Task ActualInterruptedChromiumIntentCannotBecomeAnExportThroughHttpOrPrivateMcp()
    {
        using var f = new Files(); var native = new OwnershipJournal(Path.Combine(f.Root, ".runtime"));
        var intent = await native.BeginAsync(f.Job with { RecordObservation = false, TraversalRoute = 0 }, "podman", "sha256:" + new string('a', 64), default);
        _ = await RecordedOutputJournal.BeginAsync(f.Outputs, intent, f.Job, f.Worker, default);
        var progress = await CommandProgressJournal.BeginAsync(Path.Combine(f.Root, ".runtime", "command-progress"), intent, f.Job, f.Worker, default); var sequence = 0;
        async Task Checkpoint(TraversalCheckpoint checkpoint, CancellationToken token)
        {
            await progress.AppendAsync(new(FixtureProgressProtocol.Schema, intent.Owner, ++sequence, checkpoint), token);
            if (checkpoint.Phase == TraversalCheckpointPhase.Intent && checkpoint.CommandIndex == 2) throw new IOException("deliberate interrupted output");
        }
        await Assert.ThrowsAsync<BrowserOperationException>(() => KeyboardFixtureWorker.RecordAsync(f.Job, checkpoint: Checkpoint));
        var p = new Project(f); await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, f.Root, new string('a', 40), 0), p, new PortalObservationStub());
        using var http = PortalHttpTests.Authorized(server); var status = (await http.GetFromJsonAsync<PortalRecordedOutputResponse>(PathFor(f), PortalJson.Options))!;
        Assert.Equal("Incomplete", status.OutputState); Assert.Null(status.Output); Assert.Equal("blocked", status.Scan.State);
        var graph = (await http.GetFromJsonAsync<PortalProgressGraphResponse>(PathFor(f).Replace("recorded-output", "progress/graph", StringComparison.Ordinal), PortalProgressArchiveJson.Options))!.Graph!;
        Assert.Equal(4, graph.Checkpoints.Length); Assert.Null(graph.Edges[^1].CompletedInputCalls); Assert.Null(graph.Edges[^1].ResultObservationId); Assert.True(graph.Edges[^1].InputMayHaveOccurred);
        await using var client = await Connect(server.BaseAddress);
        var reply = await client.CallToolAsync("scan_output_manifest", new Dictionary<string, object?> { ["scanId"] = f.Job.ScanId.ToString("D") });
        Assert.False(reply.IsError ?? false); var value = reply.StructuredContent!.Value.GetProperty("data"); Assert.Equal("Incomplete", value.GetProperty("outputState").GetString()); Assert.Equal(JsonValueKind.Null, value.GetProperty("output").ValueKind);
        var denied = await client.CallToolAsync("scan_output_file", new Dictionary<string, object?> { ["scanId"] = f.Job.ScanId.ToString("D"), ["bundleSha256"] = new string('a', 64) });
        Assert.True(denied.IsError ?? false); Assert.Equal(0, p.Base.CreateCalls); Assert.Equal(1, p.Base.Job.Generation); Assert.Equal(f.Worker, p.Worker);
    }
    [Fact]
    public async Task OpenApiDescribesTheActualProtectedRecordingReadContract()
    {
        await using var server = await PortalHttpTests.Start(new()); using var http = new HttpClient { BaseAddress = server.BaseAddress };
        using var doc = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json")); var spec = doc.RootElement;
        var path = "/api/workspaces/{organization}/scans/{scan}/recorded-output";
        Assert.Equal(1, spec.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("security").GetArrayLength());
        Assert.Equal(1, spec.GetProperty("paths").GetProperty(path + "/bytes").GetProperty("post").GetProperty("security").GetArrayLength());
        var schemas = spec.GetProperty("components").GetProperty("schemas"); var request = schemas.GetProperty("RetainedRecordingOutputRequest");
        Assert.Equal(new[] { "bundleSha256" }, request.GetProperty("required").EnumerateArray().Select(v => v.GetString())); Assert.False(request.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(RetainedRecordingOutput.MaximumBytes, request.GetProperty("properties").GetProperty("offset").GetProperty("maximum").GetInt32());
        var output = schemas.GetProperty("RetainedRecordingOutput").GetProperty("properties").GetProperty("output").GetProperty("properties");
        Assert.Equal("NotAssessed", output.GetProperty("goalVerification").GetProperty("const").GetString()); Assert.False(output.GetProperty("canReplay").GetProperty("const").GetBoolean());
        Assert.True(output.GetProperty("nativeRemovalAcknowledged").GetProperty("const").GetBoolean()); Assert.False(output.TryGetProperty("native", out _));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(send(request)); }
    }
    private static Task<McpClient> Connect(Uri origin)
    {
        var assembly = typeof(RetainedRecordingOutputTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "retained recording integration",
            EnvironmentVariables = new Dictionary<string, string?>
            { ["HSR_MCP_API_ORIGIN"] = origin.ToString(), ["HSR_MCP_ORGANIZATION"] = PortalProjectClientTests.Organization.ToString(), ["HSR_MCP_ACCESS_TOKEN"] = PortalProjectClientTests.Bearer }
        }));
    }
}
