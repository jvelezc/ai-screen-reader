using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class HostedArchiveTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Archive = Guid.NewGuid();
    private static readonly string Index = new('a', 64);
    private static readonly byte[] Original = [17, 92];
    private static string FileHash => TraversalArchive.Hash(Original);
    private static HostedReaderOptions Options() => new(new("http://127.0.0.1:6086/"), Organization, "archive-bearer-canary");
    private static TraversalArchiveIdentity Identity => new(Organization, Archive, Index);
    private static TraversalArchiveManifestPage Manifest() => new(Identity, new('b', 64), 1, 2, 0,
        [new("original.bin", 2, FileHash)], null);
    private static TraversalArchiveReadPage FilePage() => new(new(Identity, "original.bin", 2, FileHash, 0, Original.ToArray(), null), FileHash);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static JsonNode Tree(object value) => JsonSerializer.SerializeToNode(value, WireJson)!;
    private static HttpResponseMessage Json(object value, int status = 200) => new((HttpStatusCode)status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, WireJson), Encoding.UTF8, "application/json"),
        Headers = { CacheControl = new() { NoStore = true } }
    };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Bearer, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return await send(request);
        }
    }

    [Fact]
    public async Task PrivateToolsUseOriginalCallerAndRetainedIdentityWithoutLeakingCredentials()
    {
        var handler = new Handler(r => Task.FromResult(Json(r.RequestUri!.AbsolutePath.EndsWith("manifest", StringComparison.Ordinal) ? (object)Manifest() : FilePage())));
        await using var client = new HostedArchiveClient(Options(), handler); var tools = new HostedArchiveTools(client);
        var manifest = await tools.Manifest(Archive.ToString(), Index); var file = await tools.File(Archive.ToString(), Index, "original.bin", FileHash);
        Assert.False(manifest.IsError ?? false); Assert.False(file.IsError ?? false);
        Assert.Equal("archive-manifest", manifest.StructuredContent!.Value.GetProperty("status").GetString());
        var page = file.StructuredContent!.Value.GetProperty("data").GetProperty("page");
        Assert.Equal(Original, page.GetProperty("data").GetBytesFromBase64()); Assert.Equal(FileHash, page.GetProperty("fileSha256").GetString());
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("POST", request.Method); Assert.Equal("archive-bearer-canary", request.Bearer);
            Assert.StartsWith($"/api/workspaces/{Organization:D}/archives/{Archive:D}/", request.Path);
            using var body = JsonDocument.Parse(request.Body); Assert.Equal(Index, body.RootElement.GetProperty("indexSha256").GetString());
        });
        Assert.DoesNotContain("archive-bearer-canary", file.StructuredContent.Value.GetRawText());
        Assert.False(file.StructuredContent.Value.GetProperty("data").TryGetProperty("goalVerified", out _));
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("empty-archive")]
    [InlineData("digest")]
    [InlineData("offset")]
    [InlineData("limit")]
    [InlineData("file-digest")]
    [InlineData("path")]
    [InlineData("file-offset")]
    public async Task InvalidConsumerArgumentsAreRejectedBeforeHttp(string failure)
    {
        var handler = new Handler(_ => throw new InvalidOperationException("Must not send")); await using var client = new HostedArchiveClient(Options(), handler);
        var reply = failure switch
        {
            "archive" => await client.ManifestAsync("invalid", Index),
            "empty-archive" => await client.ManifestAsync(Guid.Empty.ToString(), Index),
            "digest" => await client.ManifestAsync(Archive.ToString(), "invalid"),
            "offset" => await client.ManifestAsync(Archive.ToString(), Index, -1),
            "limit" => await client.ManifestAsync(Archive.ToString(), Index, 4098),
            "file-digest" => await client.FileAsync(Archive.ToString(), Index, "original.bin", "invalid"),
            "path" => await client.FileAsync(Archive.ToString(), Index, "../original.bin", FileHash),
            _ => await client.FileAsync(Archive.ToString(), Index, "original.bin", FileHash, 134217729)
        };
        Assert.Equal("error", reply.Status); Assert.Null(reply.Data); Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("archive")]
    [InlineData("index")]
    [InlineData("source")]
    [InlineData("offset")]
    [InlineData("cursor")]
    [InlineData("count")]
    [InlineData("total")]
    [InlineData("null-file")]
    [InlineData("path")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("order")]
    public async Task InvalidManifestCannotBecomeConfirmedInventory(string failure)
    {
        var root = Tree(Manifest());
        switch (failure)
        {
            case "organization": root["identity"]!["organizationId"] = Guid.NewGuid(); break;
            case "archive": root["identity"]!["archiveId"] = Guid.NewGuid(); break;
            case "index": root["identity"]!["indexSha256"] = new string('c', 64); break;
            case "source": root["sourceManifestSha256"] = "bad"; break;
            case "offset": root["offset"] = 1; break;
            case "cursor": root["nextOffset"] = 1; break;
            case "count": root["totalFiles"] = 2; break;
            case "total": root["totalBytes"] = 1; break;
            case "null-file": root["files"]![0] = null; break;
            case "path": root["files"]![0]!["path"] = "../escape"; break;
            case "hash": root["files"]![0]!["sha256"] = "bad"; break;
            case "size": root["files"]![0]!["bytes"] = -1; break;
            case "duplicate": root["totalFiles"] = 2; root["totalBytes"] = 4; root["files"]!.AsArray().Add(root["files"]![0]!.DeepClone()); break;
            case "order":
                root["totalFiles"] = 2; root["totalBytes"] = 4; root["files"]!.AsArray().Add(root["files"]![0]!.DeepClone());
                root["files"]![0]!["path"] = "z.bin"; root["files"]![1]!["path"] = "a.bin"; break;
            default: root["invented"] = true; break;
        }
        var handler = new Handler(_ => Task.FromResult(Json(root))); await using var client = new HostedArchiveClient(Options(), handler);
        var reply = await client.ManifestAsync(Archive.ToString(), Index);
        Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode); Assert.Single(handler.Requests); Assert.DoesNotContain("original.bin", reply.ToJson());
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("archive")]
    [InlineData("index")]
    [InlineData("path")]
    [InlineData("file-hash")]
    [InlineData("page-hash")]
    [InlineData("bytes")]
    [InlineData("length")]
    [InlineData("offset")]
    [InlineData("cursor")]
    [InlineData("null-page")]
    [InlineData("missing")]
    public async Task InvalidFilePageCannotExposeUnverifiedBytes(string failure)
    {
        var root = Tree(FilePage());
        switch (failure)
        {
            case "organization": root["page"]!["identity"]!["organizationId"] = Guid.NewGuid(); break;
            case "archive": root["page"]!["identity"]!["archiveId"] = Guid.NewGuid(); break;
            case "index": root["page"]!["identity"]!["indexSha256"] = new string('c', 64); break;
            case "path": root["page"]!["path"] = "other.bin"; break;
            case "file-hash": root["page"]!["fileSha256"] = new string('d', 64); break;
            case "page-hash": root["pageSha256"] = new string('e', 64); break;
            case "bytes": root["page"]!["data"] = Convert.ToBase64String(new byte[] { 17, 93 }); break;
            case "length": root["page"]!["fileBytes"] = 3; break;
            case "offset": root["page"]!["offset"] = 1; break;
            case "cursor": root["page"]!["nextOffset"] = 2; break;
            case "null-page": root["page"] = null; break;
            default: root.AsObject().Remove("pageSha256"); break;
        }
        var handler = new Handler(_ => Task.FromResult(Json(root))); await using var client = new HostedArchiveClient(Options(), handler);
        var reply = await client.FileAsync(Archive.ToString(), Index, "original.bin", FileHash);
        Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode); Assert.Single(handler.Requests); Assert.DoesNotContain("original.bin", reply.ToJson());
    }

    [Theory]
    [InlineData(401, "CONTROL_AUTHENTICATION_REQUIRED")]
    [InlineData(404, "CONTROL_WORKSPACE_NOT_FOUND")]
    [InlineData(404, "CONTROL_ARCHIVE_NOT_FOUND")]
    [InlineData(409, "CONTROL_ARCHIVE_INVALID")]
    [InlineData(503, "CONTROL_ARCHIVE_UNAVAILABLE")]
    public async Task UpstreamDenialsRemainErrorsAndNeverReturnCachedPages(int status, string code)
    {
        var calls = 0; var handler = new Handler(_ => Task.FromResult(++calls == 1 ? Json(FilePage()) : Json(new { title = code, detail = "archive-bearer-canary" }, status)));
        await using var client = new HostedArchiveClient(Options(), handler);
        Assert.Equal("archive-file", (await client.FileAsync(Archive.ToString(), Index, "original.bin", FileHash)).Status);
        var denied = await client.FileAsync(Archive.ToString(), Index, "original.bin", FileHash);
        Assert.Equal(code, denied.ErrorCode); Assert.DoesNotContain("original.bin", denied.ToJson()); Assert.DoesNotContain("archive-bearer-canary", denied.ToJson());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CancelledReadJoinsTransportBeforeDisposalAndCannotReturnLateSuccess()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(_ => { entered.SetResult(); return finish.Task; });
        var client = new HostedArchiveClient(Options(), handler); using var stop = new CancellationTokenSource();
        var reading = client.ManifestAsync(Archive.ToString(), Index, token: stop.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel(); var disposing = client.DisposeAsync().AsTask(); Assert.False(reading.IsCompleted); Assert.False(disposing.IsCompleted);
        finish.SetResult(Json(Manifest())); Assert.Equal("HOSTED_ARCHIVE_READ_UNCONFIRMED", (await reading).ErrorCode);
        await disposing; Assert.Equal("WORKSPACE_CLOSED", (await client.ManifestAsync(Archive.ToString(), Index)).ErrorCode); Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LostTransportIsRedactedWithoutAutomaticRetry()
    {
        var handler = new Handler(_ => throw new HttpRequestException("archive-bearer-canary")); await using var client = new HostedArchiveClient(Options(), handler);
        var reply = await client.ManifestAsync(Archive.ToString(), Index); Assert.Equal("HOSTED_ARCHIVE_READ_UNCONFIRMED", reply.ErrorCode);
        Assert.Single(handler.Requests); Assert.DoesNotContain("archive-bearer-canary", reply.ToJson());
    }

    [Fact]
    public async Task ActualHttpAndFreshMcpConsumersReconstructAllPagesAndHonorRevocation()
    {
        var fixture = await ArchiveHttpFixture.Create(33, 65537); await using var server = await fixture.Start();
        var options = new HostedReaderOptions(server.BaseAddress, fixture.Identity.OrganizationId, PortalProjectClientTests.Bearer);
        var files = new List<TraversalArchiveEntry>(); var offset = 0;
        while (true)
        {
            await using var fresh = new HostedArchiveClient(options);
            var reply = await fresh.ManifestAsync(fixture.Identity.ArchiveId.ToString(), fixture.Identity.IndexSha256, offset);
            Assert.Equal("archive-manifest", reply.Status); var page = Assert.IsType<TraversalArchiveManifestPage>(reply.Data); files.AddRange(page.Files);
            if (page.NextOffset is not { } next) break; offset = next;
        }
        Assert.Equal(fixture.Originals.Keys.Order(StringComparer.Ordinal), files.Select(f => f.Path));
        var original = files.Single(f => f.Path == "original-000.bin"); using var rebuilt = new MemoryStream(); long bytes = 0;
        while (true)
        {
            await using var fresh = new HostedArchiveClient(options);
            var reply = await fresh.FileAsync(fixture.Identity.ArchiveId.ToString(), fixture.Identity.IndexSha256, original.Path, original.Sha256, bytes);
            Assert.Equal("archive-file", reply.Status); Assert.InRange(Encoding.UTF8.GetByteCount(reply.ToJson()), 1, 131072);
            var response = Assert.IsType<TraversalArchiveReadPage>(reply.Data); rebuilt.Write(response.Page.Data);
            if (response.Page.NextOffset is not { } next) break; bytes = next;
        }
        Assert.Equal(original.Sha256, TraversalArchive.Hash(rebuilt.ToArray())); Assert.Equal(fixture.Originals[original.Path], rebuilt.ToArray());
        fixture.Project.WorkspaceHidden = true; var before = fixture.Store.Reads; await using var denied = new HostedArchiveClient(options);
        Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", (await denied.FileAsync(fixture.Identity.ArchiveId.ToString(), fixture.Identity.IndexSha256, original.Path, original.Sha256)).ErrorCode);
        Assert.Equal(before, fixture.Store.Reads);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(65535, false)]
    [InlineData(65536, false)]
    [InlineData(65537, false)]
    public async Task ValidFilePageAndEofBoundariesRetainExactCursorAndDigest(int size, bool eof)
    {
        var original = Enumerable.Range(0, size).Select(n => (byte)(n % 251)).ToArray();
        var digest = TraversalArchive.Hash(original); var offset = eof ? size : 0;
        var data = original.Skip(offset).Take(65536).ToArray(); var next = offset + data.Length;
        var page = new TraversalArchiveReadPage(new(Identity, "original.bin", size, digest, offset, data, next < size ? next : null), TraversalArchive.Hash(data));
        var handler = new Handler(_ => Task.FromResult(Json(page))); await using var client = new HostedArchiveClient(Options(), handler);
        var reply = await client.FileAsync(Archive.ToString(), Index, "original.bin", digest, offset);
        Assert.Equal("archive-file", reply.Status); var result = Assert.IsType<TraversalArchiveReadPage>(reply.Data);
        Assert.Equal(data, result.Page.Data); Assert.Equal(page.Page.NextOffset, result.Page.NextOffset);
        Assert.Equal(digest, result.Page.FileSha256); Assert.InRange(Encoding.UTF8.GetByteCount(reply.ToJson()), 1, 131072);
    }

    [Fact]
    public async Task ExactInventoryEndReturnsEmptyMetadataWithoutInventingMissingFiles()
    {
        var handler = new Handler(_ => Task.FromResult(Json(Manifest() with { Offset = 1, Files = ImmutableArray<TraversalArchiveEntry>.Empty })));
        await using var client = new HostedArchiveClient(Options(), handler);
        var reply = await client.ManifestAsync(Archive.ToString(), Index, 1); Assert.Equal("archive-manifest", reply.Status);
        var page = Assert.IsType<TraversalArchiveManifestPage>(reply.Data); Assert.Empty(page.Files); Assert.Null(page.NextOffset); Assert.Equal(1, page.TotalFiles);
    }
}
