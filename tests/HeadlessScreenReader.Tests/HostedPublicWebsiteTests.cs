using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    private const string PublicUrl = "https://screenreader.privensic.com/ci/v1/source";
    private static readonly Guid PublicRequest = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PublicProducer = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly byte[] PublicBytes = Encoding.UTF8.GetBytes("{\"observed\":true}");
    private static string PublicDigest => Convert.ToHexStringLower(SHA256.HashData(PublicBytes));
    private static object PublicAdmission() => new
    {
        organizationId = Organization,
        scanId = ScanId,
        jobId = JobId,
        requestId = PublicRequest,
        targetUrl = PublicUrl,
        allowedHost = "screenreader.privensic.com",
        readerProfile = PublicReaderSource.CurrentProfile
    };
    private static JsonObject PublicStatus(string state = "completed") => Tree(new
    {
        organizationId = Organization,
        id = ScanId,
        requestId = PublicRequest,
        createdBy = PublicRequest,
        targetUrl = PublicUrl,
        allowedHost = "screenreader.privensic.com",
        readerProfile = PublicReaderSource.CurrentProfile,
        state,
        createdAt = "2026-11-02T17:00:00Z",
        jobId = JobId,
        generation = state == "queued" ? 0 : 1,
        producerId = state == "completed" ? PublicProducer : (Guid?)null,
        originalSha256 = state == "completed" ? PublicDigest : null,
        errorCode = (string?)null,
        workerPermission = (object?)null
    }).AsObject();
    private static JsonObject PublicNodePage(int offset = 0) => Tree(new
    {
        scan = PublicStatus(),
        originalSha256 = PublicDigest,
        receivedAt = "2026-11-02T19:00:00Z",
        captureIndex = 1,
        captures = 2,
        documentId = "website-document",
        documentUrl = PublicUrl,
        captureEpoch = 1,
        totalNodes = 1,
        offset,
        nodes = offset == 0 ? new[] { Node("website-heading") } : [],
        nextOffset = (int?)null,
        scope = "PageObservationsOnly",
        dispatchedInputs = 0,
        goalVerification = "NotAssessed",
        limitations = Array.Empty<string>()
    }).AsObject();
    private static JsonObject PublicOriginal(int offset = 0) => Tree(new
    {
        scan = PublicStatus(),
        originalSha256 = PublicDigest,
        totalBytes = PublicBytes.Length,
        offset,
        data = offset == 0 ? PublicBytes : [],
        pageSha256 = Convert.ToHexStringLower(SHA256.HashData(offset == 0 ? PublicBytes : [])),
        nextOffset = (int?)null
    }).AsObject();

    [Theory]
    [InlineData("Active", false)]
    [InlineData("Expired", true)]
    [InlineData("Missing", true)]
    public async Task PublicPermissionSnapshotsCannotGrantInputOrAutomaticResubmission(string permission, bool recovery)
    {
        var status = PublicStatus("leased"); status["workerPermission"] = Tree(PermissionSnapshot(permission));
        await using var client = new HostedScanClient(Options(), Queue(() => Json(status)));
        var reply = await client.PublicWebsiteStatusAsync(ScanId.ToString());
        Assert.Equal("website-scan-status", reply.Status); Assert.Null(reply.ErrorCode);
        Assert.Equal(recovery, Data(reply).GetProperty("requiresFencedRecovery").GetBoolean());
        Assert.False(Data(reply).GetProperty("automaticRetry").GetBoolean());
        Assert.Equal("NotAssessed", Data(reply).GetProperty("evidenceSufficiency").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicListUsesMetadataOnlyAndRejectsForeignRows(bool foreign)
    {
        var summary = PublicStatus();
        foreach (var field in new[] { "jobId", "generation", "producerId", "originalSha256", "errorCode", "workerPermission" }) summary.Remove(field);
        summary["state"] = "observed"; if (foreign) summary["organizationId"] = Guid.NewGuid();
        var handler = Queue(() => Json(new { items = new[] { summary }, hasMore = false }));
        await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ListPublicWebsitesAsync();
        Assert.Equal(foreign ? "error" : "website-scan-list", reply.Status);
        if (!foreign) Assert.Single(Data(reply).GetProperty("items").EnumerateArray());
        Assert.Equal("GET", Assert.Single(handler.Requests).Method); Assert.EndsWith("/website-scans", handler.Requests[0].Path);
        Assert.DoesNotContain("resultText", reply.ToJson());
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("request")]
    [InlineData("source")]
    [InlineData("empty-job")]
    public async Task PublicAdmissionCannotAcceptAnUnboundReceipt(string scenario)
    {
        var receipt = Tree(PublicAdmission());
        switch (scenario)
        {
            case "organization": receipt["organizationId"] = Guid.NewGuid(); break;
            case "request": receipt["requestId"] = Guid.NewGuid(); break;
            case "source": receipt["targetUrl"] = "https://example.com/"; receipt["allowedHost"] = "example.com"; break;
            case "empty-job": receipt["jobId"] = Guid.Empty; break;
        }
        var handler = Queue(() => Json(receipt, 202)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.StartPublicWebsiteAsync(PublicRequest.ToString(), PublicUrl);
        Assert.Equal("error", reply.Status); Assert.Equal(PublicReaderSource.CurrentProfile, reply.Profile);
        Assert.Single(handler.Requests); Assert.False(Data(reply).GetProperty("automaticRetry").GetBoolean());
    }

    [Fact]
    public async Task PublicStatusRejectsErrorCodesOutsideThePublishedAlphabetWithoutReturningSourceData()
    {
        var status = PublicStatus("blocked"); status["errorCode"] = "PUBLIC_WEBSITE_429";
        var handler = Queue(() => Json(status)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.PublicWebsiteStatusAsync(ScanId.ToString());
        Assert.Equal("error", reply.Status); Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode);
        Assert.DoesNotContain(PublicUrl, reply.ToJson()); Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PublicAdmissionUsesItsOwnSourceProfileAndCannotAutomaticallyRetryALostResponse()
    {
        var sends = 0;
        var handler = new Handler(_ => ++sends == 1 ? throw new HttpRequestException("PRIVATE_BEARER_CANARY") : Task.FromResult(Json(PublicAdmission(), 202)));
        await using var client = new HostedScanClient(Options(), handler);
        var lost = await client.StartPublicWebsiteAsync(PublicRequest.ToString(), PublicUrl);
        Assert.Equal("HOSTED_SCAN_OPERATION_UNCONFIRMED", lost.ErrorCode); Assert.Single(handler.Requests);
        Assert.Equal(PublicReaderSource.CurrentProfile, lost.Profile); Assert.False(Data(lost).GetProperty("automaticRetry").GetBoolean());
        Assert.Equal(PublicRequest, Data(lost).GetProperty("retryRequestId").GetGuid());
        var explicitRetry = await client.StartPublicWebsiteAsync(PublicRequest.ToString(), PublicUrl);
        Assert.Equal("website-scan-admitted", explicitRetry.Status); Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.All(handler.Requests, request => { Assert.Equal("POST", request.Method); Assert.EndsWith("/website-scans", request.Path); });
        Assert.Equal("NotEstablishedByAdmission", Data(explicitRetry).GetProperty("execution").GetString());
        Assert.DoesNotContain("PRIVATE_BEARER_CANARY", lost.ToJson());
    }

    [Theory]
    [InlineData("request")]
    [InlineData("target")]
    [InlineData("capture")]
    [InlineData("continuation")]
    [InlineData("digest")]
    public async Task InvalidPublicToolSelectorsNeverSendARequest(string scenario)
    {
        var handler = new Handler(_ => throw new InvalidOperationException());
        await using var client = new HostedScanClient(Options(), handler);
        var reply = scenario switch
        {
            "request" => await client.StartPublicWebsiteAsync("invalid", PublicUrl),
            "target" => await client.StartPublicWebsiteAsync(PublicRequest.ToString(), "https://127.0.0.1/"),
            "capture" => await client.PublicWebsiteNodesAsync(ScanId.ToString(), capture: 2),
            "continuation" => await client.PublicWebsiteNodesAsync(ScanId.ToString(), offset: 1),
            _ => await client.PublicWebsiteOriginalAsync(ScanId.ToString(), "invalid")
        };
        Assert.Equal("error", reply.Status); Assert.Equal(PublicReaderSource.CurrentProfile, reply.Profile); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PublicStatusAndObservationPreservePassiveScopeAcrossAnExplicitPinnedReconnect()
    {
        var handler = Queue(() => Json(PublicStatus()), () => Json(PublicNodePage()), () => Json(PublicNodePage(1)));
        await using (var first = new HostedScanClient(Options(), handler))
        {
            var status = await first.PublicWebsiteStatusAsync(ScanId.ToString());
            Assert.Equal("website-scan-status", status.Status); Assert.Equal("NotAssessed", Data(status).GetProperty("goalVerification").GetString());
            var page = await first.PublicWebsiteNodesAsync(ScanId.ToString());
            Assert.Equal("website-observation", page.Status); Assert.Equal(0, Data(page).GetProperty("dispatchedInputs").GetInt32());
            Assert.Equal("PageObservationsOnly", Data(page).GetProperty("scope").GetString());
        }
        // A new transport instance has no previous response cache or input authority.
        var reconnectHandler = Queue(() => Json(PublicNodePage(1)));
        await using var reconnect = new HostedScanClient(Options(), reconnectHandler);
        var eof = await reconnect.PublicWebsiteNodesAsync(ScanId.ToString(), offset: 1, originalSha256: PublicDigest);
        Assert.Equal("website-observation", eof.Status); Assert.Empty(Data(eof).GetProperty("nodes").EnumerateArray());
        Assert.EndsWith("/observations/1/1/80/" + PublicDigest, Assert.Single(reconnectHandler.Requests).Path);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("source")]
    [InlineData("profile")]
    [InlineData("capture")]
    [InlineData("input")]
    [InlineData("goal")]
    [InlineData("digest")]
    [InlineData("cursor")]
    [InlineData("extra")]
    public async Task PublicObservationRejectsChangedScopeFactsAndMalformedWireWithoutPartialData(string scenario)
    {
        var page = PublicNodePage();
        switch (scenario)
        {
            case "organization": page["scan"]!["organizationId"] = Guid.NewGuid(); break;
            case "source": page["documentUrl"] = "https://example.com/"; break;
            case "profile": page["scan"]!["readerProfile"] = "fixture-profile"; break;
            case "capture": page["captureIndex"] = 0; break;
            case "input": page["dispatchedInputs"] = 1; break;
            case "goal": page["goalVerification"] = "Reached"; break;
            case "digest": page["originalSha256"] = new string('a', 64); break;
            case "cursor": page["nextOffset"] = 0; break;
            case "extra": page["leaseToken"] = "PRIVATE_CAPABILITY_CANARY"; break;
        }
        var handler = Queue(() => Json(page)); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.PublicWebsiteNodesAsync(ScanId.ToString());
        Assert.Equal("error", reply.Status); Assert.DoesNotContain("website-heading", reply.ToJson());
        Assert.DoesNotContain("PRIVATE_CAPABILITY_CANARY", reply.ToJson()); Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PublicOriginalPagesCheckActualBytesAndFreshEofWithoutGrantingEvidenceCompleteness()
    {
        var handler = Queue(() => Json(PublicOriginal()), () => Json(PublicOriginal(PublicBytes.Length)));
        await using var client = new HostedScanClient(Options(), handler);
        var first = await client.PublicWebsiteOriginalAsync(ScanId.ToString(), PublicDigest);
        Assert.Equal("website-original", first.Status); Assert.Equal(PublicBytes, Data(first).GetProperty("data").GetBytesFromBase64());
        var eof = await client.PublicWebsiteOriginalAsync(ScanId.ToString(), PublicDigest, PublicBytes.Length);
        Assert.Equal("website-original", eof.Status); Assert.Empty(Data(eof).GetProperty("data").GetBytesFromBase64());
        Assert.Equal(2, handler.Requests.Count); Assert.Equal(PublicReaderSource.CurrentProfile, eof.Profile);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("whole-digest")]
    [InlineData("size")]
    [InlineData("offset")]
    [InlineData("cursor")]
    public async Task PublicOriginalRejectsWrongBytesIdentitySizeAndCursor(string scenario)
    {
        var page = PublicOriginal();
        switch (scenario)
        {
            case "hash": page["pageSha256"] = new string('a', 64); break;
            case "whole-digest": page["scan"]!["originalSha256"] = new string('a', 64); break;
            case "size": page["totalBytes"] = PublicBytes.Length + 1; break;
            case "offset": page["offset"] = 1; break;
            case "cursor": page["nextOffset"] = 1; break;
        }
        await using var client = new HostedScanClient(Options(), Queue(() => Json(page)));
        var reply = await client.PublicWebsiteOriginalAsync(ScanId.ToString(), PublicDigest);
        Assert.Equal("error", reply.Status); Assert.DoesNotContain(Convert.ToBase64String(PublicBytes), reply.ToJson());
    }
}
