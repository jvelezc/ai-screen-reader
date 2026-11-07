using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class WebsiteScreeningApiTests
{
    [Fact]
    public async Task ScreeningUsesTheCompleteDigestPinnedOriginalWithoutTaskOrLegalClaims()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        var nodes = Enumerable.Range(1, 110).Select(i => new AccessibleNode($"layout-{i}", "generic", "")).ToList();
        nodes.Add(new("unnamed-late", "button", "")); nodes.Add(new("catalog-late", "link", "View Course Catalog"));
        Seed(rig, [.. nodes]);
        var original = rig.Text;
        var report = await rig.Service.ScreeningAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default);
        Assert.Equal(rig.Job.ScanId, report.Scan.Id); Assert.Equal(rig.Digest, report.OriginalSha256);
        Assert.Equal(1, report.CaptureIndex); Assert.Equal(2, report.Captures); Assert.Equal(112, report.Screening.InspectedNodes);
        Assert.Equal("unnamed-late", Assert.Single(report.Screening.Issues).NodeId);
        Assert.Equal(WebsiteTaskPurpose.DiscoverServices, Assert.Single(report.Screening.SuggestedTasks).Purpose);
        Assert.Equal("NotAssessed", report.Screening.TaskBarrier); Assert.False(report.Screening.LegalCaseDetermined);
        Assert.Equal("InsufficientTaskEvidence", report.Screening.EvidenceReadiness);
        Assert.Equal(original, rig.Text); Assert.Equal(1, rig.OriginalReads); Assert.Equal(2, rig.Memberships); Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task ScreeningReportsOmittedIssuesAndJourneysWithoutClaimingCompleteCoverage()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        Seed(rig, [.. Enumerable.Range(1, 60).Select(i => new AccessibleNode($"issue-{i}", "button", "")),
            .. Enumerable.Range(1, 30).Select(i => new AccessibleNode($"task-{i}", "link", "View courses"))]);
        var report = await rig.Service.ScreeningAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default);
        Assert.Equal(60, report.TotalIssues); Assert.Equal(30, report.TotalSuggestedTasks);
        Assert.Equal(40, report.Screening.Issues.Length); Assert.Equal(20, report.Screening.SuggestedTasks.Length);
        Assert.True(report.IssuesTruncated); Assert.True(report.TasksTruncated);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(report, PortalPublicWebsiteService.ScreeningJson).Length <= 131072);
    }

    [Fact]
    public async Task ScreeningByteBudgetTrimsItemsWhileRetainingCountsAndOriginalNames()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        var label = "View courses " + new string('x', 8000);
        Seed(rig, [.. Enumerable.Range(1, 20).Select(i => new AccessibleNode($"task-{i}", "link", label))]);
        var report = await rig.Service.ScreeningAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default);
        Assert.Equal(20, report.TotalSuggestedTasks);
        Assert.InRange(report.Screening.SuggestedTasks.Length, 1, 19);
        Assert.True(report.TasksTruncated); Assert.False(report.IssuesTruncated);
        Assert.All(report.Screening.SuggestedTasks, task => Assert.Equal(label, task.ExposedName));
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(report, PortalPublicWebsiteService.ScreeningJson).Length, 1, 131072);
    }

    [Fact]
    public async Task MalformedCapturedIdentityReturnsSafeConflictWithoutPartialScreening()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        Seed(rig, [new("duplicate-private-id", "button", ""), new("duplicate-private-id", "link", "View courses")]);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.ScreeningAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default));
        Assert.Equal(409, fault.Status); Assert.Equal("CONTROL_PUBLIC_WEBSITE_ORIGINAL_INVALID", fault.Code);
        Assert.DoesNotContain("duplicate-private-id", fault.Message); Assert.Equal(1, rig.OriginalReads);
    }

    [Fact]
    public async Task CurrentWorkspaceDenialWinsOverComputedScreening()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        Seed(rig, [new("private-node", "button", "")]);
        rig.AfterOriginal = () => rig.Visible = false;
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.ScreeningAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default));
        Assert.Equal(404, fault.Status); Assert.DoesNotContain("private-node", fault.Message);
        Assert.Equal(1, rig.OriginalReads);
    }

    [Fact]
    public async Task ChangedProducerCannotReleaseScreeningForAnEarlierOriginal()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        rig.AfterOriginal = () => rig.Producer = Guid.NewGuid();
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.ScreeningAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, rig.Digest, default));
        Assert.Equal(409, fault.Status); Assert.Equal("CONTROL_PUBLIC_WEBSITE_ORIGINAL_CHANGED", fault.Code);
    }

    [Fact]
    public async Task AStaleDigestCannotBeUsedToScreenANewerPage()
    {
        using var rig = new PublicWebsiteApiTests.Rig();
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.ScreeningAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, new('f', 64), default));
        Assert.Equal(409, fault.Status); Assert.Equal(0, rig.OriginalReads);
    }

    internal static void Seed(PublicWebsiteApiTests.Rig rig, ImmutableArray<AccessibleNode> nodes)
    {
        var original = PublicWebsiteObservationTests.Observation();
        var captured = original.Captures[1] with { Nodes = nodes };
        rig.SetText(JsonSerializer.Serialize(original with { Captures = [original.Captures[0], captured] }, ObservationStore.Json));
    }
}
