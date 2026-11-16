using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    [Theory]
    [InlineData("Contact", true)]
    [InlineData("Pricing", false)]
    public async Task SelectedTaskResultCannotSubstituteAnotherGoal(string returnedKind, bool accepted)
    {
        using var rig = await PublicTaskHttpTests.Rig.Create();
        var status = rig.Status with { Job = rig.Status.Job with { OrganizationId = Organization, ParentScanId = ScanId, GoalKind = "Contact" } };
        var page = new
        {
            status,
            goal = new PublicTaskGoal(returnedKind, "Reach contact", "Contact", "https://example.com/contact"),
            assessment = status.Publication!.Assessment,
            legalCaseDetermined = false,
            scope = "PublicServiceNavigationOnly",
            videoAlignment = "EventSeekingNotVerified",
            assistiveTechnologyComparison = "NotPerformed"
        };
        var handler = Queue(() => Json(page)); await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicTaskAsync("result", ScanId.ToString(), status.Job.JobId.ToString());
        Assert.Equal(accepted ? "public-task-result" : "error", result.Status);
        if (!accepted) Assert.DoesNotContain("Reach contact", result.ToJson());
        Assert.Single(handler.Requests);
    }
    private static JsonObject CoveragePage() => Tree(new
    {
        scan = PublicStatus(),
        originalSha256 = PublicDigest,
        captureIndex = 1,
        captures = 2,
        receivedAt = "2026-11-02T17:00:00Z",
        coverage = WebsiteTaskCoverage.Inspect(new("original-document", [new("pricing-link", "link", "Pricing"), new("search-field", "searchbox", "")], []) { CaptureEpoch = 1 }),
        latestTasks = Array.Empty<PublicTaskStatus>(),
        taskResultsRequireOriginalRead = true
    }).AsObject();

    [Fact]
    public async Task PrivateCoveragePreservesUnassessedScopeOriginalNamesAndReadOnlyTransport()
    {
        var handler = Queue(() => Json(CoveragePage())); await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicWebsiteCoverageAsync(ScanId.ToString(), PublicDigest);
        Assert.Equal("website-task-coverage", result.Status);
        var report = Data(result).GetProperty("coverage");
        Assert.Equal("Pricing", report.GetProperty("groups")[0].GetProperty("examples")[0].GetProperty("exposedName").GetString());
        Assert.False(report.GetProperty("groups")[0].GetProperty("executionAuthorized").GetBoolean());
        Assert.Equal("ObservedWithoutSupportedPublicInputProfile", report.GetProperty("groups")[4].GetProperty("state").GetString());
        Assert.Equal("NotStartedByCoverage", report.GetProperty("taskExecution").GetString());
        var request = Assert.Single(handler.Requests); Assert.Equal("GET", request.Method); Assert.Null(request.Body);
        Assert.EndsWith($"/{ScanId:D}/coverage/{PublicDigest}", request.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("legal")]
    [InlineData("whole-site")]
    [InlineData("execution")]
    [InlineData("state")]
    [InlineData("authority")]
    [InlineData("truncation")]
    [InlineData("unsupported-input")]
    [InlineData("wrong-source")]
    [InlineData("wrong-original")]
    [InlineData("original-verification")]
    [InlineData("count")]
    [InlineData("duplicate-original-node")]
    public async Task PrivateCoverageRejectsPromotedClaimsAndMixedOriginals(string scenario)
    {
        var page = CoveragePage(); var coverage = page["coverage"]!; var group = coverage["groups"]![0]!;
        switch (scenario)
        {
            case "legal": coverage["legalCaseDetermined"] = true; break;
            case "whole-site": coverage["fullSiteCoverageEstablished"] = true; break;
            case "execution": coverage["taskExecution"] = "Completed"; break;
            case "state": group["state"] = "NoBarrier"; break;
            case "authority": group["executionAuthorized"] = true; break;
            case "truncation": group["examplesTruncated"] = true; break;
            case "unsupported-input": coverage["groups"]![4]!["serviceLinkProfileAvailable"] = true; break;
            case "wrong-source": page["scan"]!["organizationId"] = Guid.NewGuid().ToString(); break;
            case "wrong-original": page["originalSha256"] = new string('f', 64); break;
            case "original-verification": page["taskResultsRequireOriginalRead"] = false; break;
            case "count": coverage["interactiveControls"] = 3; break;
            case "duplicate-original-node": coverage["groups"]![4]!["examples"]![0]!["nodeId"] = "pricing-link"; break;
        }
        var handler = Queue(() => Json(page)); await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicWebsiteCoverageAsync(ScanId.ToString(), PublicDigest);
        Assert.Equal("error", result.Status); Assert.DoesNotContain("pricing-link", result.ToJson()); Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("Pricing")]
    [InlineData("Catalog")]
    [InlineData("Contact")]
    [InlineData("Help")]
    public async Task SelectedGoalIsSentAndVerifiedWithoutChangingLegacyStart(string kind)
    {
        var template = PublicTaskTests.Report().Start.Job;
        var job = template with
        {
            OrganizationId = Organization,
            ParentScanId = ScanId,
            ParentOriginalSha256 = PublicDigest,
            Source = PublicReaderSource.FromUrl(PublicUrl),
            GoalKind = kind
        };
        var status = new PublicTaskStatus("hsr-public-task-status-v1", job, "queued", "Queued", PublicRequest, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, null, null, null, null, null);
        var handler = Queue(() => Json(status, 202)); await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicTaskForGoalAsync(ScanId.ToString(), job.RequestId.ToString(), PublicDigest, kind);
        Assert.Equal("public-task-start", result.Status);
        var request = Assert.Single(handler.Requests); Assert.Equal("POST", request.Method);
        var body = JsonNode.Parse(request.Body!)!; Assert.Equal(kind, body["goalKind"]!.GetValue<string>());
        Assert.Equal(PublicDigest, body["parentOriginalSha256"]!.GetValue<string>());
        Assert.Equal(job.RequestId, Guid.Parse(body["requestId"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pricing")]
    [InlineData("Search")]
    [InlineData("Contact ")]
    [InlineData(null)]
    public async Task UnsupportedSelectedGoalIsDeniedBeforeAnyHttpRequest(string? kind)
    {
        var handler = Queue(); await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicTaskForGoalAsync(ScanId.ToString(), Guid.NewGuid().ToString(), PublicDigest, kind!);
        Assert.Equal("error", result.Status); Assert.Empty(handler.Requests);
    }
}
