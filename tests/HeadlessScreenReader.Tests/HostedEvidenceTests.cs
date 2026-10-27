using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed class HostedEvidenceTests
{
    private static readonly Guid Organization = PortalReaderSessionTests.Organization;
    private static HostedReaderOptions Options() => new(new("http://127.0.0.1:6086/"), Organization, "evidence-bearer-canary");
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request);
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, PortalJson.Options), Encoding.UTF8, "application/json"),
        Headers = { CacheControl = new() { NoStore = true } }
    };

    [Fact]
    public async Task PrivateMcpAssessmentUsesOriginalCallerAndCoreDecision()
    {
        var service = new PortalEvidenceReadiness(new ReaderProjectStub(), null); var calls = 0;
        using var client = new HostedEvidenceClient(Options(), new Handler(async request =>
        {
            calls++; Assert.Equal("evidence-bearer-canary", request.Headers.Authorization!.Parameter);
            Assert.Equal($"/api/workspaces/{Organization:D}/evidence-readiness", request.RequestUri!.AbsolutePath);
            var input = JsonSerializer.Deserialize<EvidenceReadinessRequest>(await request.Content!.ReadAsStringAsync(), PortalJson.Options)!;
            return Json(await service.AssessAsync(PortalReaderSessionTests.Actor, Organization, input, default));
        }));
        var result = await new HostedEvidenceTools(client).Assess(example: "complete");
        Assert.False(result.IsError ?? false); Assert.Equal(1, calls);
        var data = result.StructuredContent!.Value.GetProperty("data");
        Assert.Equal("Ready", data.GetProperty("assessment").GetProperty("decision").GetString());
        Assert.False(data.GetProperty("assessment").GetProperty("websitePackageReady").GetBoolean());
        Assert.DoesNotContain("evidence-bearer-canary", data.GetRawText());
    }

    [Theory]
    [InlineData("foreign-workspace")]
    [InlineData("example-claims-real")]
    [InlineData("forged-count")]
    [InlineData("missing-condition")]
    [InlineData("invented-url-assessment")]
    public async Task PrivateMcpRejectsForgedOrForeignAssessmentResponses(string scenario)
    {
        var response = await new PortalEvidenceReadiness(new ReaderProjectStub(), null).AssessAsync(PortalReaderSessionTests.Actor, Organization, new(Example: "complete"), default);
        var tree = JsonSerializer.SerializeToNode(response, PortalJson.Options)!;
        switch (scenario)
        {
            case "foreign-workspace": tree["organizationId"] = Guid.NewGuid().ToString(); break;
            case "example-claims-real": tree["assessment"]!["websitePackageReady"] = true; break;
            case "forged-count": tree["assessment"]!["conditionsMet"] = 9; break;
            case "missing-condition": tree["assessment"]!["conditions"]!.AsArray().RemoveAt(0); break;
            default: tree["status"] = "ScanResultsRequired"; break;
        }
        using var client = new HostedEvidenceClient(Options(), new Handler(_ => Task.FromResult(Json(tree))));
        var result = await client.AssessAsync(new(Example: "complete"));
        Assert.Equal("HOSTED_RESPONSE_INVALID", result.ErrorCode);
    }
}
