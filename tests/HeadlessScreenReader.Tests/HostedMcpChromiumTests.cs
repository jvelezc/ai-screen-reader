using System.Text.Json;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Cli.HostedControl;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class HostedMcpChromiumTests
{
    [Fact]
    public async Task HostedStdioUsesRealApiChromiumPagesGraphAndConfirmedClose()
    {
        // Deliberately synthetic API identity; this is not provider JWT/RLS acceptance.
        var project = new ReaderProjectStub(); await using var server = await PortalReaderHttpTests.Start(project);
        await using var client = await Connect(server.BaseAddress);
        var tools = await client.ListToolsAsync();
        Assert.Equal(new[] { "archive_file", "archive_manifest", "evidence_assess", "reader_close", "reader_execute", "reader_open_fixture", "reader_snapshot", "reader_status", "reader_timeline", "reader_trace", "scan_list", "scan_report", "scan_start_fixture" }, tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        var assessment = (await Call(client, "evidence_assess", new() { ["example"] = "gaps" })).GetProperty("data");
        Assert.Equal("InsufficientEvidence", assessment.GetProperty("assessment").GetProperty("decision").GetString());
        Assert.False(assessment.GetProperty("assessment").GetProperty("websitePackageReady").GetBoolean());
        var admissionSchema = tools.Single(t => t.Name == "scan_start_fixture").JsonSchema;
        Assert.Equal(new[] { "recordObservation", "requestId", "traversalRoute" }, admissionSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.False(admissionSchema.GetProperty("properties").GetProperty("recordObservation").GetProperty("default").GetBoolean());
        Assert.Equal("requestId", Assert.Single(admissionSchema.GetProperty("required").EnumerateArray()).GetString());
        var status = await Call(client, "reader_status");
        Assert.Equal("private core HTTP API", status.GetProperty("data").GetProperty("backend").GetString());
        Assert.False(status.GetProperty("data").GetProperty("browserInput").GetBoolean());
        Assert.Equal("synthetic-shop", status.GetProperty("data").GetProperty("durableScans").GetProperty("fixture").GetString());
        Assert.False(status.GetProperty("data").GetProperty("durableScans").GetProperty("automaticPolling").GetBoolean());
        Assert.Equal("HOSTED_FIXTURE_UNSUPPORTED", (await Call(client, "reader_open_fixture", new() { ["fixture"] = "Widgets" }, true)).GetProperty("errorCode").GetString());
        var opened = await Call(client, "reader_open_fixture"); var id = opened.GetProperty("sessionId").GetString()!;
        var first = (await Call(client, "reader_snapshot", new() { ["sessionId"] = id, ["limit"] = 2 })).GetProperty("data");
        var doc = first.GetProperty("documentId").GetString()!; var observation = first.GetProperty("observationId").GetString();
        Assert.Equal(2, first.GetProperty("returnedNodes").GetInt32()); Assert.True(first.GetProperty("hasMore").GetBoolean());
        var names = new List<string?>(); var offset = 0; var captures = project.WorkspaceCalls;
        while (true)
        {
            var page = (await Call(client, "reader_snapshot", new() { ["sessionId"] = id, ["offset"] = offset, ["limit"] = 5, ["observationId"] = observation })).GetProperty("data");
            Assert.Equal(doc, page.GetProperty("documentId").GetString()); Assert.Equal(observation, page.GetProperty("observationId").GetString());
            Assert.Equal(offset, page.GetProperty("offset").GetInt32());
            names.AddRange(page.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()));
            offset += page.GetProperty("returnedNodes").GetInt32();
            if (!page.GetProperty("hasMore").GetBoolean()) { Assert.Equal(offset, page.GetProperty("totalNodes").GetInt32()); break; }
        }
        Assert.Contains("Screen reader fixture", names); Assert.Contains("Open details", names);
        Assert.DoesNotContain("fixture-secret-do-not-log", names); Assert.True(project.WorkspaceCalls > captures);
        var step = (await Call(client, "reader_execute", new() { ["sessionId"] = id, ["documentId"] = doc, ["command"] = "NextHeading" })).GetProperty("data");
        Assert.Equal("CursorMoved", step.GetProperty("step").GetProperty("outcome").GetString());
        Assert.Equal(0, step.GetProperty("dispatchedInputs").GetInt32()); Assert.False(step.GetProperty("goalVerified").GetBoolean());
        Assert.Equal("OBSERVATION_CHANGED", (await Call(client, "reader_snapshot", new() { ["sessionId"] = id, ["offset"] = 1, ["observationId"] = observation }, true)).GetProperty("errorCode").GetString());
        foreach (var args in new[]
        {
            new Dictionary<string, object?> { ["sessionId"] = id, ["documentId"] = doc, ["command"] = "Tab" },
            new Dictionary<string, object?> { ["sessionId"] = id, ["documentId"] = doc, ["command"] = "NextHeading", ["key"] = "Enter" },
            new Dictionary<string, object?> { ["sessionId"] = id, ["documentId"] = doc, ["command"] = "NextHeading", ["goal"] = new { role = "button", name = "Open details" } }
        }) Assert.Equal("HOSTED_READ_ONLY_COMMAND_DENIED", (await Call(client, "reader_execute", args, true)).GetProperty("errorCode").GetString());
        var timeline = (await Call(client, "reader_timeline", new() { ["sessionId"] = id })).GetProperty("data");
        Assert.DoesNotContain(timeline.GetProperty("events").EnumerateArray(), e => e.GetProperty("kind").GetString() is "InputAttempted" or "InputCompleted" or "GoalVerified");
        var trace = (await Call(client, "reader_trace", new() { ["sessionId"] = id, ["limit"] = 1 })).GetProperty("data");
        Assert.Equal("Transient", trace.GetProperty("retention").GetString());
        var entry = Assert.Single(trace.GetProperty("entries").EnumerateArray());
        Assert.Equal("ReaderCursor", entry.GetProperty("edge").GetProperty("mechanism").GetString());
        Assert.Equal(entry.GetProperty("sourceState").GetProperty("id").GetString(), entry.GetProperty("edge").GetProperty("sourceStateId").GetString());
        Assert.Equal(entry.GetProperty("resultState").GetProperty("id").GetString(), entry.GetProperty("edge").GetProperty("resultStateId").GetString());
        Assert.Equal(entry.GetProperty("captureId").GetString(), entry.GetProperty("resultState").GetProperty("captureId").GetString());
        Assert.False(trace.GetProperty("traceTruncated").GetBoolean());
        var empty = (await Call(client, "reader_trace", new() { ["sessionId"] = id, ["afterSequence"] = trace.GetProperty("nextAfterSequence").GetInt64() })).GetProperty("data");
        Assert.Empty(empty.GetProperty("entries").EnumerateArray()); Assert.False(empty.GetProperty("hasMore").GetBoolean());
        Assert.Equal("closed", (await Call(client, "reader_close", new() { ["sessionId"] = id })).GetProperty("status").GetString());
        Assert.Equal("SESSION_NOT_FOUND", (await Call(client, "reader_trace", new() { ["sessionId"] = id }, true)).GetProperty("errorCode").GetString());
        using var http = PortalReaderHttpTests.Client(server);
        using var absent = await http.GetAsync(PortalReaderHttpTests.Collection + "/" + Guid.Parse(id).ToString("D"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, absent.StatusCode);
    }

    private static Task<McpClient> Connect(Uri origin)
    {
        var assembly = typeof(HostedMcpChromiumTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "private hosted reader test",
            EnvironmentVariables = new Dictionary<string, string?> { ["HSR_MCP_API_ORIGIN"] = origin.ToString(), ["HSR_MCP_ORGANIZATION"] = PortalReaderSessionTests.Organization.ToString(), ["HSR_MCP_ACCESS_TOKEN"] = "reader-first" }
        }));
    }

    private static async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?>? args = null, bool error = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var reply = await client.CallToolAsync(tool, args, cancellationToken: deadline.Token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(reply.Content)).Text;
        using var json = JsonDocument.Parse(text);
        Assert.True(error == (reply.IsError ?? false), $"{tool}: {text}");
        Assert.True(JsonElement.DeepEquals(json.RootElement, reply.StructuredContent!.Value));
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(text), 1, 131072);
        Assert.DoesNotContain("reader-first", text); return json.RootElement.Clone();
    }
}
