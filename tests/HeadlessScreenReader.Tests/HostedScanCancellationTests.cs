using System.Text.Json.Nodes;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    private static JsonObject CancellationReply(bool completed = false, int generation = 1) => new()
    {
        ["organizationId"] = Organization,
        ["scanId"] = ScanId,
        ["jobId"] = JobId,
        ["generation"] = generation,
        ["producer"] = generation == 0 ? null : Guid.NewGuid(),
        ["status"] = completed ? "AlreadyCompleted" : "Requested",
        ["inputAssessment"] = generation == 0 ? "NoAttemptStarted" : "NotAssessed",
        ["requestedAt"] = completed ? null : "2026-10-31T00:00:00Z",
        ["scope"] = "AdmittedRunCancellation",
        ["nativeCleanup"] = "NotAssessed",
        ["goalVerification"] = "NotAssessed",
        ["isCompletionReceipt"] = false,
        ["canReplay"] = false
    };

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task ExplicitCancellationUsesOnlyOriginalScopeAndKeepsStopInputAndCleanupMeaning(bool completed, int generation)
    {
        var handler = Queue(() => Json(CancellationReply(completed, generation), completed ? 200 : 202));
        await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.CancelAsync(ScanId.ToString("D"));
        Assert.Equal("scan-cancellation", reply.Status); Assert.Null(reply.ErrorCode);
        var data = Data(reply);
        Assert.Equal(completed ? "AlreadyCompleted" : "Requested", data.GetProperty("status").GetString());
        Assert.Equal(generation, data.GetProperty("generation").GetInt32());
        Assert.Equal(generation == 0 ? "NoAttemptStarted" : "NotAssessed", data.GetProperty("inputAssessment").GetString());
        Assert.Equal("NotAssessed", data.GetProperty("nativeCleanup").GetString());
        Assert.False(data.GetProperty("isCompletionReceipt").GetBoolean()); Assert.False(data.GetProperty("canReplay").GetBoolean());
        var request = Assert.Single(handler.Requests); Assert.Equal("POST", request.Method);
        Assert.Equal($"/api/workspaces/{Organization:D}/scans/{ScanId:D}/cancellation", request.Path);
        Assert.Equal("scan-original-bearer-canary", request.Bearer); Assert.Equal("{}", request.Body);
    }

    [Theory]
    [InlineData("organizationId", "scope")]
    [InlineData("scanId", "scope")]
    [InlineData("jobId", "empty")]
    [InlineData("generation", "too-high")]
    [InlineData("generation", "string")]
    [InlineData("producer", "empty")]
    [InlineData("producer", "null")]
    [InlineData("inputAssessment", "NoAttemptStarted")]
    [InlineData("status", "Stopped")]
    [InlineData("requestedAt", "null")]
    [InlineData("scope", "ExecutionReceipt")]
    [InlineData("nativeCleanup", "Confirmed")]
    [InlineData("goalVerification", "Satisfied")]
    [InlineData("isCompletionReceipt", "true")]
    [InlineData("canReplay", "true")]
    [InlineData("clientActor", "extra")]
    public async Task CancellationCannotPromoteForgedUpstreamScopeStopZeroInputOrCleanupClaims(string property, string defect)
    {
        var body = CancellationReply();
        body[property] = defect switch
        {
            "scope" => Guid.NewGuid(),
            "empty" => Guid.Empty,
            "null" => null,
            "too-high" => 4,
            "string" => "1",
            "true" => true,
            _ => JsonValue.Create(defect)
        };
        var handler = Queue(() => Json(body, 202)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.CancelAsync(ScanId.ToString("D"));
        Assert.NotNull(reply.ErrorCode); Assert.Equal("error", reply.Status); Assert.Single(handler.Requests);
        Assert.DoesNotContain("scan-original-bearer-canary", reply.ToJson(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 202)]
    [InlineData(false, 201)]
    public async Task HttpStatusMustAgreeWithCancellationMeaning(bool completed, int httpStatus)
    {
        var handler = Queue(() => Json(CancellationReply(completed), httpStatus));
        await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.CancelAsync(ScanId.ToString("D"));
        Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode); Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-scan")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidCancellationIdentityCannotContactCore(string scan)
    {
        var handler = Queue(); await using var client = new HostedScanClient(Options(), handler);
        Assert.Equal("SCAN_ID_REQUIRED", (await client.CancelAsync(scan)).ErrorCode); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LostCancellationResponseNeverAutomaticallyRetriesOrAdmitsReplacement()
    {
        var handler = Queue(() => throw new HttpRequestException("private-transport-canary"));
        await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.CancelAsync(ScanId.ToString("D"));
        Assert.Equal("HOSTED_SCAN_OPERATION_UNCONFIRMED", reply.ErrorCode); Assert.Single(handler.Requests);
        Assert.False(Data(reply).GetProperty("automaticRetry").GetBoolean());
        Assert.DoesNotContain("private-transport-canary", reply.ToJson(), StringComparison.Ordinal);
    }
}
