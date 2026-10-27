using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PortalEvidenceReadinessTests
{
    private static Guid Organization => PortalReaderSessionTests.Organization;
    private static PortalIdentity Actor => PortalReaderSessionTests.Actor;
    private static string Path => $"/api/workspaces/{Organization:D}/evidence-readiness";

    [Fact]
    public async Task UrlOnlyReturnsScanRequiredWithoutInventingAnAssessment()
    {
        var project = new ReaderProjectStub(); var service = new PortalEvidenceReadiness(project, null);
        var response = await service.AssessAsync(Actor, Organization, new("https://unknown.example/"), default);
        Assert.Equal("ScanResultsRequired", response.Status); Assert.Equal("None", response.Source);
        Assert.Null(response.Assessment); Assert.Null(response.Scan); Assert.Empty(response.ArtifactProofs);
        Assert.Equal(2, project.WorkspaceCalls);
    }

    [Theory]
    [InlineData("complete", "Ready")]
    [InlineData("gaps", "InsufficientEvidence")]
    [InlineData("none", "NoBlockingBarrierDocumented")]
    public async Task AuthorizedHttpExamplesMatchPublishedContractAndNeverAuthorizeRealEvidence(string scenario, string decision)
    {
        var project = new ReaderProjectStub(); await using var server = await PortalReaderHttpTests.Start(project);
        using var http = PortalReaderHttpTests.Client(server);
        using var response = await http.PostAsJsonAsync(Path, new EvidenceReadinessRequest(Example: scenario));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var result = await response.Content.ReadFromJsonAsync<EvidenceReadinessResponse>(PortalJson.Options);
        Assert.NotNull(result); Assert.Equal(Organization, result.OrganizationId); Assert.Equal("Example", result.Source);
        Assert.Equal(decision, result.Assessment!.Decision); Assert.False(result.Assessment.WebsitePackageReady);
        using var spec = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        Assert.True(spec.RootElement.GetProperty("paths").TryGetProperty("/api/workspaces/{organization}/evidence-readiness", out _));
        Assert.Equal(2, project.WorkspaceCalls);
    }

    [Fact]
    public async Task AuthenticationAndCurrentWorkspacePrecedeEvaluationOrArchiveReads()
    {
        var project = new ReaderProjectStub(); await using var server = await PortalReaderHttpTests.Start(project);
        using var anonymous = new HttpClient { BaseAddress = server.BaseAddress };
        using var denied = await anonymous.PostAsJsonAsync(Path, new EvidenceReadinessRequest(Example: "complete"));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Equal(0, project.WorkspaceCalls);
        project.Hidden = true; using var http = PortalReaderHttpTests.Client(server);
        using var hidden = await http.PostAsJsonAsync(Path, new EvidenceReadinessRequest(Example: "complete"));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode); Assert.Equal(1, project.WorkspaceCalls);
    }

    [Theory]
    [InlineData("duplicate", 400)]
    [InlineData("unknown-property", 400)]
    [InlineData("oversize", 413)]
    [InlineData("null", 400)]
    [InlineData("mixed-source", 400)]
    [InlineData("target-mismatch", 409)]
    public async Task RawImportsRejectAmbiguityOversizeAndSourceConfusion(string scenario, int status)
    {
        var service = new PortalEvidenceReadiness(new ReaderProjectStub(), null);
        var scan = EvidenceReadinessTests.Complete(); var json = JsonSerializer.Serialize(scan, PortalJson.Options);
        var request = scenario switch
        {
            "duplicate" => new EvidenceReadinessRequest(ScanJson: json.Replace("\"demo\":false", "\"demo\":false,\"demo\":true", StringComparison.Ordinal)),
            "unknown-property" => new(ScanJson: "{\"unexpected\":true," + json[1..]),
            "oversize" => new(ScanJson: new string(' ', 65537)),
            "null" => new(ScanJson: "null"),
            "mixed-source" => new(Scan: scan, Example: "complete"),
            _ => new("https://foreign.example/", Scan: scan)
        };
        var error = await Assert.ThrowsAsync<PortalFault>(() => service.AssessAsync(Actor, Organization, request, default));
        Assert.Equal(status, error.Status);
    }

    [Fact]
    public async Task LargeRecordRouteDoesNotRelaxOtherControlBodyLimits()
    {
        await using var server = await PortalReaderHttpTests.Start(new()); using var http = PortalReaderHttpTests.Client(server);
        var scan = EvidenceReadinessTests.Complete();
        var raw = new string(' ', 5000) + JsonSerializer.Serialize(new EvidenceReadinessRequest(Scan: scan), PortalJson.Options);
        Assert.True(Encoding.UTF8.GetByteCount(raw) > 4096);
        using var accepted = await http.PostAsync(Path, new StringContent(raw, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var rejected = await http.PostAsync("/api/workspace", new StringContent(raw, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        using var oversized = await http.PostAsync(Path, new StringContent(new string(' ', 131073), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Theory]
    [InlineData("valid", true, "HashVerified")]
    [InlineData("metadata-only", false, "Unverified")]
    [InlineData("missing", false, "Missing")]
    [InlineData("corrupt", false, "Invalid")]
    [InlineData("fake-video", false, "InvalidFormat")]
    public async Task ReadinessRequiresAuthorizedOriginalBytesRatherThanDeclaredHashes(string scenario, bool ready, string state)
    {
        var project = new ReaderProjectStub(); var store = new TraversalArchiveTests.Objects(); var archive = new TraversalArchive(store);
        var record = EvidenceReadinessTests.Complete();
        var bytes = new[] { scenario == "fake-video" ? new byte[] { 7, 8 } : new byte[] { 0x1a, 0x45, 0xdf, 0xa3, 1 }, "event log"u8.ToArray(), "AX snapshots"u8.ToArray() };
        record = record with { Artifacts = record.Artifacts.Select((a, i) => a with { Sha256 = TraversalArchive.Hash(bytes[i]) }).ToImmutableArray() };
        var receipt = await archive.FinalizeAsync(Organization, Guid.NewGuid(), TraversalArchive.Hash(TraversalArchiveTests.Manifest),
            [TraversalArchiveTests.Source(TraversalArchive.SourceManifest, TraversalArchiveTests.Manifest), .. record.Artifacts.Select((a, i) => TraversalArchiveTests.Source(a.Path, bytes[i]))]);
        if (scenario is "missing" or "corrupt")
        {
            var key = TraversalArchive.ObjectKey(receipt.Identity, record.Artifacts[0].Sha256);
            if (scenario == "missing") store.Values.Remove(key); else store.Values[key][0] ^= 1;
        }
        var result = await new PortalEvidenceReadiness(project, archive).AssessAsync(Actor, Organization,
            new(Scan: record, Archive: scenario == "metadata-only" ? null : new(receipt.Identity.ArchiveId, receipt.Identity.IndexSha256)), default);
        Assert.Equal(ready, result.Assessment!.WebsitePackageReady); Assert.Equal(state, result.ArtifactProofs[0].State);
        Assert.Equal(ready ? "Ready" : "InsufficientEvidence", result.Assessment.Decision);
        Assert.Equal(scenario == "metadata-only" ? 2 : 5, project.WorkspaceCalls);
    }

    [Fact]
    public async Task RevocationDuringOriginalReadCannotReleaseAnAssessment()
    {
        var project = new ReaderProjectStub(); var store = new TraversalArchiveTests.Objects(); var archive = new TraversalArchive(store);
        var receipt = await archive.FinalizeAsync(Organization, Guid.NewGuid(), TraversalArchive.Hash(TraversalArchiveTests.Manifest), TraversalArchiveTests.Sources());
        store.ReadOverride = (identity, part, _) => { project.Hidden = true; return Task.FromResult(store.Values.GetValueOrDefault(TraversalArchive.ObjectKey(identity, part))); };
        var record = EvidenceReadinessTests.Complete() with { Artifacts = [new() { Id = "V", Type = "Video capture", Original = true, Path = "route-1-media/original.webm", Sha256 = TraversalArchive.Hash([7, 4, 2]) }] };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new PortalEvidenceReadiness(project, archive).AssessAsync(Actor, Organization,
            new(Scan: record, Archive: new(receipt.Identity.ArchiveId, receipt.Identity.IndexSha256)), default));
        Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", fault.Code);
    }

    [Fact]
    public async Task WholeOriginalVerificationReadsEachObjectOnceAndRejectsCorruptionAndCancellation()
    {
        var store = new TraversalArchiveTests.Objects(); var archive = new TraversalArchive(store);
        var bytes = new byte[4 * 1024 * 1024 + 1]; bytes[0] = 1; bytes[^1] = 2;
        var receipt = await TraversalArchiveTests.Finalize(archive, TraversalArchiveTests.Sources(bytes)); var reads = 0;
        store.ReadOverride = (identity, part, token) => { if (part is not null) reads++; return Task.FromResult(store.Values.GetValueOrDefault(TraversalArchive.ObjectKey(identity, part))); };
        var proof = await archive.VerifyOriginalAsync(receipt.Identity, "route-1-media/original.webm", TraversalArchive.Hash(bytes), bytes.Length);
        Assert.Equal(2, reads); Assert.Equal(bytes.Length, proof.Bytes); Assert.Equal(bytes[..16], proof.Header.ToArray());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => archive.VerifyOriginalAsync(receipt.Identity, proof.Path, proof.Sha256, bytes.Length, cancelled.Token));
        var error = await Assert.ThrowsAsync<TraversalArchiveException>(() => archive.VerifyOriginalAsync(receipt.Identity, proof.Path, proof.Sha256, bytes.Length - 1));
        Assert.Equal(TraversalArchiveFailure.Invalid, error.Failure);
        store.Values[TraversalArchive.ObjectKey(receipt.Identity, TraversalArchive.Hash(bytes[..(4 * 1024 * 1024)]))][0] ^= 1;
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => archive.VerifyOriginalAsync(receipt.Identity, proof.Path, proof.Sha256, bytes.Length))).Failure);
    }
}
