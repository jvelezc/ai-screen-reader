using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PortalReportTests
{
    private static readonly PortalIdentity Actor = new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static Task<PortalReportResponse> Report(PortalProjectStub project, IPortalObservationReader reader) =>
        new PortalScanService(project, reader).ReportAsync(Actor, PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, default);

    [Theory]
    [InlineData("queued", "queued", "Pending")]
    [InlineData("leased", "leased", "Pending")]
    [InlineData("blocked", "blocked", "NotCollected")]
    public async Task IncompleteExecutionCannotReadEvidenceOrInventAccessibilityJudgment(string scanState, string jobState, string evidence)
    {
        var project = new PortalProjectStub(); var reader = new PortalObservationStub();
        project.Record = project.Record with { State = scanState };
        project.Job = project.Job with { State = jobState, ErrorCode = jobState == "blocked" ? "WORKER_RUNTIME_UNCONFIRMED" : null };
        var result = await Report(project, reader);
        Assert.Equal(evidence, result.EvidenceState); Assert.Null(result.Observation); Assert.Equal(0, reader.Calls);
        Assert.Equal(project.Job.ErrorCode, result.ErrorCode);
    }

    [Theory]
    [InlineData("scan-tenant")]
    [InlineData("scan-id")]
    [InlineData("job-tenant")]
    [InlineData("job-scan")]
    [InlineData("unknown-state")]
    [InlineData("inconsistent-state")]
    public async Task UnconfirmedProviderScopeOrStateNeverOpensEvidence(string scenario)
    {
        var project = new PortalProjectStub(); var reader = new PortalObservationStub();
        switch (scenario)
        {
            case "scan-tenant": project.Record = project.Record with { OrganizationId = Guid.NewGuid() }; break;
            case "scan-id": project.Record = project.Record with { Id = Guid.NewGuid() }; break;
            case "job-tenant": project.Job = project.Job with { OrganizationId = Guid.NewGuid() }; break;
            case "job-scan": project.Job = project.Job with { ScanId = Guid.NewGuid() }; break;
            case "unknown-state": project.Job = project.Job with { State = "unknown" }; break;
            case "inconsistent-state": project.Job = project.Job with { State = "completed" }; break;
        }
        var error = await Assert.ThrowsAsync<PortalFault>(() => Report(project, reader));
        Assert.Equal(503, error.Status); Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task ReadsAcrossAtomicCompletionRefreshMetadataOnceBeforeOpeningEvidence()
    {
        var project = Completed(); var reader = new PortalObservationStub();
        project.ScanResult = call => call == 1 ? project.Record with { State = "leased" } : project.Record;
        var result = await Report(project, reader);
        Assert.Equal("observed", result.Scan.State); Assert.Equal("Unavailable", result.EvidenceState);
        Assert.Equal(2, project.ScanCalls); Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task MissingJobAndAccessRemovedDuringRefreshCannotOpenEvidence()
    {
        var project = Completed(); var reader = new PortalObservationStub(); project.MissingJob = true;
        var missing = await Report(project, reader);
        Assert.Equal("Unavailable", missing.EvidenceState); Assert.Equal("CONTROL_JOB_NOT_FOUND", missing.ErrorCode);
        Assert.Equal(0, reader.Calls);
        project.MissingJob = false;
        project.ScanResult = call => call == 2 ? project.Record with { State = "leased" } : null;
        var revoked = await Assert.ThrowsAsync<PortalFault>(() => Report(project, reader));
        Assert.Equal(404, revoked.Status); Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task MissingAndInvalidStoredEvidenceRemainDifferentFromCompletedExecution()
    {
        var project = Completed(); var reader = new PortalObservationStub();
        var missing = await Report(project, reader);
        Assert.Equal("observed", missing.Scan.State); Assert.Equal("Unavailable", missing.EvidenceState);
        Assert.Null(missing.Observation); Assert.Equal("CONTROL_EVIDENCE_UNAVAILABLE", missing.ErrorCode);
        var invalid = await Report(project, new RejectingReader());
        Assert.Equal("observed", invalid.Scan.State); Assert.Equal("Invalid", invalid.EvidenceState);
        Assert.Null(invalid.Observation); Assert.Equal("CONTROL_EVIDENCE_UNCONFIRMED", invalid.ErrorCode);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("reference")]
    [InlineData("tenant")]
    [InlineData("generation")]
    [InlineData("input-claim")]
    [InlineData("goal-claim")]
    [InlineData("shutdown")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    public async Task ExactReferenceHashIdentityAndNativeShutdownAreRequiredBeforeRendering(string scenario)
    {
        using var files = new EvidenceFiles(); var job = files.Job;
        var receipt = FixtureWorkerTests.Receipt(job);
        receipt = scenario switch
        {
            "tenant" => receipt with { Job = job with { OrganizationId = Guid.NewGuid() } },
            "input-claim" => receipt with { DispatchedInputs = 1 },
            "goal-claim" => receipt with { GoalVerified = true },
            "shutdown" => receipt with { Shutdown = receipt.Shutdown with { ProcessExitConfirmationMilliseconds = 4000 } },
            _ => receipt
        };
        var bytes = scenario == "malformed" ? "{}"u8.ToArray()
            : scenario == "oversized" ? new byte[FixtureObservation.MaximumBytes + 1]
            : JsonSerializer.SerializeToUtf8Bytes(receipt, ObservationStore.Json);
        var metadata = await files.Write(bytes);
        metadata = scenario switch
        {
            "digest" => metadata with { Digest = new string('a', 64) },
            "reference" => metadata with { Reference = "../../unrelated.json" },
            "generation" => metadata with { Generation = 4 },
            _ => metadata
        };
        var error = await Assert.ThrowsAsync<PortalFault>(() => new PortalObservationReader(files.Root).ReadAsync(metadata, default));
        Assert.Equal(503, error.Status);
        Assert.StartsWith("CONTROL_EVIDENCE_", error.Code);
    }

    [Fact]
    public async Task MissingFileAndCancellationAreNotSuccessfulEvidence()
    {
        using var files = new EvidenceFiles();
        Assert.Null(await new PortalObservationReader(files.Root).ReadAsync(files.Metadata(new string('a', 64)), default));
        var metadata = await files.Write(JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(files.Job), ObservationStore.Json));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PortalObservationReader(files.Root).ReadAsync(metadata, cancellation.Token));
    }

    [Fact]
    public async Task RealChromiumObservationSurvivesPrivateStorageHashAndAuthenticatedHttpReport()
    {
        using var files = new EvidenceFiles(new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1));
        var observation = await FixtureWorker.ObserveAsync(files.Job);
        var stored = await new ObservationStore(files.Root).PersistAsync(JsonSerializer.SerializeToUtf8Bytes(observation, ObservationStore.Json), files.Job, default);
        var project = Completed(); project.Job = files.Metadata(stored.Sha256);
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, files.Root, new string('a', 40), 0),
            project, new PortalObservationReader(files.Root));
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.GetAsync(PortalHttpTests.Prefix + "/" + files.Job.ScanId.ToString("D"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var report = JsonSerializer.Deserialize<PortalReportResponse>(body, PortalJson.Options)!;
        Assert.Equal("Available", report.EvidenceState); Assert.NotNull(report.Observation);
        Assert.Equal(stored.Sha256, report.Observation.Digest);
        Assert.Contains(report.Observation.Nodes, node => node.Role == "heading" && node.Name == "Synthetic shop");
        Assert.Equal(observation.Snapshot.Nodes.Select(node => (node.Id, node.Required?.Value, node.Required?.Source.ToString())),
            report.Observation.Nodes.Select(node => (node.Id, node.Required, node.RequiredSource)));
        Assert.Contains(report.Observation.Nodes, node => node.Required is null);
        Assert.False(report.Observation.GoalVerified); Assert.Equal(0, report.Observation.DispatchedInputs);
        Assert.DoesNotContain("traversal-account-canary", body); Assert.DoesNotContain("traversal-secret-canary", body);
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, body);
        Assert.True(observation.Shutdown.ProcessExitConfirmed);
        Assert.NotNull(observation.Shutdown.ProcessExitConfirmationMilliseconds);
        Assert.InRange(observation.Shutdown.ProcessExitConfirmationMilliseconds.Value, 0, 3999);
    }

    private static PortalProjectStub Completed()
    {
        var project = new PortalProjectStub();
        project.Record = project.Record with { State = "observed" };
        project.Job = project.Job with { State = "completed", Generation = 1 };
        return project;
    }
    private sealed class RejectingReader : IPortalObservationReader
    {
        public Task<PortalObservationResponse?> ReadAsync(PortalJobResult job, CancellationToken token) =>
            throw new PortalFault(503, "CONTROL_EVIDENCE_UNCONFIRMED");
    }
    private sealed class EvidenceFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-portal-tests-" + Guid.NewGuid().ToString("N"));
        public FixtureJob Job { get; }
        public EvidenceFiles(FixtureJob? job = null) { Job = job ?? FixtureWorkerTests.Job(); Directory.CreateDirectory(Root); }
        public PortalJobResult Metadata(string digest) => new(Job.OrganizationId, Job.ScanId, Job.JobId, "completed", Job.Generation, digest, Job.Reference, null);
        public async Task<PortalJobResult> Write(byte[] bytes)
        {
            var path = Path.Combine(Root, Job.Reference.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, bytes);
            return Metadata(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
