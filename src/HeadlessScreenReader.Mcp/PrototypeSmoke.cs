using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Mcp;

/// <summary>A deterministic public-page task over the same stdio entry point used by Claude.</summary>
public static class PrototypeSmoke
{
    public static async Task<int> RunAsync(string configPath)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath, deadline.Token));
            var server = config.RootElement.GetProperty("mcpServers").EnumerateObject().Single().Value;
            var errors = new List<string>();
            await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Command = server.GetProperty("command").GetString()!,
                Arguments = server.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()!).ToArray(),
                WorkingDirectory = AppContext.BaseDirectory,
                ShutdownTimeout = TimeSpan.FromSeconds(20),
                StandardErrorLines = line => { lock (errors) { if (errors.Count < 20) errors.Add(line); } Console.Error.WriteLine(line); },
                Name = "HeadlessScreenReader public acceptance"
            }), cancellationToken: deadline.Token);

            async Task<JsonElement> Call(string tool, Dictionary<string, object?>? args = null, bool error = false)
            {
                var result = await client.CallToolAsync(tool, args, cancellationToken: deadline.Token);
                if ((result.IsError ?? false) != error)
                    throw new InvalidDataException($"TOOL_RESULT_UNEXPECTED: {tool}; {result.StructuredContent?.GetProperty("errorCode").GetString() ?? "PROTOCOL_ERROR"}");
                return result.StructuredContent?.Clone()
                    ?? JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();
            }
            var status = await Call("reader_status");
            Require(status.GetProperty("data").GetProperty("isolation").GetProperty("passed").GetBoolean(), "ISOLATION_MISSING");
            var denied = await Call("reader_open_url", new() { ["url"] = "https://127.0.0.1/" }, error: true);
            Require(denied.GetProperty("errorCode").GetString() == "TARGET_URL_UNSUPPORTED", "PRIVATE_URL_NOT_DENIED");
            var opened = await Call("reader_open_url", new() { ["url"] = "https://example.com/" });
            var id = opened.GetProperty("sessionId").GetString()!;
            var first = (await Call("reader_snapshot", new() { ["sessionId"] = id, ["limit"] = 100 })).GetProperty("data");
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "public-source-observation.json"),
                first.GetRawText(), deadline.Token);
            var document = first.GetProperty("documentId").GetString()!;
            Require(first.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("role").GetString() == "RootWebArea"
                && n.GetProperty("name").GetString() == "Example Domain"), "PUBLIC_TITLE_MISSING");
            Task<JsonElement> Execute(string command, object? goal = null) => Call("reader_execute", new()
            { ["sessionId"] = id, ["documentId"] = document, ["command"] = command, ["goal"] = goal });
            var title = await Execute("DocumentStart", new { role = "RootWebArea", name = "Example Domain" });
            Require(title.GetProperty("data").GetProperty("verification").GetString() == "Satisfied", "PUBLIC_TITLE_NOT_VERIFIED");
            var missingHeading = await Execute("NextHeading");
            Require(missingHeading.GetProperty("data").GetProperty("outcome").GetString() == "NoMatch", "UNEXPECTED_PAGE_HEADING");
            var paragraph = await Execute("NextParagraph");
            Require(paragraph.GetProperty("data").GetProperty("passage").GetProperty("text").GetString()!.StartsWith("This domain", StringComparison.Ordinal), "PUBLIC_PASSAGE_MISSING");
            var link = await Execute("NextLink");
            Require(link.GetProperty("data").GetProperty("outcome").GetString() == "CursorMoved", "PUBLIC_LINK_MISSING");
            var noFocus = await Execute("Activate");
            Require(noFocus.GetProperty("data").GetProperty("outcome").GetString() == "FocusRequired", "FOCUS_WAS_REPAIRED");
            var afterBrowse = (await Call("reader_snapshot", new() { ["sessionId"] = id })).GetProperty("data");
            Require(first.GetProperty("focusedNodeId").GetRawText() == afterBrowse.GetProperty("focusedNodeId").GetRawText(), "BROWSE_CHANGED_FOCUS");
            await Execute("Tab");
            var activated = await Execute("Activate");
            Require(activated.GetProperty("data").GetProperty("browserKeystrokes").GetInt32() == 1, "LINK_KEY_NOT_COMPLETED");
            var destination = (await Call("reader_snapshot", new() { ["sessionId"] = id, ["limit"] = 100 })).GetProperty("data");
            var destinationNodes = destination.GetProperty("nodes").EnumerateArray().Select(n => n.Clone()).ToList();
            var page = destination;
            while (page.GetProperty("hasMore").GetBoolean())
            {
                page = (await Call("reader_snapshot", new()
                {
                    ["sessionId"] = id,
                    ["limit"] = 100,
                    ["offset"] = destinationNodes.Count,
                    ["observationId"] = destination.GetProperty("observationId").GetString()
                })).GetProperty("data");
                destinationNodes.AddRange(page.GetProperty("nodes").EnumerateArray().Select(n => n.Clone()));
            }
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "public-destination-observation.json"),
                JsonSerializer.Serialize(new { Metadata = destination, Nodes = destinationNodes }), deadline.Token);
            var newDocument = destination.GetProperty("documentId").GetString()!;
            Require(newDocument != document, "LINK_DID_NOT_NAVIGATE");
            var destinationUrl = new Uri(destination.GetProperty("documentUrl").GetString()!);
            Require(destinationUrl.Host is "iana.org" or "www.iana.org", "WRONG_LINK_DESTINATION");
            // This is the declared destination predicate; names do not come from arbitrary page instructions.
            var expected = destinationNodes.FirstOrDefault(n => n.GetProperty("role").GetString() == "heading"
                && string.Equals(n.GetProperty("name").GetString(), "Example Domains", StringComparison.OrdinalIgnoreCase));
            Require(expected.ValueKind != JsonValueKind.Undefined, "IANA_GOAL_HEADING_MISSING");
            document = newDocument;
            var verified = await Execute("NextHeading", new { role = "heading", name = expected.GetProperty("name").GetString() });
            Require(verified.GetProperty("data").GetProperty("verification").GetString() == "Satisfied", "IANA_GOAL_NOT_VERIFIED");
            Require(verified.GetProperty("data").GetProperty("cursorNodeId").GetString() == expected.GetProperty("id").GetString(), "IANA_CURSOR_NOT_AT_GOAL");
            var timeline = (await Call("reader_timeline", new() { ["sessionId"] = id, ["limit"] = 100 })).GetProperty("data");
            var inputs = timeline.GetProperty("events").EnumerateArray().Where(e => e.GetProperty("kind").GetString() == "InputAttempted")
                .Select(e => e.GetProperty("detail").GetString()).ToArray();
            Require(inputs.SequenceEqual(["Tab", "Enter"]), "UNEXPECTED_PUBLIC_INPUT");
            await Call("reader_close", new() { ["sessionId"] = id });
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                PrototypePassed = true,
                IsolationVerified = true,
                PrivateTargetDenied = true,
                PublicTitleVerified = true,
                MissingHeadingPreserved = true,
                PublicPassageRead = true,
                ReadOnlyBrowseObserved = true,
                FocusRequiredObserved = true,
                FocusedLinkNavigationObserved = true,
                IanaDestinationVerified = true,
                Inputs = inputs,
                SessionClosed = true,
                Destination = destinationUrl.GetLeftPart(UriPartial.Path)
            }));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or JsonException or OperationCanceledException or ModelContextProtocol.McpException)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { PrototypePassed = false, Error = exception.GetType().Name, Reason = exception is InvalidDataException ? exception.Message : "PROTOTYPE_ACCEPTANCE_FAILED" }));
            return 1;
        }
    }

    private static void Require(bool condition, string code)
    { if (!condition) throw new InvalidDataException(code); }
}
