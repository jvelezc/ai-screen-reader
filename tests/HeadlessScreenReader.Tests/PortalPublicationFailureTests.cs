using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PortalPublicationFailureTests
{
    private static PortalProjectStub Project(string code)
    {
        var project = new PortalProjectStub(); project.Record = project.Record with { State = "blocked" };
        project.Job = project.Job with { State = "blocked", Generation = 1, Digest = null, Reference = null, ErrorCode = code };
        return project;
    }
    private static Task<PortalReportResponse> Read(PortalProjectStub project, PortalObservationStub reader) =>
        new PortalScanService(project, reader).ReportAsync(new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer),
            project.Record.OrganizationId, project.Record.Id, default);

    [Theory]
    [InlineData(EvidencePublicationFailureProtocol.StorageUnavailable, "Unavailable")]
    [InlineData(EvidencePublicationFailureProtocol.WriteUnconfirmed, "Unavailable")]
    [InlineData(EvidencePublicationFailureProtocol.ContentInvalid, "Invalid")]
    [InlineData(EvidencePublicationFailureProtocol.OriginalMissing, "Unavailable")]
    [InlineData("FIXTURE_OBSERVATION_UNCONFIRMED", "NotCollected")]
    public async Task BlockedPublicationPreservesKnownCauseWithoutClaimingRetainedOriginals(string code, string state)
    {
        var project = Project(code); var reader = new PortalObservationStub(); var result = await Read(project, reader);
        Assert.Equal("blocked", result.Scan.State); Assert.Equal(state, result.EvidenceState); Assert.Equal(code, result.ErrorCode);
        Assert.Null(result.Observation); Assert.Null(result.WorkerArchive); Assert.Null(result.WorkerPermission);
        Assert.Equal(0, reader.Calls); Assert.Equal(project.Record.Id, result.Scan.Id);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("leased")]
    [InlineData("completed")]
    [InlineData("emptyJob")]
    [InlineData("zeroGeneration")]
    [InlineData("excessGeneration")]
    [InlineData("digest")]
    [InlineData("reference")]
    public async Task PublicationCauseCannotReplaceAnotherStateOrCompletedReceipt(string change)
    {
        var project = Project(EvidencePublicationFailureProtocol.StorageUnavailable); var reader = new PortalObservationStub();
        project.Job = change switch
        {
            "queued" or "leased" or "completed" => project.Job with { State = change },
            "emptyJob" => project.Job with { JobId = Guid.Empty },
            "zeroGeneration" => project.Job with { Generation = 0 },
            "excessGeneration" => project.Job with { Generation = 4 },
            "digest" => project.Job with { Digest = new('a', 64) },
            _ => project.Job with { Reference = "private-original-reference-canary" }
        };
        var error = await Assert.ThrowsAsync<PortalFault>(() => Read(project, reader));
        Assert.Equal(503, error.Status); Assert.Equal("CONTROL_PUBLICATION_FAILURE_UNCONFIRMED", error.Code);
        Assert.Equal(0, reader.Calls); Assert.DoesNotContain("private-original-reference-canary", error.Code);
    }
}
