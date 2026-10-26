using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ArchiveRetrievalHttpTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(65)]
    public async Task InventoryPagingRetainsExactIdentityOrderTotalsAndEnd(int originalFiles)
    {
        var fixture = await ArchiveHttpFixture.Create(originalFiles);
        await using var server = await fixture.Start(); using var http = PortalHttpTests.Authorized(server);
        var files = new List<TraversalArchiveEntry>(); var offset = 0;
        while (true)
        {
            using var response = await http.PostAsJsonAsync(fixture.Path + "/manifest", new { fixture.Identity.IndexSha256, offset });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
            var page = (await response.Content.ReadFromJsonAsync<TraversalArchiveManifestPage>())!;
            Assert.Equal(fixture.Identity, page.Identity); Assert.Equal(fixture.ManifestHash, page.SourceManifestSha256);
            Assert.Equal(originalFiles + 1, page.TotalFiles); Assert.Equal(fixture.Originals.Sum(p => p.Value.Length), page.TotalBytes);
            Assert.Equal(offset, page.Offset); Assert.Equal(Math.Min(32, page.TotalFiles - offset), page.Files.Length);
            Assert.InRange((await response.Content.ReadAsByteArrayAsync()).Length, 1, 32768);
            files.AddRange(page.Files); if (page.NextOffset is not { } next) break; Assert.Equal(offset + 32, next); offset = next;
        }
        Assert.Equal(fixture.Originals.Keys.Order(StringComparer.Ordinal), files.Select(f => f.Path));
        Assert.All(files, file => Assert.Equal(TraversalArchive.Hash(fixture.Originals[file.Path]), file.Sha256));
        using var eof = await http.PostAsJsonAsync(fixture.Path + "/manifest", new { fixture.Identity.IndexSha256, offset = originalFiles + 1 });
        var empty = (await eof.Content.ReadFromJsonAsync<TraversalArchiveManifestPage>())!;
        Assert.Empty(empty.Files); Assert.Null(empty.NextOffset);
        Assert.Equal(fixture.Project.AuthCalls * 2, fixture.Project.DataCalls);
        Assert.All(fixture.Project.SeenBearers, bearer => Assert.Equal(PortalProjectClientTests.Bearer, bearer));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(4194305)]
    public async Task FilePagesReconstructExactOriginalAcrossFreshClientsAndObjectBoundary(int bytes)
    {
        var fixture = await ArchiveHttpFixture.Create(1, bytes); await using var server = await fixture.Start();
        using var rebuilt = new MemoryStream(); long offset = 0;
        while (true)
        {
            // New consumer state for every page: reconnect cannot change content identity.
            using var http = PortalHttpTests.Authorized(server);
            using var response = await http.PostAsJsonAsync(fixture.Path + "/file", new { fixture.Identity.IndexSha256, path = "original-000.bin", offset });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = (await response.Content.ReadFromJsonAsync<TraversalArchiveReadPage>())!;
            Assert.Equal(fixture.Identity, page.Page.Identity); Assert.Equal(offset, page.Page.Offset); Assert.Equal(bytes, page.Page.FileBytes);
            Assert.Equal(TraversalArchive.Hash(page.Page.Data), page.PageSha256);
            Assert.Equal(TraversalArchive.Hash(fixture.Originals["original-000.bin"]), page.Page.FileSha256);
            Assert.Equal(Math.Min(65536, bytes - offset), page.Page.Data.Length);
            Assert.InRange((await response.Content.ReadAsByteArrayAsync()).Length, 1, 100000);
            rebuilt.Write(page.Page.Data);
            if (page.Page.NextOffset is not { } next) break; Assert.Equal(offset + 65536, next); offset = next;
        }
        Assert.Equal(fixture.Originals["original-000.bin"], rebuilt.ToArray());
        using var endClient = PortalHttpTests.Authorized(server);
        using var eof = await endClient.PostAsJsonAsync(fixture.Path + "/file", new { fixture.Identity.IndexSha256, path = "original-000.bin", offset = bytes });
        var end = (await eof.Content.ReadFromJsonAsync<TraversalArchiveReadPage>())!;
        Assert.Empty(end.Page.Data); Assert.Null(end.Page.NextOffset);
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("expired", 401)]
    [InlineData("revoked", 404)]
    [InlineData("foreign", 404)]
    public async Task AuthorizationFailurePreventsStorageForEveryOperation(string failure, int status)
    {
        var fixture = await ArchiveHttpFixture.Create(); await using var server = await fixture.Start();
        using var http = PortalHttpTests.Authorized(server);
        if (failure == "anonymous") http.DefaultRequestHeaders.Authorization = null;
        if (failure == "expired") fixture.Project.Revoked = true;
        if (failure == "revoked") fixture.Project.WorkspaceHidden = true;
        var path = failure == "foreign" ? fixture.Path.Replace(PortalProjectClientTests.Organization.ToString("D"), Guid.NewGuid().ToString("D")) : fixture.Path;
        foreach (var operation in new[] { "manifest", "file" })
        {
            using var response = await http.PostAsJsonAsync(path + "/" + operation, operation == "manifest"
                ? (object)new { fixture.Identity.IndexSha256 } : new { fixture.Identity.IndexSha256, path = "original-000.bin" });
            Assert.Equal(status, (int)response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.DoesNotContain(PortalProjectClientTests.Bearer, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, fixture.Store.Reads);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("file")]
    public async Task MembershipLossDuringReadSuppressesAlreadyReadBytes(string operation)
    {
        var fixture = await ArchiveHttpFixture.Create(); await using var server = await fixture.Start();
        using var http = PortalHttpTests.Authorized(server);
        fixture.Store.BeforeRead = () => fixture.Project.WorkspaceHidden = true;
        using var response = await http.PostAsJsonAsync(fixture.Path + "/" + operation, operation == "manifest"
            ? (object)new { fixture.Identity.IndexSha256 } : new { fixture.Identity.IndexSha256, path = "original-000.bin" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.True(fixture.Store.Reads > 0);
        var text = await response.Content.ReadAsStringAsync(); Assert.Contains("CONTROL_WORKSPACE_NOT_FOUND", text); Assert.DoesNotContain("data", text);
    }

    [Theory]
    [InlineData("missing-index", 404, "CONTROL_ARCHIVE_NOT_FOUND")]
    [InlineData("missing-part", 404, "CONTROL_ARCHIVE_NOT_FOUND")]
    [InlineData("corrupt-index", 409, "CONTROL_ARCHIVE_INVALID")]
    [InlineData("corrupt-part", 409, "CONTROL_ARCHIVE_INVALID")]
    [InlineData("unavailable", 503, "CONTROL_ARCHIVE_UNAVAILABLE")]
    [InlineData("unconfigured", 503, "CONTROL_ARCHIVE_UNAVAILABLE")]
    public async Task MissingCorruptAndUnavailableOriginalsAreErrorsWithoutRepair(string failure, int status, string code)
    {
        var fixture = await ArchiveHttpFixture.Create();
        if (failure.StartsWith("missing", StringComparison.Ordinal) || failure.StartsWith("corrupt", StringComparison.Ordinal))
        {
            var key = failure.EndsWith("index", StringComparison.Ordinal) ? TraversalArchive.ObjectKey(fixture.Identity, null)
                : TraversalArchive.ObjectKey(fixture.Identity, TraversalArchive.Hash(fixture.Originals["original-000.bin"]));
            if (failure.StartsWith("missing", StringComparison.Ordinal)) fixture.Store.Objects.Remove(key); else fixture.Store.Objects[key][0] ^= 1;
        }
        if (failure == "unavailable") fixture.Store.Failure = TraversalArchiveFailure.Unavailable;
        var writes = fixture.Store.Writes; await using var server = await fixture.Start(failure != "unconfigured");
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.PostAsJsonAsync(fixture.Path + "/file", new { fixture.Identity.IndexSha256, path = "original-000.bin" });
        Assert.Equal(status, (int)response.StatusCode); var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(code, text); Assert.DoesNotContain("fileSha256", text); Assert.Equal(writes, fixture.Store.Writes);
    }

    [Theory]
    [InlineData("digest", 400)]
    [InlineData("negative", 400)]
    [InlineData("beyond-manifest", 400)]
    [InlineData("missing-digest", 400)]
    [InlineData("unknown-member", 400)]
    [InlineData("query", 400)]
    [InlineData("media", 415)]
    [InlineData("traversal", 409)]
    [InlineData("offset", 409)]
    [InlineData("unlisted", 404)]
    [InlineData("stale", 404)]
    public async Task InvalidRequestsNeverReturnAnInventedPage(string failure, int status)
    {
        var fixture = await ArchiveHttpFixture.Create(); await using var server = await fixture.Start();
        using var http = PortalHttpTests.Authorized(server); var operation = "manifest"; var path = fixture.Path;
        object body = new { fixture.Identity.IndexSha256 };
        switch (failure)
        {
            case "digest": body = new { indexSha256 = "bad" }; break;
            case "negative": body = new { fixture.Identity.IndexSha256, offset = -1 }; break;
            case "beyond-manifest": body = new { fixture.Identity.IndexSha256, offset = 3 }; break;
            case "missing-digest": body = new { }; break;
            case "unknown-member": body = new { fixture.Identity.IndexSha256, user = "invented" }; break;
            case "traversal": operation = "file"; body = new { fixture.Identity.IndexSha256, path = "../original-000.bin" }; break;
            case "offset": operation = "file"; body = new { fixture.Identity.IndexSha256, path = "original-000.bin", offset = 1 }; break;
            case "unlisted": operation = "file"; body = new { fixture.Identity.IndexSha256, path = "unlisted.bin" }; break;
            case "stale": body = new { indexSha256 = new string('b', 64) }; break;
        }
        path += "/" + operation + (failure == "query" ? "?token=denied" : "");
        using var response = failure == "media" ? await http.PostAsync(path, new StringContent("{}")) : await http.PostAsJsonAsync(path, body);
        Assert.Equal(status, (int)response.StatusCode); Assert.DoesNotContain("files", await response.Content.ReadAsStringAsync());
    }
}

internal sealed class ArchiveHttpFixture
{
    public PortalProjectStub Project { get; } = new();
    public ArchiveMemoryStore Store { get; } = new();
    public Dictionary<string, byte[]> Originals { get; } = new(StringComparer.Ordinal);
    public TraversalArchiveIdentity Identity { get; private set; } = null!;
    public string ManifestHash => TraversalArchive.Hash(Originals[TraversalArchive.SourceManifest]);
    public string Path => $"/api/workspaces/{Identity.OrganizationId:D}/archives/{Identity.ArchiveId:D}";
    public static async Task<ArchiveHttpFixture> Create(int count = 1, int bytes = 65537)
    {
        var result = new ArchiveHttpFixture(); result.Originals.Add(TraversalArchive.SourceManifest, Encoding.UTF8.GetBytes("{\"synthetic\":true,\"goalVerified\":false}"));
        for (var i = 0; i < count; i++) result.Originals.Add($"original-{i:000}.bin", Enumerable.Range(0, bytes).Select(n => (byte)(n % 251)).ToArray());
        var receipt = await new TraversalArchive(result.Store).FinalizeAsync(PortalProjectClientTests.Organization, Guid.NewGuid(), result.ManifestHash,
            result.Originals.Select(p => new TraversalArchiveSource(p.Key, p.Value.Length, TraversalArchive.Hash(p.Value), () => new MemoryStream(p.Value, writable: false))).ToArray());
        result.Identity = receipt.Identity; result.Store.Reads = 0; return result;
    }
    public Task<HostedControlServer> Start(bool configured = true) => HostedControlServer.StartAsync(
        new(PortalProjectClientTests.Key, System.IO.Path.GetFullPath("artifacts/portal-validation/evidence"), new string('a', 40), 0),
        Project, new PortalObservationStub(), traversalArchive: configured ? new TraversalArchive(Store) : null);
}

internal sealed class ArchiveMemoryStore : ITraversalObjectStore
{
    public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
    public int Reads { get; set; }
    public int Writes { get; private set; }
    public Action? BeforeRead { get; set; }
    public TraversalArchiveFailure? Failure { get; set; }
    public Task<byte[]?> ReadAsync(TraversalArchiveIdentity identity, string? partSha256, CancellationToken token)
    {
        Reads++; BeforeRead?.Invoke(); if (Failure is { } failure) throw new TraversalArchiveException(failure);
        return Task.FromResult(Objects.TryGetValue(TraversalArchive.ObjectKey(identity, partSha256), out var bytes) ? bytes.ToArray() : null);
    }
    public Task WriteAsync(TraversalArchiveIdentity identity, string? partSha256, byte[] bytes, CancellationToken token)
    { Writes++; Objects[TraversalArchive.ObjectKey(identity, partSha256)] = bytes.ToArray(); return Task.CompletedTask; }
}
