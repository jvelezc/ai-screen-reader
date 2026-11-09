using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PublicWebsiteOpenApiTests
{
    [Fact]
    public void PublicContractDocumentsEveryActualRouteWithPathOnlySelectorsAndAcceptedAdmission()
    {
        using var json = JsonDocument.Parse(PortalOpenApi.Document); var root = json.RootElement;
        Assert.Equal("1.20.0", root.GetProperty("info").GetProperty("version").GetString());
        var paths = root.GetProperty("paths"); const string prefix = "/api/workspaces/{organization}/website-scans";
        var screening = paths.GetProperty(prefix + "/{scan}/screening/{digest}").GetProperty("get");
        Assert.Equal("ReadPublicWebsiteScreening", screening.GetProperty("operationId").GetString());
        var digest = screening.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "digest");
        Assert.True(digest.GetProperty("required").GetBoolean());
        Assert.Equal("^[a-f0-9]{64}$", digest.GetProperty("schema").GetProperty("pattern").GetString());
        var post = paths.GetProperty(prefix).GetProperty("post");
        Assert.True(post.GetProperty("responses").GetProperty("202").GetProperty("headers").TryGetProperty("Location", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("429", out _));
        Assert.False(post.GetProperty("responses").TryGetProperty("201", out _));
        foreach (var suffix in new[] { "", "/{scan}", "/{scan}/observations/{capture}/{offset}/{limit}",
            "/{scan}/observations/{capture}/{offset}/{limit}/{digest}", "/{scan}/original/{offset}/{digest}", "/{scan}/screening/{digest}" })
        {
            var operation = paths.GetProperty(prefix + suffix).GetProperty("get");
            Assert.True(operation.GetProperty("security")[0].TryGetProperty("bearerAuth", out _));
            Assert.All(operation.GetProperty("parameters").EnumerateArray(), parameter =>
            { Assert.Equal("path", parameter.GetProperty("in").GetString()); Assert.True(parameter.GetProperty("required").GetBoolean()); });
        }
        var parameters = paths.GetProperty(prefix + "/{scan}/observations/{capture}/{offset}/{limit}/{digest}").GetProperty("get").GetProperty("parameters");
        Assert.Equal(100, parameters.EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "limit")
            .GetProperty("schema").GetProperty("maximum").GetInt32());
    }

    [Fact]
    public void PublicSchemasPreservePassiveFactsAndActualByteBoundsWithoutPrivateCapabilities()
    {
        using var json = JsonDocument.Parse(PortalOpenApi.Document);
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("CreatePublicWebsite");
        Assert.Equal(new[] { "requestId", "url" }, request.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order());
        Assert.Equal(2048, request.GetProperty("properties").GetProperty("url").GetProperty("maxLength").GetInt32());
        var nodes = schemas.GetProperty("PublicWebsiteNodes").GetProperty("properties");
        Assert.Equal("NotAssessed", nodes.GetProperty("goalVerification").GetProperty("const").GetString());
        Assert.Equal("PageObservationsOnly", nodes.GetProperty("scope").GetProperty("const").GetString());
        Assert.Equal(0, nodes.GetProperty("dispatchedInputs").GetProperty("const").GetInt32());
        Assert.Equal(2, nodes.GetProperty("captures").GetProperty("const").GetInt32());
        Assert.Equal(100, nodes.GetProperty("nodes").GetProperty("maxItems").GetInt32());
        var original = schemas.GetProperty("PublicWebsiteOriginal").GetProperty("properties");
        Assert.Equal(PublicWebsiteObservation.MaximumSupportedBytes, original.GetProperty("totalBytes").GetProperty("maximum").GetInt32());
        Assert.Equal(87384, original.GetProperty("data").GetProperty("maxLength").GetInt32());
        Assert.Equal("base64", original.GetProperty("data").GetProperty("contentEncoding").GetString());
        var status = schemas.GetProperty("PublicWebsiteStatus").GetProperty("properties");
        Assert.DoesNotContain(status.EnumerateObject(), property => property.Name is "leaseToken" or "resultText" or "nativePath");
    }

    [Fact]
    public void ScreeningContractPreservesPotentialClassificationUntestedTasksAndStringPurpose()
    {
        using var json = JsonDocument.Parse(PortalOpenApi.Document);
        var wrapper = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("PublicWebsiteScreening").GetProperty("properties");
        Assert.Equal(1, wrapper.GetProperty("captureIndex").GetProperty("const").GetInt32());
        var report = wrapper.GetProperty("screening").GetProperty("properties");
        Assert.Equal("NotAssessed", report.GetProperty("taskBarrier").GetProperty("const").GetString());
        Assert.False(report.GetProperty("legalCaseDetermined").GetProperty("const").GetBoolean());
        Assert.Equal("InsufficientTaskEvidence", report.GetProperty("evidenceReadiness").GetProperty("const").GetString());
        Assert.Equal(40, report.GetProperty("issues").GetProperty("maxItems").GetInt32());
        var issue = report.GetProperty("issues").GetProperty("items").GetProperty("properties");
        Assert.Equal("PotentialIssue", issue.GetProperty("classification").GetProperty("const").GetString());
        var task = report.GetProperty("suggestedTasks").GetProperty("items").GetProperty("properties");
        Assert.Equal("string", task.GetProperty("purpose").GetProperty("type").GetString());
        Assert.Contains("DiscoverServices", task.GetProperty("purpose").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("NotTested", task.GetProperty("state").GetProperty("const").GetString());
        Assert.False(task.GetProperty("executionAuthorized").GetProperty("const").GetBoolean());
    }
}
