using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class HostedSourceImageValidationTests
{
    private static readonly FixtureJob Job = new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1)
    { RecordObservation = true, TraversalRoute = 1 };
    private static readonly Guid Producer = Guid.NewGuid();
    private static readonly byte[] Bytes = Content();
    private static byte[] Content()
    {
        var bytes = Enumerable.Range(0, RetainedSourceImagePage.PageBytes + 7).Select(value => (byte)(value % 239)).ToArray();
        bytes[0] = 0xff; bytes[1] = 0xd8; bytes[2] = 0xff; return bytes;
    }
    private static RetainedSourceImages Manifest => new(RetainedSourceImages.CurrentSchema, Job, Producer, new string('b', 64),
        [new(1, "source-0001.jpg", 1200.25, Bytes.Length, TraversalArchive.Hash(Bytes))], Bytes.Length, false);
    private static PortalScanResponse Scan => new(Job.OrganizationId, Job.ScanId, "synthetic-shop", FixtureObservation.Profile, "blocked", DateTimeOffset.Parse("2026-10-31T00:00:00Z"));
    private static HostedReaderOptions Options => new(new("http://127.0.0.1:6086/"), Job.OrganizationId, PortalProjectClientTests.Bearer);
    private sealed class Handler(JsonNode body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), Headers = { CacheControl = new() { NoStore = true } } });
        }
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("generation")]
    [InlineData("producer")]
    [InlineData("prefix")]
    [InlineData("sequence")]
    [InlineData("image-hash")]
    [InlineData("offset")]
    [InlineData("short-data")]
    [InlineData("page-hash")]
    [InlineData("next")]
    [InlineData("completed")]
    [InlineData("replay")]
    [InlineData("goal")]
    [InlineData("completeness")]
    [InlineData("scope")]
    [InlineData("content-type")]
    [InlineData("native-wrapper")]
    [InlineData("timestamp")]
    [InlineData("path")]
    [InlineData("size")]
    public async Task ForgedImagePageCannotChangeRequestedScopeBytesCursorOrSubsetAuthority(string change)
    {
        var manifest = Manifest; var page = SourceImagesProjection.Page(manifest, 1, Bytes, 0);
        var tree = JsonSerializer.SerializeToNode(page, PortalJson.Options)!;
        var source = tree["source"]!; var image = source["image"]!;
        switch (change)
        {
            case "organization": source["job"]!["organizationId"] = Guid.NewGuid(); break;
            case "scan": source["job"]!["scanId"] = Guid.NewGuid(); break;
            case "generation": source["job"]!["generation"] = 0; break;
            case "producer": source["workerId"] = Guid.Empty; break;
            case "prefix": source["prefixSha256"] = new string('c', 64); break;
            case "sequence": image["sourceSequence"] = 2; image["file"] = "source-0002.jpg"; break;
            case "image-hash": image["sha256"] = new string('c', 64); break;
            case "offset": tree["offset"] = 1; break;
            case "short-data": tree["data"] = Convert.ToBase64String(page.Data[..^1]); break;
            case "page-hash": tree["pageSha256"] = new string('c', 64); break;
            case "next": tree["nextOffset"] = 0; break;
            case "completed": source["isCompletedRecording"] = true; break;
            case "replay": source["canReplay"] = true; break;
            case "goal": source["goalVerification"] = "Satisfied"; break;
            case "completeness": source["completeness"] = "Complete"; break;
            case "scope": source["scope"] = "OriginalVideo"; break;
            case "content-type": image["contentType"] = "image/svg+xml"; break;
            case "native-wrapper": source["native"] = new JsonObject { ["owner"] = Guid.NewGuid() }; break;
            case "timestamp": image["presentationUnixMilliseconds"] = 0; break;
            case "path": image["file"] = "../source-0001.jpg"; break;
            case "size": image["bytes"] = RetainedSourceImageFile.MaximumBytes + 1; break;
        }
        var handler = new Handler(tree); await using var client = new HostedScanClient(Options, handler);
        var result = await client.SourceImageAsync(Job.ScanId.ToString("D"), manifest.PrefixSha256, 1, manifest.Files[0].Sha256);
        Assert.Equal(change == "native-wrapper" ? "HOSTED_SCAN_OPERATION_UNCONFIRMED" : "HOSTED_RESPONSE_INVALID", result.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("no-sources")]
    [InlineData("missing-with-sources")]
    [InlineData("empty-with-images")]
    [InlineData("unknown-state")]
    [InlineData("outer-producer")]
    [InlineData("outer-job")]
    [InlineData("outer-generation")]
    [InlineData("schema")]
    [InlineData("duplicate")]
    [InlineData("replay")]
    [InlineData("not-started-producer")]
    [InlineData("not-started-generation")]
    public async Task ManifestAvailabilityMustAgreeWithActualAttemptAndSubsetMetadata(string change)
    {
        var tree = JsonSerializer.SerializeToNode(new PortalSourceImagesResponse(Scan, Job.JobId, 1, Producer, "Available", Manifest), PortalJson.Options)!;
        switch (change)
        {
            case "no-sources": tree["sources"] = null; break;
            case "missing-with-sources": tree["sourceState"] = "NotRetained"; break;
            case "empty-with-images": tree["sourceState"] = "Empty"; break;
            case "unknown-state": tree["sourceState"] = "Complete"; break;
            case "outer-producer": tree["workerId"] = Guid.NewGuid(); break;
            case "outer-job": tree["jobId"] = Guid.NewGuid(); break;
            case "outer-generation": tree["generation"] = 2; break;
            case "schema": tree["sources"]!["schema"] = "unrelated"; break;
            case "duplicate": tree["sources"]!["files"]!.AsArray().Add(tree["sources"]!["files"]![0]!.DeepClone()); break;
            case "replay": tree["sources"]!["canReplay"] = true; break;
            case "not-started-producer": tree["sources"] = null; tree["sourceState"] = "NotStarted"; tree["generation"] = 0; break;
            case "not-started-generation": tree["sources"] = null; tree["sourceState"] = "NotStarted"; tree["workerId"] = null; break;
        }
        var handler = new Handler(tree); await using var client = new HostedScanClient(Options, handler);
        Assert.Equal("HOSTED_RESPONSE_INVALID", (await client.SourceManifestAsync(Job.ScanId.ToString("D"))).ErrorCode); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ExactEofRemainsAnEmptyFreshlyAuthorizedPageWithTheSameImageIdentity()
    {
        var page = SourceImagesProjection.Page(Manifest, 1, Bytes, Bytes.Length);
        var handler = new Handler(JsonSerializer.SerializeToNode(page, PortalJson.Options)!);
        await using var client = new HostedScanClient(Options, handler);
        var reply = await client.SourceImageAsync(Job.ScanId.ToString("D"), Manifest.PrefixSha256, 1, Manifest.Files[0].Sha256, Bytes.Length);
        Assert.Null(reply.ErrorCode); Assert.Equal(1, handler.Calls);
        using var json = JsonDocument.Parse(reply.ToJson()); var returned = json.RootElement.GetProperty("data").Deserialize<RetainedSourceImagePage>(PortalJson.Options)!;
        Assert.Empty(returned.Data); Assert.Null(returned.NextOffset); Assert.Equal(TraversalArchive.Hash([]), returned.PageSha256);
        Assert.Equal(Manifest.Files[0], returned.Source.Image); Assert.False(returned.Source.IsCompletedRecording);
    }

    [Fact]
    public async Task MaximumManifestAndImagePagesStayInsideTheRealToolPayloadBudget()
    {
        var files = Enumerable.Range(1, RetainedSourceImages.MaximumFrames).Select(index =>
            new RetainedSourceImageFile(index, $"source-{index:D4}.jpg", 1200.25 + index, 4, new string('a', 64))).ToImmutableArray();
        var maximum = Manifest with { Files = files, Bytes = 4L * files.Length, HasUncommittedFiles = true }; maximum.Validate();
        var handler = new Handler(JsonSerializer.SerializeToNode(new PortalSourceImagesResponse(Scan, Job.JobId, 1, Producer, "Available", maximum), PortalJson.Options)!);
        await using var client = new HostedScanClient(Options, handler);
        var response = await client.SourceManifestAsync(Job.ScanId.ToString("D")); Assert.Null(response.ErrorCode);
        var manifestJson = response.ToJson(); Assert.True(Encoding.UTF8.GetByteCount(manifestJson) < 131072);
        using var parsed = JsonDocument.Parse(manifestJson); var sources = parsed.RootElement.GetProperty("data").GetProperty("sources");
        Assert.Equal(250, sources.GetProperty("files").GetArrayLength()); Assert.True(sources.GetProperty("hasUncommittedFiles").GetBoolean());
        Assert.False(sources.GetProperty("isCompletedRecording").GetBoolean());
        var page = SourceImagesProjection.Page(Manifest, 1, Bytes, 0);
        var pageHandler = new Handler(JsonSerializer.SerializeToNode(page, PortalJson.Options)!);
        await using var paging = new HostedScanClient(Options, pageHandler);
        var result = await paging.SourceImageAsync(Job.ScanId.ToString("D"), Manifest.PrefixSha256, 1, Manifest.Files[0].Sha256);
        Assert.Null(result.ErrorCode); Assert.True(Encoding.UTF8.GetByteCount(result.ToJson()) < 131072);
        Assert.Equal(1, pageHandler.Calls);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("prefix")]
    [InlineData("image")]
    [InlineData("zero-sequence")]
    [InlineData("large-sequence")]
    [InlineData("negative-offset")]
    [InlineData("large-offset")]
    public async Task InvalidSourceArgumentsCannotReachUpstreamOrStartARetry(string change)
    {
        var handler = new Handler(new JsonObject()); await using var client = new HostedScanClient(Options, handler);
        var reply = await client.SourceImageAsync(change == "scan" ? "not-a-scan" : Job.ScanId.ToString("D"),
            change == "prefix" ? "bad" : Manifest.PrefixSha256, change == "zero-sequence" ? 0 : change == "large-sequence" ? 251 : 1,
            change == "image" ? "bad" : Manifest.Files[0].Sha256, change == "negative-offset" ? -1 : change == "large-offset" ? RetainedSourceImageFile.MaximumBytes + 1 : 0);
        Assert.NotNull(reply.ErrorCode); Assert.Equal(0, handler.Calls); Assert.Equal("error", reply.Status);
    }
}
