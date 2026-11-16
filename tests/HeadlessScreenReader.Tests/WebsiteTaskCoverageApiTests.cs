using System.Text.Json;
using System.Collections.Concurrent;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class WebsiteTaskCoverageApiTests
{
    [Fact]
    public async Task CoverageReadLogsTheNamedOperationAndScopeWithoutOriginalNamesOrTokens()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        WebsiteScreeningApiTests.Seed(rig, [new("original-private-node", "link", "Pricing private-canary-name")]);
        var diagnostics = new ConcurrentQueue<PortalRequestDiagnostic>();
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath("artifacts/coverage-diagnostics"), new string('a', 40), 0), rig.Project, new PortalObservationStub(), diagnostic: diagnostics.Enqueue);
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.GetAsync($"/api/workspaces/{rig.Job.OrganizationId:D}/website-scans/{rig.Job.ScanId:D}/coverage/{rig.Digest}");
        Assert.Equal(200, (int)response.StatusCode);
        var item = Assert.Single(diagnostics);
        Assert.Equal("ReadPublicTaskCoverage", item.Operation);
        Assert.Equal(response.Headers.GetValues("X-HSR-Request-Id").Single(), item.SupportReference);
        var text = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("private-canary-name", text); Assert.DoesNotContain("original-private-node", text);
        Assert.DoesNotContain(rig.Actor.Token, text); Assert.Contains(rig.Job.ScanId.ToString(), text);
    }
    [Fact]
    public async Task CoverageUsesTheExactOriginalAndReturnsAnUnassessedTaskMapWithoutAdmission()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        WebsiteScreeningApiTests.Seed(rig, [new("source-pricing", "link", "Pricing"), new("source-search", "searchbox", "")]);
        var original = rig.Text;
        var result = await rig.Service.CoverageAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default);
        Assert.Equal(rig.Digest, result.OriginalSha256); Assert.Equal(rig.Job.ScanId, result.Scan.Id);
        Assert.Equal(1, result.CaptureIndex); Assert.Equal(2, result.Captures);
        Assert.Equal("source-pricing", Assert.Single(result.Coverage.Groups[0].Examples).NodeId);
        Assert.Equal("ObservedWithoutSupportedPublicInputProfile", result.Coverage.Groups[4].State);
        Assert.Equal("NotStartedByCoverage", result.Coverage.TaskExecution); Assert.False(result.Coverage.FullSiteCoverageEstablished);
        Assert.Empty(result.LatestTasks); Assert.True(result.TaskResultsRequireOriginalRead);
        Assert.Equal(1, rig.OriginalReads); Assert.Equal(1, rig.CoverageReads); Assert.Equal(4, rig.Memberships);
        Assert.Equal(0, rig.Creates); Assert.Equal(original, rig.Text);
    }

    [Theory]
    [InlineData("duplicate-goal")]
    [InlineData("duplicate-job")]
    [InlineData("duplicate-request")]
    [InlineData("foreign-review")]
    [InlineData("foreign-tenant")]
    [InlineData("different-original")]
    [InlineData("different-source")]
    [InlineData("too-many")]
    public async Task ForeignOrAmbiguousTaskMetadataCannotAppearInCoverage(string scenario)
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        var report = PublicTaskTests.Report(); var job = report.Start.Job with
        { OrganizationId = rig.Job.OrganizationId, ParentScanId = rig.Job.ScanId, ParentOriginalSha256 = rig.Digest, Source = rig.Job.Source, GoalKind = "Contact" };
        PublicTaskStatus Status(PublicTaskJob selected) => new("hsr-public-task-status-v1", selected, "queued", "Queued", rig.Actor.UserId,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null, null);
        var other = job with { JobId = Guid.NewGuid(), RequestId = Guid.NewGuid(), GoalKind = "Pricing" };
        rig.CoverageTasks = scenario switch
        {
            "duplicate-goal" => [Status(job), Status(other with { GoalKind = "Contact" })],
            "duplicate-job" => [Status(job), Status(other with { JobId = job.JobId })],
            "duplicate-request" => [Status(job), Status(other with { RequestId = job.RequestId })],
            "foreign-review" => [Status(job with { ParentScanId = Guid.NewGuid() })],
            "foreign-tenant" => [Status(job with { OrganizationId = Guid.NewGuid() })],
            "different-original" => [Status(job with { ParentOriginalSha256 = new('f', 64) })],
            "different-source" => [Status(job with { Source = PublicReaderSource.FromUrl("https://other.example/") })],
            _ => Enumerable.Repeat(Status(job), 6).ToArray()
        };
        await Assert.ThrowsAsync<PortalFault>(() => rig.Service.CoverageAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default));
        Assert.Equal(0, rig.Creates); Assert.Equal(1, rig.CoverageReads);
    }

    [Fact]
    public async Task RevocationAfterTheTaskListReadWinsOverTheHeldOriginalAndMetadata()
    {
        using var rig = new PublicWebsiteApiTests.Rig(); rig.AfterCoverage = () => rig.Visible = false;
        var failure = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.CoverageAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default));
        Assert.Equal(404, failure.Status); Assert.Equal(1, rig.OriginalReads); Assert.Equal(1, rig.CoverageReads); Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task ChangedDigestIsDeniedBeforeCoverageOrOriginalReads()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        var failure = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.CoverageAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, new('f', 64), default));
        Assert.Equal(409, failure.Status); Assert.Equal(0, rig.OriginalReads); Assert.Equal(0, rig.CoverageReads);
    }

    [Fact]
    public async Task ResponseBudgetDropsWholeExamplesWhilePreservingCountsAndOriginalNames()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        string[] labels = ["Pricing", "Course catalog", "Contact", "Help", "Search", "Email", "Sign in", "Register"];
        WebsiteScreeningApiTests.Seed(rig, [.. labels.SelectMany((label, i) => Enumerable.Range(0, 2)
            .Select(n => new AccessibleNode($"n-{i}-{n}" + new string('i', 7000), i == 5 ? "textbox" : "link", label + " " + new string('x', 7000))))]);
        var result = await rig.Service.CoverageAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(result, PortalJson.Options).Length, 1, 131072);
        Assert.Equal(16, result.Coverage.Groups.Sum(g => g.ObservedControls));
        Assert.Contains(result.Coverage.Groups, g => g.ExamplesTruncated);
        foreach (var group in result.Coverage.Groups)
            Assert.All(group.Examples, e => Assert.Contains(e.ExposedName, labels.Select(label => label + " " + new string('x', 7000))));
    }
}
