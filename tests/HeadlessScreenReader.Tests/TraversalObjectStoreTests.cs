using System.Net;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalObjectStoreTests
{
    private static string Root() => Path.GetFullPath(Path.Combine("artifacts", "traversal-archive-tests", Guid.NewGuid().ToString("N")));
    private static readonly byte[] Original = "original opaque object"u8.ToArray();
    private static TraversalArchiveIdentity Identity() => new(Guid.NewGuid(), Guid.NewGuid(), TraversalArchive.Hash(Original));

    [Fact]
    public async Task FileStore_ReconnectReadsExactBytesAndCorruptExistingObjectIsNotOverwritten()
    {
        var root = Root(); var id = Identity(); var store = new FileTraversalObjectStore(root);
        await store.WriteAsync(id, null, Original, default);
        Assert.Equal(Original, await new FileTraversalObjectStore(root).ReadAsync(id, null, default));
        await store.WriteAsync(id, null, Original, default);
        var path = Path.Combine(root, TraversalArchive.ObjectKey(id, null).Replace('/', Path.DirectorySeparatorChar));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        await File.WriteAllTextAsync(path, "corrupt");
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => store.WriteAsync(id, null, Original, default))).Failure);
        Assert.Equal("corrupt", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FileStore_UsesScopedKeysAndNeverPublishesCancelledOrMismatchedBytes()
    {
        var root = Root(); var id = Identity(); var store = new FileTraversalObjectStore(root);
        Assert.Null(await store.ReadAsync(id, null, default));
        await Assert.ThrowsAsync<TraversalArchiveException>(() => store.WriteAsync(id, null, [1], default));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync(id, null, Original, stop.Token));
        Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        await store.WriteAsync(id, null, Original, default);
        Assert.Null(await store.ReadAsync(id with { OrganizationId = Guid.NewGuid() }, null, default));
    }

    [Fact]
    public async Task FileStore_RejectsOversizedOriginalBeforeAllocatingItsContents()
    {
        var root = Root(); var id = Identity(); var store = new FileTraversalObjectStore(root);
        var path = Path.Combine(root, TraversalArchive.ObjectKey(id, null).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = File.Create(path)) file.SetLength(TraversalArchive.ObjectBytes + 1);
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => store.ReadAsync(id, null, default))).Failure);
    }

    private sealed class Transport : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public List<(string Method, string Path, string? Authorization, string? MediaType, byte[] Bytes)> Calls { get; } = [];
        public HttpStatusCode? GetStatus { get; set; }
        public HttpStatusCode PutStatus { get; set; } = HttpStatusCode.OK;
        public bool LoseWrite { get; set; }
        public bool OmitContentLength { get; set; }
        public Action? AfterGet { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(token);
            Calls.Add((request.Method.Method, path, request.Headers.TryGetValues("Authorization", out var authorization) ? authorization.Single() : null,
                request.Content?.Headers.ContentType?.MediaType, bytes));
            if (request.Method == HttpMethod.Put)
            {
                if (!LoseWrite && PutStatus == HttpStatusCode.OK) Values[path] = bytes.ToArray();
                return new(PutStatus);
            }
            AfterGet?.Invoke();
            if (GetStatus is { } status) return new(status);
            if (!Values.TryGetValue(path, out var value)) return new(HttpStatusCode.NotFound);
            HttpContent content = OmitContentLength ? new StreamContent(new MemoryStream(value)) : new ByteArrayContent(value);
            return new(HttpStatusCode.OK) { Content = content };
        }
    }
    private static S3TraversalObjectStore S3(Transport transport) => new(
        S3EvidenceConfiguration.Optional("s3-test-access-synthetic", "s3-test-secret-never-log-this-canary")!, transport);
    private static string Address(TraversalArchiveIdentity id, string? part = null) => "/storage/v1/s3/hsr-evidence/" + TraversalArchive.ObjectKey(id, part);

    [Fact]
    public async Task S3Store_SignsScopedBinaryObjectsAndReusesOnlyVerifiedOriginals()
    {
        var id = Identity(); var transport = new Transport(); using var store = S3(transport);
        var digest = TraversalArchive.Hash(Original);
        await store.WriteAsync(id, digest, Original, default);
        Assert.Equal(new[] { "GET", "PUT", "GET" }, transport.Calls.Select(c => c.Method));
        Assert.All(transport.Calls, c => { Assert.Equal(Address(id, digest), c.Path); Assert.StartsWith("AWS4-HMAC-SHA256 ", c.Authorization); });
        Assert.Equal("application/octet-stream", transport.Calls[1].MediaType);
        Assert.Equal(Original, transport.Calls[1].Bytes);
        await store.WriteAsync(id, digest, Original, default);
        Assert.Single(transport.Calls, c => c.Method == "PUT");
        Assert.Equal(Original, await store.ReadAsync(id, digest, default));
        Assert.DoesNotContain("s3-test-secret-never-log-this-canary", JsonSerializer.Serialize(transport.Calls));
    }

    [Theory]
    [InlineData("wrong-bytes")]
    [InlineData("oversized-declared")]
    [InlineData("oversized-stream")]
    public async Task S3Store_RejectsCorruptOrOversizedReadsWithoutOverwriting(string scenario)
    {
        var id = Identity(); var transport = new Transport { OmitContentLength = scenario == "oversized-stream" };
        transport.Values[Address(id)] = scenario == "wrong-bytes" ? [8] : new byte[TraversalArchive.ObjectBytes + 1];
        using var store = S3(transport);
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => store.WriteAsync(id, null, Original, default))).Failure);
        Assert.All(transport.Calls, c => Assert.Equal("GET", c.Method));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task S3Store_AuthorityAndServerFailuresAreUnavailableRatherThanMissing(HttpStatusCode status)
    {
        var transport = new Transport { GetStatus = status }; using var store = S3(transport);
        Assert.Equal(TraversalArchiveFailure.Unavailable, (await Assert.ThrowsAsync<TraversalArchiveException>(() => store.ReadAsync(Identity(), null, default))).Failure);
        Assert.Single(transport.Calls);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("lost")]
    public async Task S3Store_AcknowledgementWithoutSuccessfulReadbackIsWriteUnconfirmed(string scenario)
    {
        var transport = new Transport { PutStatus = scenario == "rejected" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK, LoseWrite = true };
        using var store = S3(transport);
        Assert.Equal(TraversalArchiveFailure.WriteUnconfirmed, (await Assert.ThrowsAsync<TraversalArchiveException>(() => store.WriteAsync(Identity(), null, Original, default))).Failure);
        Assert.Empty(transport.Values); Assert.Single(transport.Calls, c => c.Method == "PUT");
    }

    [Fact]
    public async Task S3Store_CancelledLateTransportCannotPublishSuccess()
    {
        using var stop = new CancellationTokenSource(); var id = Identity();
        var transport = new Transport { AfterGet = stop.Cancel }; transport.Values[Address(id)] = Original;
        using var store = S3(transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync(id, null, stop.Token));
        Assert.Single(transport.Calls);
    }
}
