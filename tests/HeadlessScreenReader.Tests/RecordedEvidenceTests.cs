using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class RecordedEvidenceTests
{
    private static readonly FixtureJob Job = new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1)
    { RecordObservation = true, TraversalRoute = 1 };
    private static readonly PortalIdentity Actor = new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static readonly Lazy<Task<byte[]>> Original = new(() => KeyboardFixtureWorker.RecordAsync(Job));

    private static async Task<(PortalProjectStub Project, TraversalArchiveTests.Objects Store, TraversalArchive Archive)> Setup(bool legacy = false)
    {
        var store = new TraversalArchiveTests.Objects(); var archive = new TraversalArchive(store);
        var root = Path.Combine(Path.GetTempPath(), "hsr-source-" + Guid.NewGuid().ToString("N"));
        try
        {
            var encoded = await Original.Value;
            if (legacy)
            {
                var bundle = KeyboardRecordingBundle.Decode(encoded, Job);
                var baseline = ObservationStore.Validate(bundle.Observation, Job) with { RecordedSnapshots = default };
                var baselineBytes = JsonSerializer.SerializeToUtf8Bytes(baseline, ObservationStore.Json);
                var metadata = bundle.Validate(Job) with { Environment = null, BaselineSha256 = TraversalArchive.Hash(baselineBytes) };
                encoded = (bundle with { Observation = baselineBytes, Traversal = JsonSerializer.SerializeToUtf8Bytes(metadata, ObservationStore.Json) }).Encode(Job);
            }
            var stored = await new ObservationStore(root, producer: new(archive)).PersistAsync(encoded, Job, default, Guid.NewGuid());
            var project = new PortalProjectStub(); project.Record = project.Record with { State = "observed" };
            project.Job = new(Job.OrganizationId, Job.ScanId, Job.JobId, "completed", 1, stored.Sha256, Job.Reference, null)
            { RecordObservation = true, TraversalRoute = 1, WorkerArchive = stored.WorkerArchive };
            return (project, store, archive);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OlderOriginalsRetainMissingCollectorContextAndBaselineOnlyAxWithoutFabricatingCompleteness()
    {
        var (project, _, archive) = await Setup(legacy: true);
        var result = await new PortalEvidenceReadiness(project, archive).AssessAsync(Actor, Job.OrganizationId, new(AdmittedScanId: Job.ScanId), default);
        Assert.Equal("Verified", result.SourceProof.State); Assert.False(result.SourceProof.EnvironmentVerified);
        Assert.Equal("", result.Scan!.Environment.Browser); Assert.Equal("", result.Scan.Environment.Os);
        Assert.Equal("BaselineOnly", result.ArtifactProofs.Single(p => p.ArtifactId == "AX").State);
        Assert.False(result.Assessment!.Conditions[6].Met); Assert.False(result.Assessment.Conditions[7].Met);
        Assert.Equal("InsufficientEvidence", result.Assessment.Decision); Assert.False(result.Assessment.WebsitePackageReady);
    }

    [Fact]
    public async Task ActualAdmittedOriginalsVerifyRecordedFactsWithoutInventingWebsiteBarrierRepeatsOrMouseSuccess()
    {
        var (project, _, archive) = await Setup();
        var result = await new PortalEvidenceReadiness(project, archive).AssessAsync(Actor, Job.OrganizationId, new(AdmittedScanId: Job.ScanId), default);
        Assert.Equal("Admitted", result.Source); Assert.Equal("Assessed", result.Status); Assert.Equal("Verified", result.SourceProof.State);
        Assert.Equal(Job, result.SourceProof.Job); Assert.Equal(project.Job.WorkerArchive!.WorkerId, result.SourceProof.WorkerId);
        Assert.Equal(project.Job.WorkerArchive.Receipt.Identity, result.SourceProof.Archive);
        Assert.Equal("ControlledFixture", result.Scan!.SubjectKind); Assert.Equal(RecordedEvidenceProjection.FixtureTarget, result.TargetUrl);
        Assert.Equal("NoBlockingBarrierDocumented", result.Assessment!.Decision); Assert.False(result.Assessment.WebsitePackageReady);
        Assert.True(result.Assessment.Conditions[3].Met); Assert.True(result.Assessment.Conditions[6].Met); Assert.True(result.Assessment.Conditions[7].Met);
        Assert.False(result.Assessment.Conditions[1].Met); Assert.False(result.Assessment.Conditions[4].Met); Assert.False(result.Assessment.Conditions[5].Met);
        Assert.Empty(result.Scan.Findings); Assert.Single(result.Scan.Attempts); Assert.Equal(1, result.Scan.Test.Repetitions);
        Assert.Equal("not-tested", result.Scan.Test.PointerOutcome); Assert.Equal(2, result.Scan.Events.Length);
        Assert.All(result.Scan.Events, e => { Assert.Equal("completed", e.InputStatus); Assert.Equal("none", e.FailureKind); });
        Assert.All(result.ArtifactProofs, p => Assert.Equal("HashVerified", p.State));
        var bundle = KeyboardRecordingBundle.Decode(await Original.Value, Job); var retained = bundle.Validate(Job);
        Assert.NotNull(retained.Environment); Assert.NotEmpty(retained.Environment.Browser);
        var observation = ObservationStore.Validate(bundle.Observation, Job);
        Assert.Equal(JsonSerializer.Serialize(retained.Receipt.Observations, ObservationStore.Json), JsonSerializer.Serialize(observation.RecordedSnapshots, ObservationStore.Json));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("outcome")]
    [InlineData("key")]
    [InlineData("focus")]
    [InlineData("ax")]
    [InlineData("context")]
    [InlineData("repeat")]
    [InlineData("pointer")]
    [InlineData("artifact")]
    [InlineData("scope")]
    public async Task UploadedClaimsMustMatchOriginalTargetKeysStatesOutcomeContextAttemptsAndFiles(string change)
    {
        var (project, _, archive) = await Setup(); var service = new PortalEvidenceReadiness(project, archive);
        var source = await service.AssessAsync(Actor, Job.OrganizationId, new(AdmittedScanId: Job.ScanId), default); var scan = source.Scan!;
        scan = change switch
        {
            "target" => scan with { Target = "https://real.example/" },
            "outcome" => scan with { Test = scan.Test with { KeyboardOutcome = "blocked", ObservedOutcome = "Invented barrier" } },
            "key" => scan with { Events = scan.Events.SetItem(0, scan.Events[0] with { Key = "Enter" }) },
            "focus" => scan with { Events = scan.Events.SetItem(0, scan.Events[0] with { Focus = "Invented focus" }) },
            "ax" => scan with { Events = scan.Events.SetItem(0, scan.Events[0] with { Ax = "Invented speech" }) },
            "context" => scan with { Environment = scan.Environment with { Browser = "Different browser" } },
            "repeat" => scan with { Attempts = scan.Attempts.Add(scan.Attempts[0] with { Id = "invented-repeat" }) },
            "pointer" => scan with { Attempts = scan.Attempts.Add(scan.Attempts[0] with { Id = "invented-mouse", Mode = "pointer", Outcome = "completed" }) },
            "artifact" => scan with { Artifacts = scan.Artifacts.SetItem(0, scan.Artifacts[0] with { Path = "other.webm" }) },
            _ => scan with { SubjectKind = "Website" }
        };
        var result = await service.AssessAsync(Actor, Job.OrganizationId, new(Scan: scan, AdmittedScanId: Job.ScanId), default);
        Assert.Equal("Mismatch", result.SourceProof.State); Assert.NotEmpty(result.SourceProof.Issues);
        Assert.Equal("InsufficientEvidence", result.Assessment!.Decision); Assert.False(result.Assessment.WebsitePackageReady);
        Assert.False(result.Assessment.Conditions[1].Met); Assert.False(result.Assessment.Conditions[4].Met); Assert.False(result.Assessment.Conditions[5].Met);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("revoked")]
    [InlineData("replaced-job")]
    [InlineData("foreign-scan")]
    [InlineData("foreign-archive")]
    [InlineData("cancelled")]
    public async Task MissingChangedForeignOrUnauthorizedOriginalsCannotReleaseVerifiedFacts(string change)
    {
        var (project, store, archive) = await Setup(); var identity = project.Job.WorkerArchive!.Receipt.Identity;
        var index = await archive.ReadIndexAsync(identity); var part = index.Files.Single(f => f.Path == "original.webm").Parts[0].Sha256;
        if (change == "missing") store.Values.Remove(TraversalArchive.ObjectKey(identity, part));
        if (change == "corrupt") store.Values[TraversalArchive.ObjectKey(identity, part)][0] ^= 1;
        if (change == "foreign-scan") project.Record = project.Record with { Id = Guid.NewGuid() };
        if (change == "foreign-archive") project.Job = project.Job with { WorkerArchive = project.Job.WorkerArchive with { WorkerId = Guid.Empty } };
        using var cancellation = new CancellationTokenSource(); if (change == "cancelled") cancellation.Cancel();
        if (change is "revoked" or "replaced-job") store.ReadOverride = (id, hash, token) =>
        {
            if (hash is not null) { if (change == "revoked") project.WorkspaceHidden = true; else project.Job = project.Job with { Generation = 2 }; }
            return Task.FromResult(store.Values.GetValueOrDefault(TraversalArchive.ObjectKey(id, hash))?.ToArray());
        };
        var run = () => new PortalEvidenceReadiness(project, archive).AssessAsync(Actor, Job.OrganizationId, new(AdmittedScanId: Job.ScanId), cancellation.Token);
        if (change == "cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(run);
        else if (change is "revoked" or "replaced-job" or "foreign-scan" or "foreign-archive")
            Assert.Contains((await Assert.ThrowsAsync<PortalFault>(run)).Status, new[] { 404, 409, 503 });
        else
        {
            var response = await run(); Assert.Equal("RecordingRequired", response.Status); Assert.Null(response.Assessment); Assert.Null(response.Scan);
            Assert.NotEqual("Verified", response.SourceProof.State); Assert.Empty(response.ArtifactProofs);
        }
    }
}
