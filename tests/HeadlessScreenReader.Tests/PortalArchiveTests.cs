using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PortalArchiveTests
{
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-archive-report-" + Guid.NewGuid().ToString("N"));
        public FixtureJob Job { get; } = new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1);
        public byte[] Bytes { get; }
        public PortalJobResult Metadata { get; }
        public Files()
        {
            Directory.CreateDirectory(Root);
            Bytes = JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(Job), ObservationStore.Json);
            Metadata = new(Job.OrganizationId, Job.ScanId, Job.JobId, "completed", 1,
                Convert.ToHexStringLower(SHA256.HashData(Bytes)), Job.Reference, null);
        }
        public async Task Save()
        {
            var path = Path.Combine(Root, Job.Reference.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, Bytes);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private sealed class Archive : IObservationArchive
    {
        public int Calls { get; private set; }
        public FixtureJob? SeenJob { get; private set; }
        public string? SeenDigest { get; private set; }
        public Func<CancellationToken, Task<byte[]?>> Read { get; set; } = _ => Task.FromResult<byte[]?>(null);
        public Task FinalizeAsync(FixtureJob job, byte[] bytes, string digest, CancellationToken token) => throw new InvalidOperationException("Report cannot write objects");
        public Task<byte[]?> ReadAsync(FixtureJob job, string digest, CancellationToken token)
        { Calls++; SeenJob = job; SeenDigest = digest; return Read(token); }
    }
    private static PortalProjectStub Project(Files files) => new()
    { Job = files.Metadata, Record = new(files.Job.OrganizationId, files.Job.ScanId, "synthetic-shop", "hsr-desktop-reader-v13", "observed", DateTimeOffset.UtcNow) };

    [Fact]
    public async Task VerifiedRemoteEvidenceIsReadableWithoutTheLocalFileAndPreservesReaderFacts()
    {
        using var files = new Files(); var archive = new Archive { Read = _ => Task.FromResult<byte[]?>(files.Bytes) };
        var result = await new PortalObservationReader(files.Root, archive).ReadAsync(files.Metadata, default);
        Assert.NotNull(result); Assert.Equal("S3", result.EvidenceStorage); Assert.Equal(files.Metadata.Digest, result.Digest);
        Assert.Equal(files.Job, archive.SeenJob); Assert.Equal(files.Metadata.Digest, archive.SeenDigest);
        Assert.Equal(1, archive.Calls); Assert.Equal(0, result.DispatchedInputs); Assert.False(result.GoalVerified);
        Assert.Equal(FixtureWorkerTests.Receipt(files.Job).Snapshot.Nodes.Select(node => (node.Id, node.Name, node.Required?.Value)),
            result.Nodes.Select(node => (node.Id, node.Name, node.Required)));
        Assert.Empty(Directory.EnumerateFiles(files.Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OnlyAnActuallyMissingObjectAllowsExplicitLocalFallback()
    {
        using var files = new Files(); await files.Save(); var archive = new Archive();
        var fallback = await new PortalObservationReader(files.Root, archive).ReadAsync(files.Metadata, default);
        Assert.NotNull(fallback); Assert.Equal("LocalFallback", fallback.EvidenceStorage); Assert.Equal(1, archive.Calls);
        var local = await new PortalObservationReader(files.Root).ReadAsync(files.Metadata, default);
        Assert.NotNull(local); Assert.Equal("Local", local.EvidenceStorage); Assert.Equal(fallback.Digest, local.Digest);
    }

    [Theory]
    [InlineData(ObservationArchiveFailure.Unavailable, "Unavailable", "CONTROL_EVIDENCE_REMOTE_UNAVAILABLE")]
    [InlineData(ObservationArchiveFailure.WriteUnconfirmed, "Unavailable", "CONTROL_EVIDENCE_REMOTE_UNAVAILABLE")]
    [InlineData(ObservationArchiveFailure.Invalid, "Invalid", "CONTROL_EVIDENCE_UNCONFIRMED")]
    public async Task RemoteFailureNeverSilentlyFallsBackToAValidLocalCopy(ObservationArchiveFailure failure, string state, string code)
    {
        using var files = new Files(); await files.Save(); var archive = new Archive { Read = _ => throw new ObservationArchiveException(failure) };
        var result = await new PortalScanService(Project(files), new PortalObservationReader(files.Root, archive)).ReportAsync(
            new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer), files.Job.OrganizationId, files.Job.ScanId, default);
        Assert.Equal("observed", result.Scan.State); Assert.Equal(state, result.EvidenceState);
        Assert.Equal(code, result.ErrorCode); Assert.Null(result.Observation); Assert.Equal(1, archive.Calls);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("scope")]
    [InlineData("goal")]
    [InlineData("input")]
    [InlineData("shutdown")]
    [InlineData("oversized")]
    public async Task PortalIndependentlyValidatesRemoteHashScopeAndNativeFacts(string scenario)
    {
        using var files = new Files(); await files.Save();
        var observation = FixtureWorkerTests.Receipt(files.Job);
        observation = scenario switch
        {
            "scope" => observation with { Job = files.Job with { Generation = 2 } },
            "goal" => observation with { GoalVerified = true },
            "input" => observation with { DispatchedInputs = 1 },
            "shutdown" => observation with { Shutdown = observation.Shutdown with { ProcessExitConfirmationMilliseconds = 4000 } },
            _ => observation
        };
        var bytes = scenario == "oversized" ? new byte[FixtureObservation.MaximumBytes + 1]
            : JsonSerializer.SerializeToUtf8Bytes(observation, ObservationStore.Json);
        if (scenario == "hash") bytes[0] ^= 1;
        var metadata = scenario == "hash" ? files.Metadata : files.Metadata with { Digest = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
        var archive = new Archive { Read = _ => Task.FromResult<byte[]?>(bytes) };
        var error = await Assert.ThrowsAsync<PortalFault>(() => new PortalObservationReader(files.Root, archive).ReadAsync(metadata, default));
        Assert.Equal("CONTROL_EVIDENCE_UNCONFIRMED", error.Code); Assert.Equal(503, error.Status); Assert.Equal(1, archive.Calls);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("digest")]
    [InlineData("generation")]
    [InlineData("state")]
    public async Task InvalidMetadataCannotReachObjectStorage(string scenario)
    {
        using var files = new Files(); var archive = new Archive();
        var metadata = scenario switch
        {
            "reference" => files.Metadata with { Reference = "../../secret" },
            "digest" => files.Metadata with { Digest = new string('A', 64) },
            "generation" => files.Metadata with { Generation = 0 },
            _ => files.Metadata with { State = "leased" }
        };
        var error = await Assert.ThrowsAsync<PortalFault>(() => new PortalObservationReader(files.Root, archive).ReadAsync(metadata, default));
        Assert.Equal("CONTROL_EVIDENCE_IDENTITY_UNCONFIRMED", error.Code); Assert.Equal(0, archive.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedIdentityOrRlsHiddenScanCannotReadRemoteEvidence(bool hidden)
    {
        using var files = new Files(); var archive = new Archive(); var project = Project(files);
        project.Revoked = !hidden;
        if (hidden) project.ScanResult = _ => null;
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, files.Root, new string('a', 40), 0),
            project, new PortalObservationReader(files.Root, archive));
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.GetAsync(PortalHttpTests.Prefix + "/" + files.Job.ScanId.ToString("D"));
        Assert.Equal(hidden ? System.Net.HttpStatusCode.NotFound : System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, archive.Calls);
    }

    [Fact]
    public async Task CancellationReachesRemoteReadAndDoesNotInventMissingEvidence()
    {
        using var files = new Files(); using var stop = new CancellationTokenSource(); stop.Cancel();
        var archive = new Archive { Read = token => Task.FromCanceled<byte[]?>(token) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PortalObservationReader(files.Root, archive).ReadAsync(files.Metadata, stop.Token));
        Assert.Equal(1, archive.Calls);
    }
}
