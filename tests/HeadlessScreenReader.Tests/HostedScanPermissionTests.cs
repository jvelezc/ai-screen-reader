using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    private static object PermissionSnapshot(string status)
    {
        var checkedAt = DateTimeOffset.Parse("2026-11-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        return new
        {
            Schema = "hsr-scan-worker-permission-v1",
            OrganizationId = Organization,
            ScanId,
            JobId,
            Generation = 1L,
            Status = status,
            CheckedAt = checkedAt,
            ExpiresAt = status == "Missing" ? (DateTimeOffset?)null : checkedAt.AddMilliseconds(status == "Active" ? 1 : -1)
        };
    }

    [Theory]
    [InlineData("Expired")]
    [InlineData("Missing")]
    public async Task LostWorkerPermissionRequiresFencedRecoveryWithoutPollingOrReplaying(string status)
    {
        var report = Tree(Report(evidence: "Pending", state: "leased"));
        report["workerPermission"] = Tree(PermissionSnapshot(status));
        var handler = Queue(() => Json(report)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ReportAsync(ScanId.ToString());
        Assert.Equal("scan-report", reply.Status); Assert.Null(reply.ErrorCode);
        var data = Data(reply);
        Assert.Equal("leased", data.GetProperty("scan").GetProperty("state").GetString());
        Assert.Equal("Pending", data.GetProperty("evidenceState").GetString());
        Assert.Equal(status, data.GetProperty("workerPermission").GetProperty("status").GetString());
        Assert.Equal("AwaitFencedRecovery", data.GetProperty("nextActionCode").GetString());
        Assert.False(data.GetProperty("canReplay").GetBoolean());
        Assert.Equal("NotAssessed", data.GetProperty("goalVerification").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("observation").ValueKind);
        Assert.False(data.TryGetProperty("barrier", out _));
        Assert.DoesNotContain("Poll scan_report", data.GetProperty("nextAction").GetString());
        var request = Assert.Single(handler.Requests); Assert.Equal("GET", request.Method);
        Assert.Equal("scan-original-bearer-canary", request.Bearer);
    }

    [Fact]
    public async Task ActivePermissionKeepsExplicitPollingWithoutGrantingReplay()
    {
        var report = Tree(Report(evidence: "Pending", state: "leased"));
        report["workerPermission"] = Tree(PermissionSnapshot("Active"));
        var handler = Queue(() => Json(report)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ReportAsync(ScanId.ToString()); Assert.Equal("scan-report", reply.Status);
        var data = Data(reply); Assert.Equal("PollExplicitly", data.GetProperty("nextActionCode").GetString());
        Assert.Equal("Active", data.GetProperty("workerPermission").GetProperty("status").GetString());
        Assert.Contains("Poll scan_report explicitly", data.GetProperty("nextAction").GetString());
        Assert.False(data.GetProperty("canReplay").GetBoolean()); Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("emptyJob")]
    [InlineData("zeroGeneration")]
    [InlineData("excessGeneration")]
    [InlineData("schema")]
    [InlineData("unknownStatus")]
    [InlineData("contradictoryExpiry")]
    [InlineData("missingClock")]
    [InlineData("privateField")]
    [InlineData("terminalState")]
    [InlineData("missingSnapshot")]
    public async Task InvalidPermissionCannotAdvertisePollingOrExposeUpstreamPrivateFields(string change)
    {
        var report = Tree(Report(evidence: "Pending", state: "leased"));
        var permission = Tree(PermissionSnapshot("Expired")); report["workerPermission"] = permission;
        switch (change)
        {
            case "organization": permission["organizationId"] = Guid.NewGuid(); break;
            case "scan": permission["scanId"] = Guid.NewGuid(); break;
            case "emptyJob": permission["jobId"] = Guid.Empty; break;
            case "zeroGeneration": permission["generation"] = 0; break;
            case "excessGeneration": permission["generation"] = 4; break;
            case "schema": permission["schema"] = "other"; break;
            case "unknownStatus": permission["status"] = "Running"; break;
            case "contradictoryExpiry": permission["status"] = "Active"; break;
            case "missingClock": permission.AsObject().Remove("checkedAt"); break;
            case "privateField": permission["token"] = "permission-private-field-canary"; break;
            case "terminalState": report = Tree(Report()); report["workerPermission"] = permission.DeepClone(); break;
            default: report.AsObject().Remove("workerPermission"); break;
        }
        var handler = Queue(() => Json(report)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ReportAsync(ScanId.ToString());
        Assert.Equal("error", reply.Status);
        var data = Data(reply); Assert.False(data.GetProperty("automaticRetry").GetBoolean());
        Assert.False(data.TryGetProperty("workerPermission", out _)); Assert.False(data.TryGetProperty("scan", out _));
        Assert.DoesNotContain("Poll scan_report", reply.ToJson());
        Assert.DoesNotContain("permission-private-field-canary", reply.ToJson());
        Assert.Equal("GET", Assert.Single(handler.Requests).Method);
    }
}
