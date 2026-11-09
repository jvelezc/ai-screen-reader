using System.Reflection;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

public sealed class PublicWebsiteMcpHttpTests
{
    [Fact]
    public async Task StdioPublicToolsConsumeScopedHttpBytesAndDenyAfterReconnectWithoutNewWork()
    {
        // Synthetic fixed-project provider replies; no provider JWT or original-account acceptance claim.
        using var rig = new PublicWebsiteApiTests.Rig { State = "queued" };
        WebsiteScreeningApiTests.Seed(rig, [new("heading", "heading", "Actual exposed name"),
            new("unnamed", "button", ""), new("course", "link", "View Course Catalog")]);
        await using var server = await HostedControlServer.StartAsync(
            new(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/public-website-mcp"), new string('a', 40), 0),
            rig.Project, new PortalObservationStub(), publicWebsiteRuntimeReady: () => Task.FromResult(true));
        await using (var mcp = await Connect(server.BaseAddress, rig.Job.OrganizationId))
        {
            var tools = await mcp.ListToolsAsync(); Assert.Equal(33, tools.Count);
            var screeningTool = tools.Single(tool => tool.Name == "website_screening");
            Assert.Equal(new[] { "originalSha256", "scanId" }, screeningTool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
            var admission = tools.Single(tool => tool.Name == "website_scan_start").JsonSchema;
            Assert.Equal(new[] { "requestId", "url" }, admission.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
            var created = await Call(mcp, "website_scan_start", new() { ["requestId"] = rig.RequestId, ["url"] = rig.Job.Source.TargetUrl });
            Assert.Equal("website-scan-admitted", created.GetProperty("status").GetString());
            Assert.Equal(rig.Job.ScanId, created.GetProperty("data").GetProperty("admission").GetProperty("scanId").GetGuid());
            Assert.Equal(1, rig.Creates);
            rig.State = "completed";
            var status = await Call(mcp, "website_scan_status", new() { ["scanId"] = rig.Job.ScanId });
            Assert.Equal("NotAssessed", status.GetProperty("data").GetProperty("goalVerification").GetString());
            var observation = await Call(mcp, "website_observation", new() { ["scanId"] = rig.Job.ScanId });
            var data = observation.GetProperty("data");
            Assert.Contains(data.GetProperty("nodes").EnumerateArray(), n => n.GetProperty("name").GetString() == "Actual exposed name");
            Assert.Equal(rig.Digest, data.GetProperty("originalSha256").GetString());
            Assert.Equal(0, data.GetProperty("dispatchedInputs").GetInt32());
            var screening = await Call(mcp, "website_screening", new() { ["scanId"] = rig.Job.ScanId, ["originalSha256"] = rig.Digest });
            Assert.Equal("website-screening", screening.GetProperty("status").GetString());
            Assert.Equal("InsufficientTaskEvidence", screening.GetProperty("data").GetProperty("screening").GetProperty("evidenceReadiness").GetString());
            Assert.Equal("unnamed", Assert.Single(screening.GetProperty("data").GetProperty("screening").GetProperty("issues").EnumerateArray()).GetProperty("nodeId").GetString());
            Assert.Equal("DiscoverServices", Assert.Single(screening.GetProperty("data").GetProperty("screening").GetProperty("suggestedTasks").EnumerateArray()).GetProperty("purpose").GetString());
            var original = await Call(mcp, "website_original", new() { ["scanId"] = rig.Job.ScanId, ["originalSha256"] = rig.Digest });
            Assert.Equal(Encoding.UTF8.GetBytes(rig.Text), original.GetProperty("data").GetProperty("data").GetBytesFromBase64());
        }
        await using var reconnect = await Connect(server.BaseAddress, rig.Job.OrganizationId);
        var eof = await Call(reconnect, "website_original", new()
        {
            ["scanId"] = rig.Job.ScanId,
            ["originalSha256"] = rig.Digest,
            ["offset"] = Encoding.UTF8.GetByteCount(rig.Text)
        });
        Assert.Empty(eof.GetProperty("data").GetProperty("data").GetBytesFromBase64());
        rig.Visible = false;
        var denied = await Call(reconnect, "website_observation", new() { ["scanId"] = rig.Job.ScanId }, true);
        Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", denied.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("Actual exposed name", denied.GetRawText()); Assert.Equal(1, rig.Creates);
    }

    private static Task<McpClient> Connect(Uri origin, Guid organization)
    {
        var assembly = typeof(PublicWebsiteMcpHttpTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "public website API protocol test",
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HSR_MCP_API_ORIGIN"] = origin.ToString(),
                ["HSR_MCP_ORGANIZATION"] = organization.ToString(),
                ["HSR_MCP_ACCESS_TOKEN"] = PortalProjectClientTests.Bearer
            }
        }));
    }
    private static async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?> args, bool error = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await client.CallToolAsync(tool, args, cancellationToken: deadline.Token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, text);
        Assert.True(error == (result.IsError ?? false), text); Assert.InRange(Encoding.UTF8.GetByteCount(text), 1, 131072);
        using var json = JsonDocument.Parse(text); Assert.True(JsonElement.DeepEquals(json.RootElement, result.StructuredContent!.Value));
        return json.RootElement.Clone();
    }
}
