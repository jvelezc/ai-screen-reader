using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Worker;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class HostedScanChromiumTests
{
    [Fact]
    public async Task AdmittedKeyboardGraphOriginalSurvivesHttpAndStdioReconnectWithoutPromotingUnknown()
    {
        // Synthetic provider identity; actual Chromium, original media, archive and protocols.
        var root = Path.Combine(Path.GetTempPath(), "hsr-keyboard-mcp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = new PortalProjectStub();
            var job = new HeadlessScreenReader.Domain.FixtureJob(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1)
            { RecordObservation = true, TraversalRoute = 3 };
            var archive = new TraversalArchive(new FileTraversalObjectStore(Path.Combine(root, "archive")));
            await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, root, new string('a', 40), 0), project, new PortalObservationReader(root), traversalArchive: archive);
            await using (var first = await Connect(server.BaseAddress))
            {
                var admission = (await Call(first, "scan_start_fixture", new() { ["requestId"] = Guid.NewGuid(), ["traversalRoute"] = 3 })).GetProperty("data");
                Assert.Equal("KeyboardTraversal", admission.GetProperty("recordingRequested").GetString()); Assert.Equal(3, project.LastTraversalRoute);
                Assert.Equal("NotEstablishedByAdmission", admission.GetProperty("evidence").GetString());
            }
            var encoded = await KeyboardFixtureWorker.RecordAsync(job); var original = KeyboardRecordingBundle.Decode(encoded, job);
            var stored = await new ObservationStore(root, producer: new(archive)).PersistAsync(encoded, job, default, Guid.NewGuid());
            project.Record = project.Record with { State = "observed" };
            project.Job = new(job.OrganizationId, job.ScanId, job.JobId, "completed", 1, stored.Sha256, job.Reference, null)
            { RecordObservation = true, TraversalRoute = 3, WorkerArchive = stored.WorkerArchive };
            await using var reconnected = await Connect(server.BaseAddress);
            var report = (await Call(reconnected, "scan_report", new() { ["scanId"] = job.ScanId })).GetProperty("data");
            Assert.StartsWith("PreTraversalBaseline", report.GetProperty("observationScope").GetString());
            Assert.Equal(0, report.GetProperty("observation").GetProperty("dispatchedInputs").GetInt32());
            var identity = stored.WorkerArchive!.Receipt.Identity;
            var args = new Dictionary<string, object?> { ["archiveId"] = identity.ArchiveId, ["indexSha256"] = identity.IndexSha256, ["path"] = "traversal.json", ["fileSha256"] = TraversalArchive.Hash(original.Traversal) };
            using var restored = new MemoryStream(); long? offset = 0;
            do
            {
                args["offset"] = offset.Value; var response = (await Call(reconnected, "archive_file", args)).GetProperty("data");
                var page = response.GetProperty("page"); var bytes = page.GetProperty("data").GetBytesFromBase64();
                Assert.Equal(TraversalArchive.Hash(bytes), response.GetProperty("pageSha256").GetString()); restored.Write(bytes);
                offset = page.GetProperty("nextOffset").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextOffset").GetInt64();
            } while (offset is not null);
            Assert.Equal(original.Traversal, restored.ToArray());
            var route = original.Validate(job); Assert.Equal(HeadlessScreenReader.Domain.TraversalReachability.Unknown, route.Receipt.Reachability);
            Assert.Equal(3, route.Receipt.CompletedKeystrokes); Assert.Equal(3, route.Graph.Edges.Length);
            project.Revoked = true;
            Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", (await Call(reconnected, "archive_file", args, true)).GetProperty("errorCode").GetString());
        }
        finally
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(root)));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PassiveOriginalVideoSurvivesAuthorizedHttpAndStdioReconnectWithoutInventingTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "hsr-passive-mcp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = new PortalProjectStub();
            var job = new HeadlessScreenReader.Domain.FixtureJob(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1)
            { RecordObservation = true };
            var store = new FileTraversalObjectStore(Path.Combine(root, "archives")); var archive = new TraversalArchive(store);
            await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, root, new string('a', 40), 0), project,
                new PortalObservationReader(root), traversalArchive: archive);
            await using (var first = await Connect(server.BaseAddress))
            {
                var reply = (await Call(first, "scan_start_fixture", new() { ["requestId"] = Guid.NewGuid(), ["recordObservation"] = true })).GetProperty("data");
                Assert.Equal("PassiveObservation", reply.GetProperty("recordingRequested").GetString()); Assert.Equal(1, project.RecordedCreateCalls);
            }
            var collected = await FixtureWorker.RecordAsync(job); var bundle = PassiveRecordingBundle.Decode(collected, job);
            var stored = await new ObservationStore(root, producer: new(archive)).PersistAsync(collected, job, default, Guid.NewGuid());
            project.Record = project.Record with { State = "observed" };
            project.Job = new(job.OrganizationId, job.ScanId, job.JobId, "completed", 1, stored.Sha256, job.Reference, null)
            { RecordObservation = true, WorkerArchive = stored.WorkerArchive };
            await using var reconnected = await Connect(server.BaseAddress);
            var report = (await Call(reconnected, "scan_report", new() { ["scanId"] = job.ScanId })).GetProperty("data");
            Assert.Equal("PassiveObservation", report.GetProperty("workerArchive").GetProperty("recording").GetString());
            Assert.Equal(0, report.GetProperty("observation").GetProperty("dispatchedInputs").GetInt32()); Assert.False(report.GetProperty("observation").GetProperty("goalVerified").GetBoolean());
            var identity = stored.WorkerArchive!.Receipt.Identity;
            var arguments = new Dictionary<string, object?> { ["archiveId"] = identity.ArchiveId, ["indexSha256"] = identity.IndexSha256 };
            var inventory = (await Call(reconnected, "archive_manifest", arguments)).GetProperty("data");
            Assert.Equal(4, inventory.GetProperty("totalFiles").GetInt32());
            var original = inventory.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("path").GetString() == "original.webm");
            arguments["path"] = "original.webm"; arguments["fileSha256"] = original.GetProperty("sha256").GetString();
            using var restored = new MemoryStream(); long offset = 0;
            do
            {
                arguments["offset"] = offset; var reply = (await Call(reconnected, "archive_file", arguments)).GetProperty("data");
                var page = reply.GetProperty("page"); var bytes = page.GetProperty("data").GetBytesFromBase64();
                Assert.Equal(TraversalArchive.Hash(bytes), reply.GetProperty("pageSha256").GetString()); restored.Write(bytes);
                if (page.GetProperty("nextOffset").ValueKind == JsonValueKind.Null) break;
                offset = page.GetProperty("nextOffset").GetInt64();
            } while (true);
            Assert.Equal(bundle.Video, restored.ToArray()); Assert.Single(bundle.Validate(job).States); Assert.Empty(bundle.Validate(job).Edges);
            project.Revoked = true;
            Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", (await Call(reconnected, "archive_file", arguments, error: true)).GetProperty("errorCode").GetString());
        }
        finally
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(root)));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ScanAuthorityFailureAndClientDisposalDoNotQuarantineLiveReader()
    {
        var project = new PortalProjectStub(); await using var server = await PortalHttpTests.Start(project);
        var options = new HeadlessScreenReader.Mcp.HostedReaderOptions(server.BaseAddress, PortalProjectClientTests.Organization, PortalProjectClientTests.Bearer);
        await using var reader = new HeadlessScreenReader.Mcp.HostedReaderClient(options);
        await using var scans = new HeadlessScreenReader.Mcp.HostedScanClient(options);
        var opened = await reader.OpenAsync(); var id = opened.SessionId!;
        using var first = JsonDocument.Parse((await reader.SnapshotAsync(id)).ToJson());
        var doc = first.RootElement.GetProperty("data").GetProperty("documentId").GetString()!;
        project.Revoked = true; Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", (await scans.ListAsync()).ErrorCode); project.Revoked = false;
        Assert.Equal("step", (await reader.ExecuteAsync(id, doc, "NextHeading")).Status);
        await scans.DisposeAsync(); Assert.Equal("observed", (await reader.SnapshotAsync(id)).Status);
        Assert.Equal("closed", (await reader.CloseAsync(id)).Status); Assert.True(reader.CleanupConfirmed);
    }

    [Fact]
    public async Task StdioScanWorkflowReadsActualChromiumEvidenceAcrossReconnectAndRejectsCorruption()
    {
        // Synthetic API identity, actual source bytes and browser. No provider JWT/S3 claim.
        var root = Path.Combine(Path.GetTempPath(), "hsr-mcp-scan-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var project = new PortalProjectStub { HistoryCount = 0 };
            await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, root, new string('a', 40), 0), project, new PortalObservationReader(root));
            var requestId = Guid.NewGuid(); string scan;
            await using (var first = await Connect(server.BaseAddress))
            {
                Assert.Empty((await Call(first, "scan_list")).GetProperty("data").GetProperty("items").EnumerateArray());
                var admitted = (await Call(first, "scan_start_fixture", new() { ["requestId"] = requestId })).GetProperty("data");
                scan = admitted.GetProperty("scanId").GetString()!;
                Assert.Equal("NotEstablishedByAdmission", admitted.GetProperty("execution").GetString());
                Assert.Equal("NotEstablishedByAdmission", admitted.GetProperty("evidence").GetString());
                var retry = (await Call(first, "scan_start_fixture", new() { ["requestId"] = requestId })).GetProperty("data");
                Assert.Equal(scan, retry.GetProperty("scanId").GetString()); Assert.Equal(requestId, project.LastRequest); Assert.Equal(2, project.CreateCalls);
                project.HistoryCount = 1;
                var pending = (await Call(first, "scan_report", new() { ["scanId"] = scan })).GetProperty("data");
                Assert.Equal("Pending", pending.GetProperty("evidenceState").GetString()); Assert.Equal(JsonValueKind.Null, pending.GetProperty("observation").ValueKind);
            }

            var job = new HeadlessScreenReader.Domain.FixtureJob(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1);
            var original = await FixtureWorker.ObserveAsync(job);
            Assert.True(original.Shutdown.ProcessExitConfirmed); Assert.Equal(0, original.DispatchedInputs); Assert.False(original.GoalVerified);
            var producerObjects = new FileTraversalObjectStore(Path.Combine(root, "archives"));
            var stored = await new ObservationStore(root, producer: new(new(producerObjects))).PersistAsync(
                JsonSerializer.SerializeToUtf8Bytes(original, ObservationStore.Json), job, default, Guid.NewGuid());
            project.Job = new(job.OrganizationId, job.ScanId, job.JobId, "completed", job.Generation, stored.Sha256, job.Reference, null)
            { WorkerArchive = stored.WorkerArchive };
            project.Record = project.Record with { State = "observed" };

            await using var second = await Connect(server.BaseAddress);
            var listed = (await Call(second, "scan_list")).GetProperty("data");
            Assert.Equal(Guid.Parse(scan), Assert.Single(listed.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            var page = (await Call(second, "scan_report", new() { ["scanId"] = scan, ["limit"] = 2 })).GetProperty("data");
            Assert.Equal("Available", page.GetProperty("evidenceState").GetString()); var observation = page.GetProperty("observation");
            Assert.Equal(job.JobId, page.GetProperty("workerArchive").GetProperty("job").GetProperty("jobId").GetGuid());
            Assert.Equal(stored.WorkerArchive!.Receipt.Identity.IndexSha256,
                page.GetProperty("workerArchive").GetProperty("receipt").GetProperty("identity").GetProperty("indexSha256").GetString());
            Assert.Equal(stored.Sha256, observation.GetProperty("digest").GetString()); Assert.Equal("Local", observation.GetProperty("evidenceStorage").GetString());
            var reportId = page.GetProperty("reportId").GetString(); var ids = new List<string?>();
            while (true)
            {
                Assert.Equal(reportId, page.GetProperty("reportId").GetString());
                var payload = page.GetRawText(); Assert.DoesNotContain("traversal-account-canary", payload); Assert.DoesNotContain("traversal-secret-canary", payload);
                ids.AddRange(page.GetProperty("observation").GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("id").GetString()));
                if (!page.GetProperty("hasMore").GetBoolean()) break;
                page = (await Call(second, "scan_report", new() { ["scanId"] = scan, ["offset"] = page.GetProperty("nextOffset").GetInt32(), ["limit"] = 5, ["reportId"] = reportId })).GetProperty("data");
            }
            Assert.Equal(original.Snapshot.Nodes.Select(n => n.Id), ids); Assert.Equal(ids.Count, page.GetProperty("totalNodes").GetInt32());
            Assert.All(project.SeenBearers, bearer => Assert.Equal(PortalProjectClientTests.Bearer, bearer));
            project.Revoked = true;
            var denied = await Call(second, "scan_report", new() { ["scanId"] = scan, ["offset"] = 2, ["reportId"] = reportId }, true);
            Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", denied.GetProperty("errorCode").GetString()); Assert.DoesNotContain("Synthetic shop", denied.GetRawText());
            project.Revoked = false;
            var path = Path.Combine(root, job.Reference.Replace('/', Path.DirectorySeparatorChar)); await File.WriteAllTextAsync(path, "corrupt owned observation");
            var invalid = await Call(second, "scan_report", new() { ["scanId"] = scan }, true);
            Assert.Equal("HOSTED_SCAN_EVIDENCE_INVALID", invalid.GetProperty("errorCode").GetString());
            Assert.Equal("observed", invalid.GetProperty("data").GetProperty("scan").GetProperty("state").GetString());
            Assert.Equal("Invalid", invalid.GetProperty("data").GetProperty("evidenceState").GetString());
            Assert.Equal(JsonValueKind.Null, invalid.GetProperty("data").GetProperty("observation").ValueKind);
        }
        finally
        {
            var resolved = Path.GetFullPath(root); var temporary = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("hsr-mcp-scan-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test root");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static Task<McpClient> Connect(Uri origin)
    {
        var assembly = typeof(HostedScanChromiumTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "private core scan test",
            EnvironmentVariables = new Dictionary<string, string?> { ["HSR_MCP_API_ORIGIN"] = origin.ToString(), ["HSR_MCP_ORGANIZATION"] = PortalProjectClientTests.Organization.ToString(), ["HSR_MCP_ACCESS_TOKEN"] = PortalProjectClientTests.Bearer }
        }));
    }
    private static async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?>? arguments = null, bool error = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var reply = await client.CallToolAsync(tool, arguments, cancellationToken: deadline.Token);
        using var parsed = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(reply.Content)).Text);
        Assert.True(error == (reply.IsError ?? false), $"{tool}: {parsed.RootElement.GetRawText()}");
        Assert.True(JsonElement.DeepEquals(parsed.RootElement, reply.StructuredContent!.Value));
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(parsed.RootElement.GetRawText()), 1, 131072);
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, parsed.RootElement.GetRawText()); return parsed.RootElement.Clone();
    }
}
