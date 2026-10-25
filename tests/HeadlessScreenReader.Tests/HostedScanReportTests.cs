using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    [Theory]
    [InlineData("queued", "Pending", null, null)]
    [InlineData("leased", "Pending", "WORKER_RETRY_PENDING", null)]
    [InlineData("blocked", "NotCollected", "WORKER_RUNTIME_UNCONFIRMED", null)]
    [InlineData("observed", "Unavailable", "CONTROL_EVIDENCE_UNAVAILABLE", "HOSTED_SCAN_EVIDENCE_UNAVAILABLE")]
    [InlineData("queued", "Unavailable", "CONTROL_JOB_NOT_FOUND", "HOSTED_SCAN_EVIDENCE_UNAVAILABLE")]
    [InlineData("observed", "Invalid", "CONTROL_EVIDENCE_UNCONFIRMED", "HOSTED_SCAN_EVIDENCE_INVALID")]
    public async Task ExecutionAndEvidenceStatesNeverBecomeInventedGoalOrBarrier(string state, string evidence, string? upstream, string? error)
    {
        var handler = Queue(() => Json(Report(evidence: evidence, state: state, error: upstream)));
        await using var client = new HostedScanClient(Options(), handler);
        var result = await new HostedScanTools(client).Report(ScanId.ToString());
        Assert.Equal(error is not null, result.IsError ?? false);
        var root = result.StructuredContent!.Value; var data = root.GetProperty("data");
        Assert.Equal(error, root.GetProperty("errorCode").GetString()); Assert.Equal(state, data.GetProperty("scan").GetProperty("state").GetString());
        Assert.Equal(evidence, data.GetProperty("evidenceState").GetString()); Assert.Equal(upstream, data.GetProperty("errorCode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("observation").ValueKind); Assert.Equal("NotAssessed", data.GetProperty("goalVerification").GetString());
        Assert.False(data.TryGetProperty("barrier", out _));
    }

    [Theory]
    [InlineData("S3")]
    [InlineData("Local")]
    [InlineData("LocalFallback")]
    public async Task AvailableReportPreservesCoreEvidenceDigestStorageAndExposedFacts(string storage)
    {
        var handler = Queue(() => Json(Report(Observation(storage)))); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ReportAsync(ScanId.ToString()); Assert.Equal("scan-report", reply.Status);
        var data = Data(reply); var observation = data.GetProperty("observation");
        Assert.Equal(new string('a', 64), observation.GetProperty("digest").GetString()); Assert.Equal(storage, observation.GetProperty("evidenceStorage").GetString());
        Assert.NotEqual(observation.GetProperty("digest").GetString(), data.GetProperty("reportId").GetString());
        Assert.Equal(64, data.GetProperty("reportId").GetString()!.Length); Assert.Equal(0, observation.GetProperty("dispatchedInputs").GetInt32()); Assert.False(observation.GetProperty("goalVerified").GetBoolean());
        Assert.Equal("Synthetic shop", observation.GetProperty("nodes")[0].GetProperty("name").GetString()); Assert.Equal(System.Text.Json.JsonValueKind.Null, observation.GetProperty("nodes")[0].GetProperty("required").ValueKind);
        Assert.False(data.GetProperty("hasMore").GetBoolean()); Assert.Equal(1, data.GetProperty("nextOffset").GetInt32()); Assert.Null(reply.SessionId);
    }

    [Fact]
    public async Task ReportBytePagingSurvivesReconnectAndNeverSkipsNodes()
    {
        var report = Report(Observation(nodes: [Node("one", 70000), Node("two", 70000)]));
        var firstHandler = Queue(() => Json(report)); var firstClient = new HostedScanClient(Options(), firstHandler);
        var first = Data(await firstClient.ReportAsync(ScanId.ToString())); await firstClient.DisposeAsync();
        Assert.Equal(1, first.GetProperty("returnedNodes").GetInt32()); Assert.True(first.GetProperty("hasMore").GetBoolean()); Assert.Equal(1, first.GetProperty("nextOffset").GetInt32());
        var secondHandler = Queue(() => Json(report)); await using var fresh = new HostedScanClient(Options(), secondHandler);
        var second = Data(await fresh.ReportAsync(ScanId.ToString(), 1, reportId: first.GetProperty("reportId").GetString()));
        Assert.Equal("two", second.GetProperty("observation").GetProperty("nodes")[0].GetProperty("id").GetString());
        Assert.Equal(first.GetProperty("reportId").GetString(), second.GetProperty("reportId").GetString()); Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Equal(2, second.GetProperty("nextOffset").GetInt32()); Assert.Equal("GET", Assert.Single(secondHandler.Requests).Method);
    }

    [Fact]
    public async Task ChangedReportAndRevokedAccessNeverReturnPreviousPages()
    {
        var report = Report(Observation(nodes: [Node("one"), Node("two")]));
        var handler = Queue(() => Json(report), () => Json(Report(Observation("LocalFallback"))), () => Json(new { title = "CONTROL_WORKSPACE_NOT_FOUND", detail = "scan-original-bearer-canary" }, 404));
        await using var client = new HostedScanClient(Options(), handler);
        var first = Data(await client.ReportAsync(ScanId.ToString(), limit: 1)); var id = first.GetProperty("reportId").GetString();
        var changed = await client.ReportAsync(ScanId.ToString(), 1, reportId: id); Assert.Equal("HOSTED_SCAN_REPORT_CHANGED", changed.ErrorCode); Assert.Null(changed.Data);
        var revoked = await client.ReportAsync(ScanId.ToString(), 1, reportId: id);
        Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", revoked.ErrorCode); Assert.DoesNotContain("Synthetic shop", revoked.ToJson()); Assert.DoesNotContain("scan-original-bearer-canary", revoked.ToJson());
        Assert.Equal(3, handler.Requests.Count); Assert.All(handler.Requests, r => Assert.Equal("GET", r.Method));
    }

    [Theory]
    [InlineData(-1, 80, null, "PAGE_BOUNDS_INVALID")]
    [InlineData(2049, 80, null, "PAGE_BOUNDS_INVALID")]
    [InlineData(0, 0, null, "PAGE_BOUNDS_INVALID")]
    [InlineData(0, 101, null, "PAGE_BOUNDS_INVALID")]
    [InlineData(1, 80, null, "REPORT_ID_REQUIRED")]
    [InlineData(0, 80, "forged", "REPORT_ID_INVALID")]
    public async Task InvalidReportPagesAreRejectedBeforeHttp(int offset, int limit, string? reportId, string expected)
    {
        var handler = Queue(); await using var client = new HostedScanClient(Options(), handler);
        Assert.Equal(expected, (await client.ReportAsync(ScanId.ToString(), offset, limit, reportId)).ErrorCode); Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ReportEndAndBeyondEndKeepExactCursorBounds(int count)
    {
        var report = Report(Observation(nodes: count == 0 ? [] : [Node()])); var handler = Queue(() => Json(report), () => Json(report), () => Json(report));
        await using var client = new HostedScanClient(Options(), handler); var first = Data(await client.ReportAsync(ScanId.ToString()));
        Assert.Equal(count, first.GetProperty("totalNodes").GetInt32());
        var end = Data(await client.ReportAsync(ScanId.ToString(), count, reportId: first.GetProperty("reportId").GetString()));
        Assert.Equal(0, end.GetProperty("returnedNodes").GetInt32()); Assert.Equal(count, end.GetProperty("nextOffset").GetInt32()); Assert.False(end.GetProperty("hasMore").GetBoolean());
        Assert.Equal("PAGE_BOUNDS_INVALID", (await client.ReportAsync(ScanId.ToString(), count + 1, reportId: first.GetProperty("reportId").GetString())).ErrorCode);
    }

    [Fact]
    public async Task OversizedReportNodeFailsWithoutSkippingOrInventingCursor()
    {
        var handler = Queue(() => Json(Report(Observation(nodes: [Node(characters: 140000)])))); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ReportAsync(ScanId.ToString()); Assert.Equal("HOSTED_SCAN_REPORT_ENTRY_TOO_LARGE", reply.ErrorCode); Assert.Null(reply.Data);
        Assert.DoesNotContain("nextOffset", reply.ToJson());
    }

    [Theory]
    [InlineData("wrongScan")]
    [InlineData("wrongScope")]
    [InlineData("pendingObserved")]
    [InlineData("availableQueued")]
    [InlineData("hiddenObservation")]
    [InlineData("errorWithEvidence")]
    [InlineData("invalidWithoutCode")]
    [InlineData("unsafeCode")]
    [InlineData("storage")]
    [InlineData("digest")]
    [InlineData("duplicateNode")]
    [InlineData("nullNode")]
    [InlineData("missingFact")]
    public async Task ContradictoryReportsAndMalformedEvidenceCannotBecomeAvailable(string failure)
    {
        var root = Tree(Report());
        switch (failure)
        {
            case "wrongScan": root["scan"]!["id"] = Guid.NewGuid(); break;
            case "wrongScope": root["scan"]!["organizationId"] = Guid.NewGuid(); break;
            case "pendingObserved": root = Tree(Report(evidence: "Pending")); break;
            case "availableQueued": root["scan"]!["state"] = "queued"; break;
            case "hiddenObservation": root["evidenceState"] = "Unavailable"; root["errorCode"] = "CONTROL_EVIDENCE_UNAVAILABLE"; break;
            case "errorWithEvidence": root["errorCode"] = "CONTROL_EVIDENCE_UNCONFIRMED"; break;
            case "invalidWithoutCode": root = Tree(Report(evidence: "Invalid")); break;
            case "unsafeCode": root = Tree(Report(evidence: "Invalid", error: "scan-original-bearer-canary")); break;
            case "storage": root["observation"]!["evidenceStorage"] = "PrivateForever"; break;
            case "digest": root["observation"]!["digest"] = "bad-digest"; break;
            case "duplicateNode": root["observation"]!["nodes"]!.AsArray().Add(Tree(Node())); break;
            case "nullNode": root["observation"]!["nodes"]![0] = null; break;
            default: root["observation"]!.AsObject().Remove("goalVerified"); break;
        }
        var handler = Queue(() => Json(root)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ReportAsync(ScanId.ToString()); Assert.Equal("error", reply.Status); Assert.DoesNotContain("Synthetic shop", reply.ToJson()); Assert.DoesNotContain("scan-original-bearer-canary", reply.ToJson());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task ReportedInputAndGoalViolationsRemainVisibleWithoutSourceText(int inputs, bool goal)
    {
        var root = Tree(Report()); root["observation"]!["dispatchedInputs"] = inputs; root["observation"]!["goalVerified"] = goal;
        var handler = Queue(() => Json(root)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ReportAsync(ScanId.ToString()); Assert.Equal("HOSTED_SCAN_READ_ONLY_VIOLATION", reply.ErrorCode);
        Assert.Equal(inputs, Data(reply).GetProperty("dispatchedInputs").GetInt32()); Assert.Equal(goal, Data(reply).GetProperty("goalVerified").GetBoolean()); Assert.DoesNotContain("Synthetic shop", reply.ToJson());
    }
}
