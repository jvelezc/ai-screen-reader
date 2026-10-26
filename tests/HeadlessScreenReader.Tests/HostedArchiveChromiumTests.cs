using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class HostedArchiveChromiumTests
{
    [Fact]
    public async Task StdioReconnectRetrievesActualChromiumOriginalAndDeniesLostAuthority()
    {
        var fixture = await ArchiveHttpFixture.Create(0);
        var job = new FixtureJob(fixture.Identity.OrganizationId, Guid.NewGuid(), Guid.NewGuid(), 1);
        var observed = await FixtureWorker.ObserveAsync(job);
        Assert.True(observed.Shutdown.ProcessExitConfirmed); Assert.Equal(0, observed.DispatchedInputs); Assert.False(observed.GoalVerified);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(observed, ObservationStore.Json); var digest = TraversalArchive.Hash(bytes);
        var receipt = await new TraversalArchive(fixture.Store).FinalizeAsync(fixture.Identity.OrganizationId, Guid.NewGuid(), fixture.ManifestHash,
            [new(TraversalArchive.SourceManifest, fixture.Originals[TraversalArchive.SourceManifest].Length, fixture.ManifestHash,
                () => new MemoryStream(fixture.Originals[TraversalArchive.SourceManifest], writable: false)),
             new("chromium.json", bytes.Length, digest, () => new MemoryStream(bytes, writable: false))]);
        await using var server = await fixture.Start();
        var arguments = new Dictionary<string, object?> { ["archiveId"] = receipt.Identity.ArchiveId, ["indexSha256"] = receipt.Identity.IndexSha256 };
        await using (var first = await Connect(server.BaseAddress))
        {
            var tools = await first.ListToolsAsync(); Assert.Contains(tools, t => t.Name == "archive_manifest"); Assert.Contains(tools, t => t.Name == "archive_file");
            var inventory = (await Call(first, "archive_manifest", arguments)).GetProperty("data");
            Assert.Equal(receipt.Identity.IndexSha256, inventory.GetProperty("identity").GetProperty("indexSha256").GetString());
            Assert.Equal(digest, inventory.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("path").GetString() == "chromium.json").GetProperty("sha256").GetString());
        }
        arguments["path"] = "chromium.json"; arguments["fileSha256"] = digest;
        using var rebuilt = new MemoryStream(); long offset = 0;
        do
        {
            await using var fresh = await Connect(server.BaseAddress); arguments["offset"] = offset;
            var reply = (await Call(fresh, "archive_file", arguments)).GetProperty("data"); var page = reply.GetProperty("page");
            var actual = page.GetProperty("data").GetBytesFromBase64(); Assert.Equal(TraversalArchive.Hash(actual), reply.GetProperty("pageSha256").GetString());
            rebuilt.Write(actual); if (page.GetProperty("nextOffset").ValueKind == JsonValueKind.Null) break;
            offset = page.GetProperty("nextOffset").GetInt64();
        } while (true);
        Assert.Equal(bytes, rebuilt.ToArray()); Assert.Equal(digest, TraversalArchive.Hash(rebuilt.ToArray()));
        await using var denied = await Connect(server.BaseAddress); fixture.Project.Revoked = true; var reads = fixture.Store.Reads;
        Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", (await Call(denied, "archive_file", arguments, error: true)).GetProperty("errorCode").GetString());
        Assert.Equal(reads, fixture.Store.Reads);
    }

    private static Task<McpClient> Connect(Uri origin)
    {
        var assembly = typeof(HostedArchiveChromiumTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "private archive retrieval test",
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HSR_MCP_API_ORIGIN"] = origin.ToString(),
                ["HSR_MCP_ORGANIZATION"] = PortalProjectClientTests.Organization.ToString(),
                ["HSR_MCP_ACCESS_TOKEN"] = PortalProjectClientTests.Bearer
            }
        }));
    }
    private static async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?> arguments, bool error = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var reply = await client.CallToolAsync(tool, arguments, cancellationToken: deadline.Token);
        using var parsed = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(reply.Content)).Text);
        Assert.True(error == (reply.IsError ?? false), parsed.RootElement.GetRawText());
        Assert.True(JsonElement.DeepEquals(parsed.RootElement, reply.StructuredContent!.Value));
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(parsed.RootElement.GetRawText()), 1, 131072);
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, parsed.RootElement.GetRawText()); return parsed.RootElement.Clone();
    }
}
