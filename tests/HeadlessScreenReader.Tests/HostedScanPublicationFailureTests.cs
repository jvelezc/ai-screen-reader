using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    [Theory]
    [InlineData(EvidencePublicationFailureProtocol.StorageUnavailable, "Unavailable", "StorageUnavailable")]
    [InlineData(EvidencePublicationFailureProtocol.WriteUnconfirmed, "Unavailable", "WriteUnconfirmed")]
    [InlineData(EvidencePublicationFailureProtocol.ContentInvalid, "Invalid", "ContentInvalid")]
    [InlineData(EvidencePublicationFailureProtocol.OriginalMissing, "Unavailable", "OriginalMissing")]
    public async Task KnownPublicationFailureGivesSameOriginalRecoveryAdviceWithoutReplay(string code, string evidence, string cause)
    {
        var handler = Queue(() => Json(Report(evidence: evidence, state: "blocked", error: code)));
        await using var client = new HostedScanClient(Options(), handler); var reply = await client.ReportAsync(ScanId.ToString());
        Assert.Equal("error", reply.Status); Assert.Equal("HOSTED_SCAN_EVIDENCE_" + evidence.ToUpperInvariant(), reply.ErrorCode);
        var data = Data(reply); Assert.Equal("blocked", data.GetProperty("scan").GetProperty("state").GetString());
        Assert.Equal(evidence, data.GetProperty("evidenceState").GetString()); Assert.Equal(code, data.GetProperty("errorCode").GetString());
        Assert.Equal(cause, data.GetProperty("publicationFailure").GetString());
        Assert.Equal("InspectRetainedPublication", data.GetProperty("nextActionCode").GetString());
        Assert.Contains("same scan", data.GetProperty("nextAction").GetString()); Assert.Contains("if available", data.GetProperty("nextAction").GetString());
        Assert.Contains("website input", data.GetProperty("nextAction").GetString());
        Assert.False(data.GetProperty("canReplay").GetBoolean()); Assert.Equal("NotAssessed", data.GetProperty("goalVerification").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("observation").ValueKind);
        Assert.False(data.TryGetProperty("barrier", out _)); Assert.DoesNotContain("scan-original-bearer-canary", reply.ToJson());
        Assert.Equal("GET", Assert.Single(handler.Requests).Method);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("observed")]
    [InlineData("notCollected")]
    [InlineData("wrongEvidence")]
    [InlineData("observation")]
    public async Task ContradictoryPublicationCauseCannotAdvertiseSafeRecovery(string change)
    {
        var report = Tree(Report(evidence: "Unavailable", state: "blocked", error: EvidencePublicationFailureProtocol.StorageUnavailable));
        switch (change)
        {
            case "queued": report["scan"]!["state"] = "queued"; break;
            case "observed": report["scan"]!["state"] = "observed"; break;
            case "notCollected": report["evidenceState"] = "NotCollected"; break;
            case "wrongEvidence": report["evidenceState"] = "Invalid"; break;
            default: report["observation"] = Tree(Observation()); break;
        }
        var handler = Queue(() => Json(report)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ReportAsync(ScanId.ToString()); Assert.Equal("error", reply.Status);
        var data = Data(reply); Assert.False(data.TryGetProperty("publicationFailure", out _));
        Assert.False(data.TryGetProperty("scan", out _)); Assert.False(data.GetProperty("automaticRetry").GetBoolean());
        Assert.DoesNotContain("InspectRetainedPublication", reply.ToJson()); Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HistoricalGenericFailureIsNotReclassifiedAsStorageFailure()
    {
        var handler = Queue(() => Json(Report(evidence: "NotCollected", state: "blocked", error: "FIXTURE_OBSERVATION_UNCONFIRMED")));
        await using var client = new HostedScanClient(Options(), handler); var reply = await client.ReportAsync(ScanId.ToString());
        Assert.Equal("scan-report", reply.Status); var data = Data(reply);
        Assert.Equal("NotCollected", data.GetProperty("evidenceState").GetString());
        Assert.Equal("InspectEvidenceState", data.GetProperty("nextActionCode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("publicationFailure").ValueKind);
        Assert.False(data.GetProperty("canReplay").GetBoolean()); Assert.Single(handler.Requests);
    }
}
