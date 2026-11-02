using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PortalWorkerPermissionTests
{
    private static readonly PortalIdentity Actor = new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static readonly DateTimeOffset Clock = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static PortalProjectStub Project(string status)
    {
        var project = new PortalProjectStub(); project.Record = project.Record with { State = "leased" };
        project.Job = project.Job with
        {
            State = "leased",
            Generation = 1,
            WorkerPermission = new(
            ScanWorkerPermissionSnapshot.CurrentSchema, project.Job.OrganizationId, project.Job.ScanId,
            project.Job.JobId, 1, status, Clock, status == "Missing" ? null : Clock.AddTicks(status == "Active" ? 1 : -1))
        };
        return project;
    }
    private static Task<PortalReportResponse> Read(PortalProjectStub project, PortalObservationStub reader) =>
        new PortalScanService(project, reader).ReportAsync(Actor, project.Record.OrganizationId, project.Record.Id, default);

    [Theory]
    [InlineData("Active")]
    [InlineData("Expired")]
    [InlineData("Missing")]
    public async Task ReportPreservesRawLeasedStateAndActualPermissionWithoutReadingEvidence(string status)
    {
        var project = Project(status); var reader = new PortalObservationStub();
        var result = await Read(project, reader);
        Assert.Equal("leased", result.Scan.State); Assert.Equal("Pending", result.EvidenceState);
        Assert.Equal(project.Job.WorkerPermission, result.WorkerPermission); Assert.Equal(status, result.WorkerPermission!.Status);
        Assert.Null(result.Observation); Assert.Null(result.WorkerArchive); Assert.Null(result.ErrorCode);
        Assert.Equal(0, reader.Calls); Assert.Equal(1, project.Job.Generation);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation")]
    [InlineData("contradiction")]
    public async Task UnconfirmedPermissionCannotBecomeAnActivePendingReport(string change)
    {
        var project = Project("Active"); var reader = new PortalObservationStub(); var permission = project.Job.WorkerPermission!;
        project.Job = project.Job with
        {
            WorkerPermission = change switch
            {
                "missing" => null,
                "organization" => permission with { OrganizationId = Guid.NewGuid() },
                "scan" => permission with { ScanId = Guid.NewGuid() },
                "job" => permission with { JobId = Guid.NewGuid() },
                "generation" => permission with { Generation = 2 },
                _ => permission with { ExpiresAt = Clock }
            }
        };
        var error = await Assert.ThrowsAsync<PortalFault>(() => Read(project, reader));
        Assert.Equal(503, error.Status); Assert.Equal("CONTROL_WORKER_PERMISSION_UNCONFIRMED", error.Code);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData("queued", "queued")]
    [InlineData("blocked", "blocked")]
    [InlineData("completed", "observed")]
    public async Task PermissionSnapshotCannotBeAttachedToAnotherExecutionState(string job, string scan)
    {
        var project = Project("Expired"); var reader = new PortalObservationStub();
        project.Job = project.Job with { State = job }; project.Record = project.Record with { State = scan };
        Assert.Equal("CONTROL_WORKER_PERMISSION_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => Read(project, reader))).Code);
        Assert.Equal(0, reader.Calls);
    }
}
