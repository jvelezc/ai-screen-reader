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

public sealed class RetainedProgressGraphTests
{
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-progress-graph-" + Guid.NewGuid().ToString("N"));
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
        public CommandProgressSnapshot Read() => CommandProgressJournal.ReadSnapshot(ProgressRoot, Job.OrganizationId, Job.JobId, 1);
        public FixtureProgressMessage[] Messages(int nameLength = 0)
        {
            var snapshot = FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Snapshot with { DocumentUrl = null };
            if (nameLength > 0) snapshot = snapshot with { Nodes = [snapshot.Nodes[0] with { Name = new string('x', nameLength) }] };
            var source = new TraversalObservation("o1", snapshot, ReaderMode.Browse, null, new string('a', 64), 0);
            var instruction = AuthoredKeyboardRoutes.Plans[0].Instructions[0];
            var result = source with { Id = "o2", ElapsedMilliseconds = 2 };
            var attempt = new TraversalAttempt("a1", source.Id, result.Id, instruction, null, [], false, 1, 2);
            return [new(FixtureProgressProtocol.Schema, Native.Owner, 1, new(TraversalCheckpointPhase.Baseline, 0, source)),
                new(FixtureProgressProtocol.Schema, Native.Owner, 2, new(TraversalCheckpointPhase.Intent, 1, source, instruction)),
                new(FixtureProgressProtocol.Schema, Native.Owner, 3, new(TraversalCheckpointPhase.Observed, 1, source, instruction, attempt, result))];
        }
        public RetainedProgressGraph Graph() => RetainedProgressProjection.Graph(Read(), Job, Worker);
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
    private sealed class Reader(CommandProgressSnapshot? value) : IPortalProgressSnapshotReader
    {
        public int Calls { get; private set; }
        public Action? During { get; set; }
        public Exception? Failure { get; set; }
        public Task<CommandProgressSnapshot?> ReadSnapshotAsync(FixtureJob job, Guid worker, CancellationToken token)
        {
            Calls++; During?.Invoke();
            return Failure is null ? Task.FromResult(value) : Task.FromException<CommandProgressSnapshot?>(Failure);
        }
    }
    private static PortalIdentity Actor => new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static string PathFor(Files f) => $"/api/workspaces/{f.Job.OrganizationId:D}/scans/{f.Job.ScanId:D}/progress";
    private static PortalProgressService Service(Files f, Project p, IPortalProgressSnapshotReader? r = null) => new(p, new PortalProgressReader(f.Root), r ?? new PortalProgressReader(f.Root));
    private static PortalProgressGraphResponse Response(Files f, RetainedProgressGraph graph) => new(new(new(f.Job.OrganizationId, f.Job.ScanId,
        "synthetic-shop", FixtureObservation.Profile, "blocked", DateTimeOffset.Parse("2026-10-30T00:00:00Z")), f.Job.JobId, 1, f.Worker, "Available", graph.Progress), graph);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task GraphPreservesHeaderBaselineUnresolvedIntentAndUnknownObservedInput(int count)
    {
        using var f = new Files(); var journal = await f.Begin();
        foreach (var message in f.Messages().Take(count)) await journal.AppendAsync(message, default);
        var graph = f.Graph(); graph.Validate();
        Assert.Equal(count, graph.Checkpoints.Length); Assert.Equal(count / 2, graph.Edges.Length);
        Assert.Equal(count == 0 ? 0 : count == 3 ? 2 : 1, graph.Nodes.Length);
        Assert.Equal(count > 1, graph.Progress.InputMayHaveOccurred); Assert.False(graph.Progress.IsCompletionReceipt);
        Assert.False(graph.Progress.CanReplay); Assert.Equal("NotAssessed", graph.Progress.GoalVerification);
        if (count > 1)
        {
            var edge = Assert.Single(graph.Edges); Assert.Equal("o1", edge.SourceObservationId);
            Assert.Equal(count == 3 ? "o2" : null, edge.ResultObservationId); Assert.Null(edge.CompletedInputCalls);
            Assert.Null(edge.Verification); Assert.Null(edge.Outcome); Assert.True(edge.InputMayHaveOccurred);
            Assert.Equal(count == 3 ? 3 : null, edge.ObservedSequence);
        }
        var json = JsonSerializer.Serialize(graph, PortalProgressArchiveJson.Options);
        Assert.DoesNotContain(f.Native.Owner.ToString("D"), json); Assert.DoesNotContain("capability", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256:", json); Assert.DoesNotContain("snapshot", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ObservedZeroInputRemainsDistinctFromMissingInputAndOverallGoalVerification()
    {
        using var f = new Files(); var journal = await f.Begin(); var messages = f.Messages();
        messages[2] = messages[2] with
        {
            Checkpoint = messages[2].Checkpoint with
            {
                Attempt = messages[2].Checkpoint.Attempt! with
                {
                    Step = new(1, ReaderCommand.Tab, ReaderMode.Browse,
                ReaderOutcome.Unknown, "retained result", "node", "node", 0, Verification: VerificationStatus.Satisfied)
                }
            }
        };
        foreach (var message in messages) await journal.AppendAsync(message, default);
        var graph = f.Graph(); Assert.Equal(0, Assert.Single(graph.Edges).CompletedInputCalls);
        Assert.Equal(VerificationStatus.Satisfied, graph.Edges[0].Verification); Assert.Equal(0, graph.Progress.LastCompletedInputCalls);
        Assert.Equal("NotAssessed", graph.Progress.GoalVerification); Assert.False(graph.Progress.IsCompletionReceipt); Assert.False(graph.Progress.CanReplay);
    }

    [Fact]
    public async Task CanonicalCheckpointPagesReconstructAllSemanticsAndKeepSourceAndExportHashesDistinct()
    {
        using var f = new Files(); var journal = await f.Begin(); var message = f.Messages(40000)[0];
        await journal.AppendAsync(message, default); var graph = f.Graph(); var descriptor = Assert.Single(graph.Checkpoints);
        var expected = FixtureProgressProtocol.Encode(message.Checkpoint); Assert.True(expected.Length > 32768);
        Assert.Equal(expected.Length, descriptor.Bytes); Assert.Equal(TraversalArchive.Hash(expected), descriptor.CheckpointSha256);
        var original = File.ReadAllBytes(Directory.GetFiles(f.Scope, "0001-*.json").Single());
        Assert.Equal(TraversalArchive.Hash(original), descriptor.SourceSha256); Assert.NotEqual(descriptor.SourceSha256, descriptor.CheckpointSha256);
        var service = Service(f, new(f)); using var reconstructed = new MemoryStream(); var offset = 0; var pages = 0;
        do
        {
            var page = await service.CheckpointAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, new(graph.Progress.PrefixSha256, 1, offset), default);
            Assert.Equal(offset, page.Offset); Assert.Equal(expected.Length, page.FileBytes); Assert.Equal(descriptor.CheckpointSha256, page.CheckpointSha256);
            Assert.Equal(descriptor.SourceSha256, page.SourceSha256); Assert.Equal(TraversalArchive.Hash(page.Data), page.PageSha256);
            Assert.InRange(page.Data.Length, 1, RetainedCheckpointPage.PageBytes); reconstructed.Write(page.Data); pages++;
            if (page.NextOffset is null) break;
            Assert.Equal(offset + page.Data.Length, page.NextOffset); offset = page.NextOffset.Value;
        } while (true);
        Assert.Equal(3, pages); Assert.Equal(expected, reconstructed.ToArray());
        using var exported = JsonDocument.Parse(reconstructed.ToArray());
        Assert.Equal(new string('x', 40000), exported.RootElement.GetProperty("Source").GetProperty("Snapshot").GetProperty("Nodes")[0].GetProperty("Name").GetString());
        Assert.False(exported.RootElement.TryGetProperty("ExecutionId", out _));
        var eof = await service.CheckpointAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, new(graph.Progress.PrefixSha256, 1, expected.Length), default);
        Assert.Empty(eof.Data); Assert.Null(eof.NextOffset); Assert.Equal(TraversalArchive.Hash([]), eof.PageSha256);
        Assert.Equal(2, Directory.GetFileSystemEntries(f.Scope).Length); Assert.Equal(original, File.ReadAllBytes(Directory.GetFiles(f.Scope, "0001-*.json").Single()));
    }

    [Theory]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(16385)]
    public async Task CheckpointPageBoundaryPreservesEveryByteAndOnlyAdvancesWhenBytesRemain(int length)
    {
        using var f = new Files(); var journal = await f.Begin(); var baseBytes = FixtureProgressProtocol.Encode(f.Messages(1)[0].Checkpoint).Length;
        var message = f.Messages(length - baseBytes + 1)[0]; await journal.AppendAsync(message, default);
        var snapshot = f.Read(); var expected = FixtureProgressProtocol.Encode(message.Checkpoint); Assert.Equal(length, expected.Length);
        var prefix = snapshot.Summary.LastSha256; var page = RetainedProgressProjection.Checkpoint(snapshot, f.Job, f.Worker, prefix, 1, 0);
        Assert.Equal(expected[..Math.Min(length, 16384)], page.Data); Assert.Equal(length > 16384 ? 16384 : null, page.NextOffset);
        Assert.Equal(TraversalArchive.Hash(expected), page.CheckpointSha256);
        if (page.NextOffset is { } next)
        {
            var last = RetainedProgressProjection.Checkpoint(snapshot, f.Job, f.Worker, prefix, 1, next);
            Assert.Equal(expected[next..], last.Data); Assert.Single(last.Data); Assert.Null(last.NextOffset);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task EmptyGraphAndCheckpointScopeCannotReachProviderOrStorage(bool organization, bool bytes)
    {
        using var f = new Files(); var project = new Project(f); var reader = new Reader(null); var service = Service(f, project, reader);
        var org = organization ? Guid.Empty : f.Job.OrganizationId; var scan = organization ? f.Job.ScanId : Guid.Empty;
        Func<Task> operation = bytes ? async () => { await service.CheckpointAsync(Actor, org, scan, new(new string('a', 64), 1), default); }
        : async () => { await service.GraphAsync(Actor, org, scan, default); };
        var error = await Assert.ThrowsAsync<PortalFault>(operation); Assert.Equal(400, error.Status); Assert.Equal("CONTROL_REQUEST_INVALID", error.Code);
        Assert.Equal(0, reader.Calls); Assert.Equal(0, project.Base.DataCalls);
    }

    [Theory]
    [InlineData("sequence-zero")]
    [InlineData("sequence-high")]
    [InlineData("sequence-absent")]
    [InlineData("negative-offset")]
    [InlineData("unaligned-offset")]
    [InlineData("beyond-eof")]
    [InlineData("prefix-format")]
    public async Task InvalidCheckpointPagesNeverAdvanceOrReleaseBytes(string change)
    {
        using var f = new Files(); var journal = await f.Begin(); await journal.AppendAsync(f.Messages()[0], default);
        var graph = f.Graph(); var descriptor = graph.Checkpoints[0]; var request = new PortalCheckpointRequest(graph.Progress.PrefixSha256, 1);
        request = change switch
        {
            "sequence-zero" => request with { Sequence = 0 },
            "sequence-high" => request with { Sequence = 10 },
            "sequence-absent" => request with { Sequence = 2 },
            "negative-offset" => request with { Offset = -1 },
            "unaligned-offset" => request with { Offset = 1 },
            "beyond-eof" => request with { Offset = descriptor.Bytes + 1 },
            "prefix-format" => request with { PrefixSha256 = "invalid" },
            _ => throw new InvalidOperationException()
        };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Service(f, new(f)).CheckpointAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, request, default));
        Assert.Equal(400, fault.Status); Assert.Equal("CONTROL_PROGRESS_PAGE_INVALID", fault.Code);
        Assert.Equal(graph.Progress.PrefixSha256, f.Graph().Progress.PrefixSha256);
    }

    [Fact]
    public async Task AppendingTheSameAttemptInvalidatesOldPageIdentityWithoutReplayOrReplacement()
    {
        using var f = new Files(); var journal = await f.Begin(); var messages = f.Messages(); await journal.AppendAsync(messages[0], default);
        var project = new Project(f); var service = Service(f, project); var old = await service.GraphAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default);
        await journal.AppendAsync(messages[1], default);
        var error = await Assert.ThrowsAsync<PortalFault>(() => service.CheckpointAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, new(old.Graph!.Progress.PrefixSha256, 1), default));
        Assert.Equal(409, error.Status); Assert.Equal("CONTROL_PROGRESS_PREFIX_CHANGED", error.Code);
        var current = await service.GraphAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default);
        Assert.Equal(2, current.Graph!.Checkpoints.Length); Assert.Null(Assert.Single(current.Graph.Edges).ObservedSequence);
        Assert.Equal(old.Status.JobId, current.Status.JobId); Assert.Equal(old.Status.WorkerId, current.Status.WorkerId);
        Assert.Equal(old.Status.Generation, current.Status.Generation); Assert.Equal(0, project.Base.CreateCalls);
    }

    [Theory]
    [InlineData("NotApplicable")]
    [InlineData("NotStarted")]
    [InlineData("NotRetained")]
    public async Task MissingGraphStatesStayExplicitAndCannotYieldCheckpointBytes(string state)
    {
        using var f = new Files(); var project = new Project(f); var reader = new Reader(null);
        if (state == "NotApplicable") project.Base.Job = project.Base.Job with { TraversalRoute = 0 };
        if (state == "NotStarted") { project.Base.Job = project.Base.Job with { State = "queued", Generation = 0 }; project.Base.Record = project.Base.Record with { State = "queued" }; }
        var service = Service(f, project, reader); var value = await service.GraphAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, default);
        Assert.Equal(state, value.Status.ProgressState); Assert.Null(value.Graph); Assert.Null(value.Status.Progress);
        using var handler = new Handler(_ => JsonResponse(JsonSerializer.SerializeToNode(value, PortalProgressArchiveJson.Options)!));
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        var reply = await client.ProgressGraphAsync(f.Job.ScanId.ToString()); Assert.Equal("scan-progress-graph", reply.Status);
        using var json = JsonDocument.Parse(reply.ToJson()); Assert.Equal(state, json.RootElement.GetProperty("data").GetProperty("status").GetProperty("progressState").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").GetProperty("graph").ValueKind); Assert.Equal(1, handler.Calls);
        var error = await Assert.ThrowsAsync<PortalFault>(() => service.CheckpointAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, new(new string('a', 64), 1), default));
        Assert.Equal(404, error.Status); Assert.Equal("CONTROL_PROGRESS_NOT_RETAINED", error.Code);
        Assert.Equal(state == "NotRetained" ? 2 : 0, reader.Calls); Assert.Equal(0, project.Base.CreateCalls);
    }

    [Theory]
    [InlineData("membership", false)]
    [InlineData("membership", true)]
    [InlineData("generation", false)]
    [InlineData("generation", true)]
    [InlineData("worker", false)]
    [InlineData("worker", true)]
    [InlineData("foreign", false)]
    [InlineData("foreign", true)]
    [InlineData("missingworker", false)]
    [InlineData("missingworker", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("storage", false)]
    [InlineData("storage", true)]
    [InlineData("membership-storage", false)]
    [InlineData("membership-storage", true)]
    [InlineData("payload-worker", false)]
    [InlineData("payload-worker", true)]
    public async Task EveryGraphAndByteReadRechecksAuthorityEvenAfterStorageFailure(string scenario, bool bytes)
    {
        using var f = new Files(); var journal = await f.Begin(); await journal.AppendAsync(f.Messages()[0], default);
        var snapshot = f.Read(); var prefix = f.Graph().Progress.PrefixSha256; var project = new Project(f);
        if (scenario == "payload-worker") snapshot = snapshot with { Summary = snapshot.Summary with { Header = snapshot.Summary.Header with { WorkerId = Guid.NewGuid() } } };
        var reader = new Reader(snapshot); using var stop = new CancellationTokenSource();
        reader.During = () =>
        {
            if (scenario.StartsWith("membership", StringComparison.Ordinal)) project.Base.WorkspaceHidden = true;
            if (scenario == "generation") project.Base.Job = project.Base.Job with { Generation = 2 };
            if (scenario == "worker") project.Worker = Guid.NewGuid();
            if (scenario == "cancel") stop.Cancel();
        };
        if (scenario.Contains("storage", StringComparison.Ordinal)) reader.Failure = new IOException("private-path-canary");
        if (scenario == "missingworker") project.Worker = null;
        var organization = scenario == "foreign" ? Guid.NewGuid() : f.Job.OrganizationId; var service = Service(f, project, reader);
        Func<Task> operation = bytes ? async () => { await service.CheckpointAsync(Actor, organization, f.Job.ScanId, new(prefix, 1), stop.Token); }
        : async () => { await service.GraphAsync(Actor, organization, f.Job.ScanId, stop.Token); };
        if (scenario == "cancel") await Assert.ThrowsAsync<OperationCanceledException>(operation);
        else
        {
            var error = await Assert.ThrowsAsync<PortalFault>(operation);
            Assert.Equal(scenario switch { "membership" or "membership-storage" or "foreign" => 404, "generation" or "worker" or "payload-worker" => 409, _ => 503 }, error.Status);
            Assert.Equal(scenario switch
            {
                "membership" or "membership-storage" or "foreign" => "CONTROL_WORKSPACE_NOT_FOUND",
                "generation" or "worker" => "CONTROL_PROGRESS_ATTEMPT_CHANGED",
                "payload-worker" => "CONTROL_PROGRESS_INVALID",
                "missingworker" => "CONTROL_PROGRESS_IDENTITY_UNCONFIRMED",
                _ => "CONTROL_PROGRESS_UNAVAILABLE"
            }, error.Code);
            Assert.DoesNotContain("private-path-canary", error.Message);
        }
        Assert.Equal(scenario is "foreign" or "missingworker" ? 0 : 1, reader.Calls);
    }

    [Fact]
    public async Task UncommittedWritesStayOutsideGraphAndCorruptionAfterRequestedCheckpointFailsClosed()
    {
        using var f = new Files(); var journal = await f.Begin(); var messages = f.Messages();
        await journal.AppendAsync(messages[0], default);
        File.WriteAllText(Path.Combine(f.Scope, "0002-" + new string('c', 64) + ".json.pending"), "unfinished");
        var graph = f.Graph(); Assert.True(graph.Progress.HasUncommittedWrite); Assert.Single(graph.Checkpoints); Assert.Empty(graph.Edges);
        File.Delete(Path.Combine(f.Scope, "0002-" + new string('c', 64) + ".json.pending")); await journal.AppendAsync(messages[1], default);
        var prefix = f.Graph().Progress.PrefixSha256;
        File.AppendAllText(Directory.GetFiles(f.Scope, "0002-*.json").Single(), " ");
        var error = await Assert.ThrowsAsync<PortalFault>(() => Service(f, new(f)).CheckpointAsync(Actor, f.Job.OrganizationId, f.Job.ScanId, new(prefix, 1), default));
        Assert.Equal(409, error.Status); Assert.Equal("CONTROL_PROGRESS_INVALID", error.Code);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("count")]
    [InlineData("duplicate-node")]
    [InlineData("seal")]
    [InlineData("phase")]
    [InlineData("observed")]
    [InlineData("zero-input")]
    [InlineData("replay")]
    public async Task GraphContractRejectsInventedTopologyInputAndAuthority(string change)
    {
        using var f = new Files(); var journal = await f.Begin(); foreach (var item in f.Messages().Take(2)) await journal.AppendAsync(item, default);
        var graph = f.Graph(); var edge = graph.Edges[0];
        graph = change switch
        {
            "schema" => graph with { Schema = "unrecognized" },
            "source" => graph with { Edges = [edge with { SourceObservationId = "absent" }] },
            "destination" => graph with { Edges = [edge with { ResultObservationId = "o1" }] },
            "count" => graph with { Checkpoints = [] },
            "duplicate-node" => graph with { Nodes = [graph.Nodes[0], graph.Nodes[0]] },
            "seal" => graph with { Checkpoints = graph.Checkpoints.SetItem(1, graph.Checkpoints[1] with { SourceSha256 = new string('b', 64) }) },
            "phase" => graph with { Checkpoints = graph.Checkpoints.SetItem(1, graph.Checkpoints[1] with { Phase = TraversalCheckpointPhase.Observed }) },
            "observed" => graph with { Edges = [edge with { ObservedSequence = 3 }] },
            "zero-input" => graph with { Edges = [edge with { CompletedInputCalls = 0 }] },
            "replay" => graph with { Progress = graph.Progress with { CanReplay = true } },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("WORKER_PROGRESS_INVALID", Assert.Throws<BrowserOperationException>(graph.Validate).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualChromiumPathAndFullCheckpointEventsSurviveHttpAndPrivateMcpReconnect(bool interrupted)
    {
        using var f = new Files(); var journal = await f.Begin(); var sequence = 0; byte[]? completed = null;
        async Task Checkpoint(TraversalCheckpoint cp, CancellationToken token)
        {
            await journal.AppendAsync(new(FixtureProgressProtocol.Schema, f.Native.Owner, ++sequence, cp), token);
            if (interrupted && cp.Phase == TraversalCheckpointPhase.Intent && cp.CommandIndex == 2) throw new IOException("deliberate retained-intent stop");
        }
        if (interrupted) await Assert.ThrowsAsync<BrowserOperationException>(() => KeyboardFixtureWorker.RecordAsync(f.Job, checkpoint: Checkpoint));
        else completed = await KeyboardFixtureWorker.RecordAsync(f.Job, checkpoint: Checkpoint);
        var project = new Project(f); var observations = new PortalObservationStub();
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, f.Root, new string('a', 40), 0), project, observations);
        using var http = PortalHttpTests.Authorized(server); using var response = await http.GetAsync(PathFor(f) + "/graph");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var value = (await response.Content.ReadFromJsonAsync<PortalProgressGraphResponse>(PortalProgressArchiveJson.Options))!; var graph = value.Graph!;
        Assert.Equal(interrupted ? 4 : 5, graph.Checkpoints.Length); Assert.Equal(2, graph.Edges.Length);
        Assert.Equal(1, graph.Edges[0].CompletedInputCalls); Assert.True(graph.Edges[0].InputMayHaveOccurred);
        Assert.False(graph.Progress.CanReplay); Assert.False(graph.Progress.IsCompletionReceipt); Assert.Equal("NotAssessed", graph.Progress.GoalVerification);
        if (interrupted) { Assert.Null(graph.Edges[1].ResultObservationId); Assert.Null(graph.Edges[1].CompletedInputCalls); Assert.Null(graph.Edges[1].ObservedSequence); }
        else
        {
            var recording = KeyboardRecordingBundle.Decode(completed!, f.Job).Validate(f.Job);
            Assert.Equal(TraversalReachability.Reached, recording.Receipt.Reachability);
            Assert.Equal(recording.Receipt.Observations.Select(o => o.Id), graph.Nodes.Select(n => n.ObservationId));
            Assert.Equal(VerificationStatus.Satisfied, graph.Edges[^1].Verification);
        }
        for (var connection = 0; connection < 2; connection++)
        {
            await using var mcp = await Connect(server.BaseAddress);
            var read = await mcp.CallToolAsync("scan_progress_graph", new Dictionary<string, object?> { ["scanId"] = f.Job.ScanId.ToString("D") });
            Assert.False(read.IsError ?? false); using var json = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(read.Content)).Text);
            Assert.Equal(graph.Progress.PrefixSha256, json.RootElement.GetProperty("data").GetProperty("graph").GetProperty("progress").GetProperty("prefixSha256").GetString());
            Assert.True(JsonElement.DeepEquals(json.RootElement, read.StructuredContent!.Value));
            var file = graph.Checkpoints[2]; using var bytes = new MemoryStream(); var offset = 0;
            do
            {
                var reply = await mcp.CallToolAsync("scan_checkpoint", new Dictionary<string, object?>
                {
                    ["scanId"] = f.Job.ScanId.ToString("D"),
                    ["prefixSha256"] = graph.Progress.PrefixSha256,
                    ["sequence"] = file.Sequence,
                    ["checkpointSha256"] = file.CheckpointSha256,
                    ["offset"] = offset
                });
                Assert.False(reply.IsError ?? false); var text = Assert.IsType<TextContentBlock>(Assert.Single(reply.Content)).Text;
                Assert.InRange(Encoding.UTF8.GetByteCount(text), 1, 131072); using var page = JsonDocument.Parse(text);
                var data = page.RootElement.GetProperty("data"); var chunk = data.GetProperty("data").GetBytesFromBase64(); bytes.Write(chunk);
                Assert.Equal(TraversalArchive.Hash(chunk), data.GetProperty("pageSha256").GetString());
                if (data.GetProperty("nextOffset").ValueKind == JsonValueKind.Null) break;
                offset = data.GetProperty("nextOffset").GetInt32();
            } while (true);
            Assert.Equal(file.CheckpointSha256, TraversalArchive.Hash(bytes.ToArray()));
            var checkpoint = JsonSerializer.Deserialize<TraversalCheckpoint>(bytes.ToArray(), ReaderWorkerWire.Json)!;
            Assert.Contains(checkpoint.Attempt!.Events, e => e.Kind == ReaderEventKind.InputCompleted);
            Assert.Equal("o1", checkpoint.Source.Id); Assert.Equal("o2", checkpoint.Result!.Id);
        }
        project.Base.Revoked = true;
        using var denied = await http.PostAsJsonAsync(PathFor(f) + "/checkpoint", new PortalCheckpointRequest(graph.Progress.PrefixSha256, 1));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Equal(0, project.Base.CreateCalls); Assert.Equal(0, observations.Calls);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("nested-worker")]
    [InlineData("invented-destination")]
    [InlineData("unknown-to-zero")]
    [InlineData("wrong-seal")]
    [InlineData("completion")]
    public async Task McpRejectsContradictoryGraphsWithoutRetryOrInput(string change)
    {
        using var f = new Files(); var journal = await f.Begin(); foreach (var message in f.Messages().Take(2)) await journal.AppendAsync(message, default);
        var value = JsonSerializer.SerializeToNode(Response(f, f.Graph()), PortalProgressArchiveJson.Options)!;
        switch (change)
        {
            case "scope": value["status"]!["scan"]!["id"] = Guid.NewGuid(); break;
            case "nested-worker": value["graph"]!["progress"]!["workerId"] = Guid.NewGuid(); break;
            case "invented-destination": value["graph"]!["edges"]![0]!["resultObservationId"] = "o1"; break;
            case "unknown-to-zero": value["graph"]!["edges"]![0]!["completedInputCalls"] = 0; break;
            case "wrong-seal": value["graph"]!["checkpoints"]![1]!["sourceSha256"] = new string('d', 64); break;
            case "completion": value["status"]!["progress"]!["isCompletionReceipt"] = true; break;
        }
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal(PathFor(f) + "/graph", request.RequestUri!.AbsolutePath);
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization!.Parameter);
            return JsonResponse(value);
        });
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        var reply = await client.ProgressGraphAsync(f.Job.ScanId.ToString());
        Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("prefix")]
    [InlineData("sequence")]
    [InlineData("export-hash")]
    [InlineData("page-hash")]
    [InlineData("short-page")]
    [InlineData("offset")]
    [InlineData("next-offset")]
    [InlineData("invented-goal")]
    public async Task McpRejectsAlteredCheckpointIdentityBytesAndPagination(string change)
    {
        using var f = new Files(); var journal = await f.Begin(); await journal.AppendAsync(f.Messages(40000)[0], default);
        var graph = f.Graph(); var file = graph.Checkpoints[0]; var prefix = graph.Progress.PrefixSha256;
        var page = RetainedProgressProjection.Checkpoint(f.Read(), f.Job, f.Worker, prefix, 1, 0);
        var value = JsonSerializer.SerializeToNode(page, PortalProgressArchiveJson.Options)!;
        switch (change)
        {
            case "scope": value["progress"]!["job"]!["organizationId"] = Guid.NewGuid(); break;
            case "prefix": value["progress"]!["prefixSha256"] = new string('d', 64); break;
            case "sequence": value["sequence"] = 2; break;
            case "export-hash": value["checkpointSha256"] = new string('d', 64); break;
            case "page-hash": value["pageSha256"] = new string('d', 64); break;
            case "short-page": value["data"] = Convert.ToBase64String(page.Data[..^1]); break;
            case "offset": value["offset"] = 16384; break;
            case "next-offset": value["nextOffset"] = 32768; break;
            case "invented-goal": value["progress"]!["goalVerification"] = "Reached"; break;
        }
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal(PathFor(f) + "/checkpoint", request.RequestUri!.AbsolutePath);
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization!.Parameter);
            using var input = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(new[] { "prefixSha256", "sequence", "offset" }, input.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal(prefix, input.RootElement.GetProperty("prefixSha256").GetString());
            return JsonResponse(value);
        });
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        Assert.Equal("HOSTED_RESPONSE_INVALID", (await client.CheckpointAsync(f.Job.ScanId.ToString(), prefix, 1, file.CheckpointSha256)).ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("prefix")]
    [InlineData("export")]
    [InlineData("sequence")]
    [InlineData("offset")]
    public async Task McpRejectsInvalidPageArgumentsBeforeNetwork(string change)
    {
        using var f = new Files(); using var handler = new Handler(_ => throw new InvalidOperationException("must not fetch"));
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        var reply = await client.CheckpointAsync(change == "scan" ? "bad" : f.Job.ScanId.ToString(), change == "prefix" ? "bad" : new string('a', 64),
            change == "sequence" ? 0 : 1, change == "export" ? "bad" : new string('b', 64), change == "offset" ? -1 : 0);
        Assert.Equal(change == "scan" ? "SCAN_ID_REQUIRED" : change is "prefix" or "export" ? "PROGRESS_IDENTITY_INVALID" : "PROGRESS_PAGE_INVALID", reply.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(503)]
    public async Task McpPreservesSafeUpstreamDenialsWithNoRetry(int status)
    {
        using var f = new Files(); using var handler = new Handler(_ => new((HttpStatusCode)status)
        { Content = new StringContent("{\"title\":\"CONTROL_PROGRESS_PREFIX_CHANGED\",\"detail\":\"private-canary\"}", Encoding.UTF8, "application/problem+json") });
        await using var client = new HostedScanClient(new(new("http://127.0.0.1:6086/"), f.Job.OrganizationId, PortalProjectClientTests.Bearer), handler);
        var reply = await client.ProgressGraphAsync(f.Job.ScanId.ToString()); Assert.Equal("CONTROL_PROGRESS_PREFIX_CHANGED", reply.ErrorCode);
        Assert.DoesNotContain("private-canary", reply.ToJson()); Assert.Contains("\"automaticRetry\":false", reply.ToJson()); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("unauthenticated")]
    [InlineData("unauthenticated-graph")]
    [InlineData("query")]
    [InlineData("worker-selector")]
    [InlineData("duplicate-prefix")]
    [InlineData("missing-sequence")]
    public async Task HttpRejectsAuthoritySelectorsAndMalformedRequestsBeforeEvidence(string scenario)
    {
        using var f = new Files(); var project = new Project(f); var reader = new Reader(null);
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, f.Root, new string('a', 40), 0), project,
            new PortalObservationStub(), progressSnapshotReader: reader);
        using var http = scenario.StartsWith("unauthenticated", StringComparison.Ordinal) ? new HttpClient { BaseAddress = server.BaseAddress } : PortalHttpTests.Authorized(server);
        var prefix = new string('a', 64); var body = new JsonObject { ["prefixSha256"] = prefix, ["sequence"] = 1 };
        if (scenario == "worker-selector") body["workerId"] = f.Worker.ToString();
        if (scenario == "missing-sequence") body.Remove("sequence");
        var text = scenario == "duplicate-prefix" ? "{\"prefixSha256\":\"" + prefix + "\",\"prefixSha256\":\"" + prefix + "\",\"sequence\":1}" : body.ToJsonString();
        using var content = new StringContent(text, Encoding.UTF8, "application/json");
        using var response = scenario == "unauthenticated-graph" ? await http.GetAsync(PathFor(f) + "/graph")
            : scenario == "query" ? await http.GetAsync(PathFor(f) + "/graph?workerId=untrusted")
            : await http.PostAsync(PathFor(f) + "/checkpoint", content);
        Assert.Equal(scenario.StartsWith("unauthenticated", StringComparison.Ordinal) ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore); Assert.Equal(0, reader.Calls); Assert.Equal(0, project.Base.DataCalls);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(send(request)); }
    }
    private static HttpResponseMessage JsonResponse(JsonNode value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json"), Headers = { CacheControl = new() { NoStore = true } } };

    private static Task<McpClient> Connect(Uri origin)
    {
        var assembly = typeof(RetainedProgressGraphTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "retained checkpoint integration",
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HSR_MCP_API_ORIGIN"] = origin.ToString(),
                ["HSR_MCP_ORGANIZATION"] = PortalProjectClientTests.Organization.ToString(),
                ["HSR_MCP_ACCESS_TOKEN"] = PortalProjectClientTests.Bearer
            }
        }));
    }
}
