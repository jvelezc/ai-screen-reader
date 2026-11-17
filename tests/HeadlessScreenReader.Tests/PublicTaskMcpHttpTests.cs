using System.Reflection;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskMcpHttpTests
{
    [Fact]
    public async Task SelectedGoalReconcilesThroughRealStdioAndHttpAndChangedIntentIsDenied()
    {
        using var rig = await PublicTaskHttpTests.Rig.Create();
        rig.Status = rig.Status with { Job = rig.Status.Job with { GoalKind = "Contact" } }; rig.Ready = false;
        await using var server = await rig.Server(); await using var mcp = await Connect(server.BaseAddress, rig.Status.Job.OrganizationId);
        var args = new Dictionary<string, object?>
        {
            ["scanId"] = rig.Status.Job.ParentScanId,
            ["requestId"] = rig.Status.Job.RequestId,
            ["parentOriginalSha256"] = rig.Status.Job.ParentOriginalSha256,
            ["goalKind"] = "Contact"
        };
        var result = await Call(mcp, "website_task_start_for_goal", args);
        Assert.Equal(rig.Status.Job.JobId, result.GetProperty("data").GetProperty("job").GetProperty("jobId").GetGuid());
        Assert.Equal("Contact", result.GetProperty("data").GetProperty("job").GetProperty("goalKind").GetString());
        args["goalKind"] = "Pricing";
        var denied = await Call(mcp, "website_task_start_for_goal", args, true);
        Assert.Equal("CONTROL_REQUEST_CONFLICT", denied.GetProperty("errorCode").GetString()); Assert.Equal(0, rig.Creates);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivateMcpReadsOnlyPinnedReaderOriginalsAndDeniesRevokedAccess(bool recorded)
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(recorded ? PublicTaskReaderTests.WithReader : null);
        await using var server = await rig.Server(); await using var mcp = await Connect(server.BaseAddress, rig.Status.Job.OrganizationId);
        var args = new Dictionary<string, object?> { ["scanId"] = rig.Status.Job.ParentScanId, ["jobId"] = rig.Status.Job.JobId, ["reportSha256"] = rig.Status.ReportSha256 };
        var result = (await Call(mcp, "website_task_reader", args)).GetProperty("data");
        Assert.Equal(recorded ? "Recorded" : "NotRetained", result.GetProperty("coverage").GetString());
        Assert.Equal(rig.Status.ReportSha256, result.GetProperty("reportSha256").GetString());
        if (recorded) Assert.Equal("View pricing, link", result.GetProperty("events")[0].GetProperty("reader").GetProperty("announcement").GetString());
        else Assert.Equal(JsonValueKind.Null, result.GetProperty("initial").ValueKind);
        args["reportSha256"] = new string('d', 64); await Call(mcp, "website_task_reader", args, true);
        args["reportSha256"] = rig.Status.ReportSha256; rig.DenyAfter = rig.Reads;
        var revoked = await Call(mcp, "website_task_reader", args, true);
        Assert.DoesNotContain("View pricing", revoked.GetRawText()); Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task PrivateMcpReadsSameJobResultTraceGraphAndRecordingThenDeniesRevokedAccess()
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(); await using var server = await rig.Server();
        await using var mcp = await Connect(server.BaseAddress, rig.Status.Job.OrganizationId);
        var tools = await mcp.ListToolsAsync(); Assert.Equal(47, tools.Count);
        var args = new Dictionary<string, object?> { ["scanId"] = rig.Status.Job.ParentScanId, ["jobId"] = rig.Status.Job.JobId };
        var result = await Call(mcp, "website_task_result", args);
        Assert.Equal("WebsiteEvidenceReady", result.GetProperty("data").GetProperty("assessment").GetProperty("outcome").GetString());
        Assert.Equal(4, result.GetProperty("data").GetProperty("diagnostics").GetProperty("missingTraces").GetInt32());
        args["reportSha256"] = rig.Status.ReportSha256;
        var trace = await Call(mcp, "website_task_trace", args); Assert.Equal("Enter", trace.GetProperty("data").GetProperty("events")[0].GetProperty("action").GetString());
        args["graph"] = true; var graph = await Call(mcp, "website_task_trace", args); Assert.Single(graph.GetProperty("data").GetProperty("edges").EnumerateArray()); args.Remove("graph");
        args["file"] = "attempt-1.webm"; var original = await Call(mcp, "website_task_original", args); Assert.Equal(rig.Files["attempt-1.webm"], original.GetProperty("data").GetProperty("data").GetBytesFromBase64());
        rig.DenyAfter = rig.Reads;
        var denied = await Call(mcp, "website_task_original", args, true); Assert.DoesNotContain(Convert.ToBase64String(rig.Files["attempt-1.webm"]), denied.GetRawText()); Assert.Equal(0, rig.Creates);
    }

    private static Task<McpClient> Connect(Uri origin, Guid organization)
    {
        var assembly = typeof(PublicTaskMcpHttpTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
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
