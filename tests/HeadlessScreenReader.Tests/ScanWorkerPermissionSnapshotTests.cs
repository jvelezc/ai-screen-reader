using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class ScanWorkerPermissionSnapshotTests
{
    private static readonly FixtureJob Job = FixtureWorkerTests.Job();
    private static readonly DateTimeOffset Clock = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static ScanWorkerPermissionSnapshot Snapshot(string status, long? ticks) => new(
        ScanWorkerPermissionSnapshot.CurrentSchema, Job.OrganizationId, Job.ScanId, Job.JobId, Job.Generation,
        status, Clock, ticks is null ? null : Clock.AddTicks(ticks.Value));

    [Theory]
    [InlineData("Active", 1L)]
    [InlineData("Expired", 0L)]
    [InlineData("Expired", -1L)]
    [InlineData("Missing", null)]
    public void DatabaseClockSnapshotKeepsExactPermissionBoundaryAndScope(string status, long? ticks)
    {
        var value = Snapshot(status, ticks);
        value.Validate(Job.OrganizationId, Job.ScanId);
        Assert.Equal(status, value.Status); Assert.Equal(Clock, value.CheckedAt);
        Assert.Equal(ticks is null ? null : Clock.AddTicks(ticks.Value), value.ExpiresAt);
        Assert.Equal(Job.JobId, value.JobId); Assert.Equal(Job.Generation, value.Generation);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("zeroGeneration")]
    [InlineData("excessGeneration")]
    [InlineData("missingClock")]
    [InlineData("missingExpiry")]
    [InlineData("defaultExpiry")]
    [InlineData("unknownStatus")]
    [InlineData("activeAtExpiry")]
    [InlineData("expiredBeforeExpiry")]
    [InlineData("missingWithExpiry")]
    public void ContradictoryOrUnscopedPermissionCannotBecomeActive(string change)
    {
        var value = Snapshot("Active", 1);
        value = change switch
        {
            "schema" => value with { Schema = "other" },
            "organization" => value with { OrganizationId = Guid.NewGuid() },
            "scan" => value with { ScanId = Guid.NewGuid() },
            "job" => value with { JobId = Guid.Empty },
            "zeroGeneration" => value with { Generation = 0 },
            "excessGeneration" => value with { Generation = 4 },
            "missingClock" => value with { CheckedAt = default },
            "missingExpiry" => value with { ExpiresAt = null },
            "defaultExpiry" => value with { ExpiresAt = default(DateTimeOffset) },
            "unknownStatus" => value with { Status = "Running" },
            "activeAtExpiry" => value with { ExpiresAt = Clock },
            "expiredBeforeExpiry" => value with { Status = "Expired" },
            _ => value with { Status = "Missing" }
        };
        Assert.Equal("SCAN_WORKER_PERMISSION_INVALID", Assert.Throws<BrowserOperationException>(() =>
            value.Validate(Job.OrganizationId, Job.ScanId)).Code);
    }
}
