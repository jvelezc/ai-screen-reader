using System.Text.Json;
using HeadlessScreenReader.Mcp;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumMcpTests
{
    [Fact]
    public async Task StdioDiscovery_AdvertisesBoundedReaderToolsAndUsableSchemas()
    {
        await using var client = await ConnectAsync();
        var tools = await client.ListToolsAsync();
        Assert.Equal(new[] { "reader_close", "reader_execute", "reader_observe", "reader_open_fixture", "reader_open_url", "reader_snapshot", "reader_status", "reader_timeline" },
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        var execute = tools.Single(tool => tool.Name == "reader_execute").JsonSchema;
        Assert.Equal("object", execute.GetProperty("type").GetString());
        Assert.True(execute.GetProperty("properties").TryGetProperty("documentId", out _));
        Assert.True(execute.GetProperty("properties").TryGetProperty("goal", out _));
        var status = await CallAsync(client, "reader_status");
        Assert.False(status.GetProperty("data").GetProperty("arbitrarySites").GetBoolean());
        Assert.Contains("Foundation", status.GetProperty("data").GetProperty("fixtures").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task StdioWorkflow_RealChromium_BrowseFocusActivateVerifyTimelineAndClose()
    {
        await using var client = await ConnectAsync();
        var open = await CallAsync(client, "reader_open_fixture", new() { ["fixture"] = "Foundation" });
        var id = open.GetProperty("sessionId").GetString()!;
        var snapshot = await CallAsync(client, "reader_snapshot", new() { ["sessionId"] = id });
        var document = snapshot.GetProperty("data").GetProperty("documentId").GetString()!;
        var nodes = snapshot.GetProperty("data").GetProperty("nodes");
        Assert.DoesNotContain("fixture-secret-do-not-log", nodes.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-account-value", nodes.GetRawText(), StringComparison.Ordinal);
        var navigate = await Execute(client, id, document, "NextButton");
        Assert.Equal("CursorMoved", navigate.GetProperty("data").GetProperty("outcome").GetString());
        var unfocused = await Execute(client, id, document, "Activate");
        Assert.Equal("FocusRequired", unfocused.GetProperty("data").GetProperty("outcome").GetString());
        await Execute(client, id, document, "Tab");
        await Execute(client, id, document, "Tab");
        var activated = await CallAsync(client, "reader_execute", new()
        {
            ["sessionId"] = id,
            ["documentId"] = document,
            ["command"] = "Activate",
            ["goal"] = new { role = "heading", name = "Details opened", mustBeFocused = true }
        });
        Assert.Equal("Satisfied", activated.GetProperty("data").GetProperty("verification").GetString());
        Assert.Equal(1, activated.GetProperty("data").GetProperty("browserKeystrokes").GetInt32());
        var timeline = await CallAsync(client, "reader_timeline", new() { ["sessionId"] = id });
        var events = timeline.GetProperty("data").GetProperty("events").EnumerateArray().ToArray();
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "GoalVerified");
        Assert.Equal(new[] { "Tab", "Tab", "Enter" }, events.Where(e => e.GetProperty("kind").GetString() == "InputAttempted")
            .Select(e => e.GetProperty("detail").GetString()));
        Assert.Equal("closed", (await CallAsync(client, "reader_close", new() { ["sessionId"] = id })).GetProperty("status").GetString());
        var stale = await CallAsync(client, "reader_snapshot", new() { ["sessionId"] = id }, error: true);
        Assert.Equal("SESSION_NOT_FOUND", stale.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task StdioPagination_IsImmutableAndCannotReuseInvalidatedObservation()
    {
        await using var client = await ConnectAsync();
        var open = await CallAsync(client, "reader_open_fixture", new() { ["fixture"] = "Widgets" });
        var id = open.GetProperty("sessionId").GetString()!;
        var first = (await CallAsync(client, "reader_snapshot", new() { ["sessionId"] = id, ["limit"] = 2 })).GetProperty("data");
        Assert.Equal(2, first.GetProperty("returnedNodes").GetInt32());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        var observation = first.GetProperty("observationId").GetString();
        var second = (await CallAsync(client, "reader_snapshot", new() { ["sessionId"] = id, ["offset"] = 2, ["limit"] = 2, ["observationId"] = observation })).GetProperty("data");
        Assert.Equal(observation, second.GetProperty("observationId").GetString());
        Assert.NotEqual(first.GetProperty("nodes")[0].GetProperty("id").GetString(), second.GetProperty("nodes")[0].GetProperty("id").GetString());
        await Execute(client, id, first.GetProperty("documentId").GetString()!, "ReadCurrent");
        var stale = await CallAsync(client, "reader_snapshot", new() { ["sessionId"] = id, ["offset"] = 2, ["observationId"] = observation }, error: true);
        Assert.Equal("OBSERVATION_CHANGED", stale.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task StdioSessionReplacement_RejectsOldOwnerAndStaleDocumentWithoutInput()
    {
        await using var client = await ConnectAsync();
        var first = await CallAsync(client, "reader_open_fixture", new() { ["fixture"] = "Foundation" });
        var oldId = first.GetProperty("sessionId").GetString()!;
        var second = await CallAsync(client, "reader_open_fixture", new() { ["fixture"] = "Multiselect" });
        var id = second.GetProperty("sessionId").GetString()!;
        Assert.NotEqual(oldId, id);
        Assert.Equal("SESSION_NOT_FOUND", (await CallAsync(client, "reader_close", new() { ["sessionId"] = oldId }, error: true)).GetProperty("errorCode").GetString());
        var stale = await Execute(client, id, "stale", "Tab");
        Assert.Equal("DOCUMENT_CHANGED", stale.GetProperty("data").GetProperty("errorCode").GetString());
        Assert.Equal(0, stale.GetProperty("data").GetProperty("browserKeystrokes").GetInt32());
        var timeline = await CallAsync(client, "reader_timeline", new() { ["sessionId"] = id });
        Assert.DoesNotContain(timeline.GetProperty("data").GetProperty("events").EnumerateArray(), e => e.GetProperty("kind").GetString() == "InputAttempted");
    }

    [Fact]
    public async Task SessionLease_ExpiresAndDisposesBrowserWithoutNewInput()
    {
        var clock = new FakeTimeProvider();
        await using var workspace = new ReaderWorkspace(clock);
        var open = await workspace.OpenFixtureAsync("Foundation", false);
        Assert.Equal("opened", open.Status);
        clock.Advance(TimeSpan.FromMinutes(10));
        var result = await workspace.SnapshotAsync(open.SessionId!);
        Assert.Equal("SESSION_EXPIRED", result.ErrorCode);
        Assert.Null(result.Data);
        var fresh = await workspace.OpenFixtureAsync("Foundation", false);
        Assert.Equal("opened", fresh.Status);
        Assert.NotEqual(open.SessionId, fresh.SessionId);
    }

    [Fact]
    public async Task SessionBudget_StopsFurtherOperationsAtExactBound()
    {
        await using var workspace = new ReaderWorkspace(TimeProvider.System);
        var open = await workspace.OpenFixtureAsync("Foundation", false);
        for (var i = 0; i < 500; i++) Assert.Equal("timeline", (await workspace.TimelineAsync(open.SessionId!)).Status);
        Assert.Equal("SESSION_BUDGET_EXHAUSTED", (await workspace.SnapshotAsync(open.SessionId!)).ErrorCode);
        Assert.Equal("closed", (await workspace.CloseAsync(open.SessionId!)).Status);
    }

    [Fact]
    public async Task StdioBlockedControlAndDisallowedTextKey_RemainExplicitWithoutInput()
    {
        await using var client = await ConnectAsync();
        var open = await CallAsync(client, "reader_open_fixture", new() { ["fixture"] = "Multiselect" });
        var id = open.GetProperty("sessionId").GetString()!;
        var snapshot = await CallAsync(client, "reader_snapshot", new() { ["sessionId"] = id });
        var document = snapshot.GetProperty("data").GetProperty("documentId").GetString()!;
        await Execute(client, id, document, "Tab");
        await CallAsync(client, "reader_execute", new() { ["sessionId"] = id, ["documentId"] = document, ["command"] = "PressKey", ["key"] = "End" });
        var blocked = await Execute(client, id, document, "Activate");
        Assert.Equal("Blocked", blocked.GetProperty("data").GetProperty("outcome").GetString());
        Assert.Equal(0, blocked.GetProperty("data").GetProperty("browserKeystrokes").GetInt32());
        var denied = await CallAsync(client, "reader_execute", new() { ["sessionId"] = id, ["documentId"] = document, ["command"] = "PressKey", ["key"] = "A" });
        Assert.Equal("Denied", denied.GetProperty("data").GetProperty("outcome").GetString());
        Assert.Equal(0, denied.GetProperty("data").GetProperty("browserKeystrokes").GetInt32());
    }

    [Fact]
    public async Task StdioIdleObservation_IsBoundedSampledAndReadOnly()
    {
        await using var client = await ConnectAsync();
        var open = await CallAsync(client, "reader_open_fixture", new() { ["fixture"] = "LiveRegions" });
        var id = open.GetProperty("sessionId").GetString()!;
        var observed = await CallAsync(client, "reader_observe", new() { ["sessionId"] = id, ["durationMs"] = 2000, ["maxCaptures"] = 1 });
        var batches = observed.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal("Observed", batches[0].GetProperty("status").GetString());
        Assert.Equal("CaptureLimitReached", batches[^1].GetProperty("status").GetString());
        Assert.True(batches[0].GetProperty("samplingMayMissChanges").GetBoolean());
        Assert.DoesNotContain(batches.SelectMany(b => b.GetProperty("events").EnumerateArray()),
            e => e.GetProperty("kind").GetString() == "InputAttempted");
    }

    [Fact]
    public async Task StdioEditableAndPasswordFocus_RejectsInputAndExcludesValues()
    {
        await using var client = await ConnectAsync();
        var open = await CallAsync(client, "reader_open_fixture", new() { ["fixture"] = "Foundation" });
        var id = open.GetProperty("sessionId").GetString()!;
        var snapshot = await CallAsync(client, "reader_snapshot", new() { ["sessionId"] = id });
        var document = snapshot.GetProperty("data").GetProperty("documentId").GetString()!;
        for (var i = 0; i < 4; i++) await Execute(client, id, document, "Tab");
        foreach (var field in new[] { "Account", "Secret" })
        {
            var fresh = await CallAsync(client, "reader_snapshot", new() { ["sessionId"] = id });
            var focused = fresh.GetProperty("data").GetProperty("nodes").EnumerateArray()
                .Single(n => n.GetProperty("isFocused").GetBoolean() && n.GetProperty("role").GetString() != "RootWebArea");
            Assert.Equal(field, focused.GetProperty("name").GetString());
            Assert.Equal("ChromiumAccessibility", focused.GetProperty("editability").GetProperty("source").GetString());
            var denied = await CallAsync(client, "reader_execute", new() { ["sessionId"] = id, ["documentId"] = document, ["command"] = "PressKey", ["key"] = "Space" });
            Assert.Equal("Denied", denied.GetProperty("data").GetProperty("outcome").GetString());
            Assert.False(denied.GetProperty("data").GetProperty("inputMayHaveOccurred").GetBoolean());
            Assert.Equal(0, denied.GetProperty("data").GetProperty("browserKeystrokes").GetInt32());
            Assert.DoesNotContain("fixture-secret-do-not-log", fresh.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-account-value", fresh.GetRawText(), StringComparison.Ordinal);
            if (field == "Account") await Execute(client, id, document, "Tab");
        }
        var timeline = await CallAsync(client, "reader_timeline", new() { ["sessionId"] = id });
        Assert.Equal(2, timeline.GetProperty("data").GetProperty("events").EnumerateArray().Count(e => e.GetProperty("kind").GetString() == "InputDenied"));
    }

    private static Task<McpClient> ConnectAsync() => McpClient.CreateAsync(new StdioClientTransport(new()
    {
        Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
        Arguments = [typeof(ReaderTools).Assembly.Location],
        WorkingDirectory = AppContext.BaseDirectory,
        Name = "HeadlessScreenReader integration"
    }));

    private static Task<JsonElement> Execute(McpClient client, string session, string document, string command) =>
        CallAsync(client, "reader_execute", new() { ["sessionId"] = session, ["documentId"] = document, ["command"] = command });

    private static async Task<JsonElement> CallAsync(McpClient client, string tool,
        Dictionary<string, object?>? arguments = null, bool error = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
        Assert.Equal(error, result.IsError ?? false);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var document = JsonDocument.Parse(text);
        Assert.Equal(document.RootElement.GetRawText(), result.StructuredContent?.GetRawText());
        return document.RootElement.Clone();
    }
}
