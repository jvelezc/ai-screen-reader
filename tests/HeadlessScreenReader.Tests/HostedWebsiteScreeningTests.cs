using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    private static JsonObject ScreeningPage() => Tree(new
    {
        scan = PublicStatus(),
        originalSha256 = PublicDigest,
        captureIndex = 1,
        captures = 2,
        receivedAt = "2026-11-06T00:00:00Z",
        screening = new
        {
            schema = WebsitePageScreening.CurrentSchema,
            policy = WebsitePageScreening.CurrentPolicy,
            documentId = "original-document",
            captureEpoch = 1,
            inspectedNodes = 10,
            interactiveControls = 2,
            issues = new[] { new { id = "i1", rule = "ExposedInteractiveControlNameMissing", nodeId = "unnamed-control", role = "button", exposedName = "", disabled = false, classification = "PotentialIssue", taskImpact = "NotAssessed" } },
            suggestedTasks = new[] { new { id = "t1", purpose = "DiscoverServices", nodeId = "course-link", role = "link", exposedName = "View Course Catalog", disabled = false, state = "NotTested", executionAuthorized = false } },
            limitations = new[] { "No task was tested." },
            scope = "CapturedPageOnly",
            taskExecution = "NotStarted",
            taskBarrier = "NotAssessed",
            evidenceReadiness = "InsufficientTaskEvidence",
            legalCaseDetermined = false
        },
        totalIssues = 1,
        totalSuggestedTasks = 1,
        issuesTruncated = false,
        tasksTruncated = false
    }).AsObject();

    [Fact]
    public async Task PrivateScreeningPreservesPotentialIssuesUntestedJourneysAndPinnedOriginal()
    {
        var handler = Queue(() => Json(ScreeningPage()));
        await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicWebsiteScreeningAsync(ScanId.ToString(), PublicDigest);
        Assert.Equal("website-screening", result.Status); Assert.Equal("GET", Assert.Single(handler.Requests).Method);
        var data = Data(result); Assert.Equal(PublicDigest, data.GetProperty("originalSha256").GetString());
        var report = data.GetProperty("screening");
        Assert.Equal("NotStarted", report.GetProperty("taskExecution").GetString());
        Assert.Equal("PotentialIssue", report.GetProperty("issues")[0].GetProperty("classification").GetString());
        Assert.Equal("NotTested", report.GetProperty("suggestedTasks")[0].GetProperty("state").GetString());
        Assert.False(report.GetProperty("suggestedTasks")[0].GetProperty("executionAuthorized").GetBoolean());
        Assert.False(report.GetProperty("legalCaseDetermined").GetBoolean());
    }

    [Theory]
    [InlineData("not-a-scan", "valid", "PUBLIC_WEBSITE_SCAN_ID_REQUIRED")]
    [InlineData("00000000-0000-0000-0000-000000000000", "valid", "PUBLIC_WEBSITE_SCAN_ID_REQUIRED")]
    [InlineData("valid", "not-a-digest", "PUBLIC_WEBSITE_DIGEST_REQUIRED")]
    [InlineData("valid", null, "PUBLIC_WEBSITE_DIGEST_REQUIRED")]
    public async Task PrivateScreeningRejectsMalformedIdentityBeforeTransport(string scan, string? digest, string code)
    {
        var handler = Queue();
        await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicWebsiteScreeningAsync(scan == "valid" ? ScanId.ToString() : scan,
            digest == "valid" ? PublicDigest : digest!);
        Assert.Equal("error", result.Status); Assert.Equal(code, result.ErrorCode); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PrivateScreeningBoundsItsEnvelopeWithoutShorteningOriginalLabels()
    {
        var page = ScreeningPage(); var report = page["screening"]!;
        var originalTask = report["suggestedTasks"]![0]!.DeepClone();
        var label = "View courses " + new string('x', 8000);
        var tasks = new JsonArray();
        for (var i = 1; i <= 20; i++)
        {
            var task = originalTask.DeepClone(); task["id"] = $"t{i}"; task["nodeId"] = $"course-{i}";
            task["exposedName"] = label; tasks.Add(task);
        }
        report["suggestedTasks"] = tasks; report["inspectedNodes"] = 30; report["interactiveControls"] = 21;
        page["totalSuggestedTasks"] = 20;
        var handler = Queue(() => Json(page));
        await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicWebsiteScreeningAsync(ScanId.ToString(), PublicDigest);
        Assert.Equal("website-screening", result.Status);
        var data = Data(result); var returned = data.GetProperty("screening").GetProperty("suggestedTasks");
        Assert.Equal(20, data.GetProperty("totalSuggestedTasks").GetInt32()); Assert.True(data.GetProperty("tasksTruncated").GetBoolean());
        Assert.InRange(returned.GetArrayLength(), 1, 19);
        Assert.All(returned.EnumerateArray(), task => Assert.Equal(label, task.GetProperty("exposedName").GetString()));
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(result.ToJson()), 1, 131072); Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PrivateScreeningCannotSilentlyDropOversizedSourceLimitations()
    {
        var page = ScreeningPage();
        page["screening"]!["limitations"] = new JsonArray(Enumerable.Range(1, 20)
            .Select(_ => (JsonNode?)JsonValue.Create(new string('x', 8000))).ToArray());
        var handler = Queue(() => Json(page));
        await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicWebsiteScreeningAsync(ScanId.ToString(), PublicDigest);
        Assert.Equal("error", result.Status); Assert.Equal("PUBLIC_WEBSITE_PAGE_TOO_LARGE", result.ErrorCode);
        Assert.DoesNotContain("course-link", result.ToJson()); Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("task-execution")]
    [InlineData("barrier")]
    [InlineData("readiness")]
    [InlineData("legal-case")]
    [InlineData("authority")]
    [InlineData("task-state")]
    [InlineData("classification")]
    [InlineData("numeric-purpose")]
    [InlineData("noninteractive-role")]
    [InlineData("overlapping-node")]
    [InlineData("digest")]
    [InlineData("capture")]
    [InlineData("counts")]
    [InlineData("extra")]
    public async Task PrivateScreeningRejectsInventedExecutionEvidenceAndScope(string scenario)
    {
        var page = ScreeningPage(); var report = page["screening"]!;
        switch (scenario)
        {
            case "task-execution": report["taskExecution"] = "Completed"; break;
            case "barrier": report["taskBarrier"] = "Documented"; break;
            case "readiness": report["evidenceReadiness"] = "Ready"; break;
            case "legal-case": report["legalCaseDetermined"] = true; break;
            case "authority": report["suggestedTasks"]![0]!["executionAuthorized"] = true; break;
            case "task-state": report["suggestedTasks"]![0]!["state"] = "Passed"; break;
            case "classification": report["issues"]![0]!["classification"] = "ConfirmedViolation"; break;
            case "numeric-purpose": report["suggestedTasks"]![0]!["purpose"] = 0; break;
            case "noninteractive-role": report["issues"]![0]!["role"] = "generic"; break;
            case "overlapping-node": report["suggestedTasks"]![0]!["nodeId"] = "unnamed-control"; break;
            case "digest": page["originalSha256"] = new string('f', 64); break;
            case "capture": page["captureIndex"] = 0; break;
            case "counts": page["totalIssues"] = 0; break;
            case "extra": report["inputToken"] = "PRIVATE_CAPABILITY_SENTINEL"; break;
        }
        var handler = Queue(() => Json(page));
        await using var client = new HostedScanClient(Options(), handler);
        var result = await client.PublicWebsiteScreeningAsync(ScanId.ToString(), PublicDigest);
        Assert.Equal("error", result.Status); Assert.DoesNotContain("course-link", result.ToJson());
        Assert.DoesNotContain("PRIVATE_CAPABILITY_SENTINEL", result.ToJson()); Assert.Single(handler.Requests);
    }
}
