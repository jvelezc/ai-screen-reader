using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReviewAssessmentConsumerTests
{
    [Theory]
    [InlineData("MissingAssessment")]
    [InlineData("MissingInput")]
    [InlineData("MissingSiteScope")]
    [InlineData("MissingLegal")]
    [InlineData("MissingComparison")]
    [InlineData("MissingOutcome")]
    [InlineData("MissingCount")]
    [InlineData("MissingBarriers")]
    [InlineData("FalseAssessment")]
    [InlineData("InputStarted")]
    [InlineData("WholeSite")]
    [InlineData("LegalVerdict")]
    [InlineData("NVDAClaim")]
    [InlineData("WrongOutcome")]
    [InlineData("WrongCount")]
    [InlineData("WrongBarriers")]
    [InlineData("WrongOrganization")]
    [InlineData("WrongParent")]
    [InlineData("WrongRequest")]
    [InlineData("MetadataAssessment")]
    [InlineData("MissingMetadataAssessment")]
    [InlineData("MissingMetadataRead")]
    [InlineData("MissingMetadataScope")]
    [InlineData("ForeignMember")]
    [InlineData("WrongReport")]
    [InlineData("MissingConditionTruth")]
    public async Task IndependentConsumerRejectsMissingTruthAndForgedReviewFindings(string change)
    {
        var set = PublicReviewTestData.Set(1);
        var body = JsonSerializer.SerializeToNode(PublicReviewTestData.Assessment(set, [PublicReviewTestData.Verified(set.Tasks[0], true)]), PortalJson.Options)!;
        var root = body.AsObject();
        switch (change)
        {
            case "MissingAssessment": root.Remove("isAssessment"); break;
            case "MissingInput": root.Remove("websiteInputStartedByAssessment"); break;
            case "MissingSiteScope": root.Remove("fullSiteCoverageEstablished"); break;
            case "MissingLegal": root.Remove("legalCaseDetermined"); break;
            case "MissingComparison": root.Remove("assistiveTechnologyComparison"); break;
            case "MissingOutcome": root.Remove("outcome"); break;
            case "MissingCount": root.Remove("verifiedTaskCount"); break;
            case "MissingBarriers": root.Remove("barrierJobIds"); break;
            case "FalseAssessment": root["isAssessment"] = false; break;
            case "InputStarted": root["websiteInputStartedByAssessment"] = true; break;
            case "WholeSite": root["fullSiteCoverageEstablished"] = true; break;
            case "LegalVerdict": root["legalCaseDetermined"] = true; break;
            case "NVDAClaim": root["assistiveTechnologyComparison"] = "NVDAValidated"; break;
            case "WrongOutcome": root["outcome"] = "NoBlockingBarrierInTestedTasks"; break;
            case "WrongCount": root["verifiedTaskCount"] = 0; break;
            case "WrongBarriers": root["barrierJobIds"] = new JsonArray(); break;
            case "WrongOrganization": root["taskSet"]!["organizationId"] = set.SetId; break;
            case "WrongParent": root["taskSet"]!["parentScanId"] = set.SetId; break;
            case "WrongRequest": root["taskSet"]!["requestId"] = set.SetId; break;
            case "MetadataAssessment": root["taskSet"]!["isAssessment"] = true; break;
            case "MissingMetadataAssessment": root["taskSet"]!.AsObject().Remove("isAssessment"); break;
            case "MissingMetadataRead": root["taskSet"]!.AsObject().Remove("taskResultsRequireOriginalRead"); break;
            case "MissingMetadataScope": root["taskSet"]!.AsObject().Remove("scope"); break;
            case "ForeignMember": root["tasks"]![0]!["jobId"] = set.SetId; break;
            case "WrongReport": root["tasks"]![0]!["reportSha256"] = new string('f', 64); break;
            case "MissingConditionTruth": root["tasks"]![0]!["assessment"]!["conditions"]![0]!.AsObject().Remove("satisfied"); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        using var transport = new Handler(body.ToJsonString());
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), set.OrganizationId, PortalProjectClientTests.Bearer), transport);
        var reply = await client.PublicReviewAssessmentAsync(set.ParentScanId.ToString(), set.RequestId.ToString());
        Assert.Equal("error", reply.Status);
        Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode);
        Assert.Equal(1, transport.Reads);
        Assert.DoesNotContain("WebsiteEvidenceReady", reply.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, reply.ToJson(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad", "31000000-0000-4000-8000-000000000005")]
    [InlineData("31000000-0000-4000-8000-000000000002", "bad")]
    [InlineData("00000000-0000-0000-0000-000000000000", "31000000-0000-4000-8000-000000000005")]
    public async Task InvalidReviewSelectorsNeverReachTheNetwork(string scan, string request)
    {
        using var transport = new Handler("{}");
        await using var client = new HostedScanClient(new(new("http://localhost:6086/"), PublicReviewTestData.Set(0).OrganizationId, PortalProjectClientTests.Bearer), transport);
        var reply = await client.PublicReviewAssessmentAsync(scan, request);
        Assert.Equal("PUBLIC_REVIEW_REQUEST_INVALID", reply.ErrorCode);
        Assert.Equal(0, transport.Reads);
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        internal int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Reads++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization?.Parameter);
            Assert.EndsWith("/tasks/sets/31000000-0000-4000-8000-000000000005/assessment", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.CacheControl = new() { NoStore = true }; return Task.FromResult(response);
        }
    }
}
