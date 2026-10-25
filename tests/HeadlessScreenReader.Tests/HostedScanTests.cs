using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid ScanId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static HostedReaderOptions Options() => new(new("http://127.0.0.1:6086/"), Organization, "scan-original-bearer-canary");
    private static object Scan(string state = "observed", Guid? id = null) => new
    { organizationId = Organization, id = id ?? ScanId, fixture = "synthetic-shop", readerProfile = ScreenReaderSession.Profile, state, createdAt = "2026-10-25T00:00:00Z" };
    private static object Admission() => new { organizationId = Organization, scanId = ScanId, jobId = JobId };
    private static object Node(string id = "heading", int characters = 0) => new
    {
        id,
        parentId = (string?)null,
        role = "heading",
        name = characters == 0 ? "Synthetic shop" : new('x', characters),
        focused = false,
        disabled = false,
        headingLevel = 1,
        @checked = (string?)null,
        expanded = (bool?)null,
        selected = (bool?)null,
        required = (bool?)null,
        requiredSource = (string?)null,
        description = (string?)null
    };
    private static object Observation(string storage = "S3", object[]? nodes = null) => new
    { documentId = "document", digest = new string('a', 64), readerProfile = ScreenReaderSession.Profile, dispatchedInputs = 0, goalVerified = false, nodes = nodes ?? [Node()], limitations = new[] { "Bounded synthetic fixture" }, evidenceStorage = storage };
    private static object Report(object? observation = null, string evidence = "Available", string state = "observed", string? error = null) => new
    { scan = Scan(state), evidenceState = evidence, errorCode = error, observation = evidence == "Available" ? observation ?? Observation() : observation };
    private static JsonElement Data(ToolReply reply) { using var json = JsonDocument.Parse(reply.ToJson()); return json.RootElement.GetProperty("data").Clone(); }
    private static JsonNode Tree(object value) => JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static HttpResponseMessage Json(object value, int status = 200) => new((HttpStatusCode)status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json"),
        Headers = { CacheControl = new() { NoStore = true } }
    };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Bearer, string? Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await send(request);
        }
    }
    private static Handler Queue(params Func<HttpResponseMessage>[] responses)
    {
        var queue = new Queue<Func<HttpResponseMessage>>(responses); return new(_ => Task.FromResult(queue.Dequeue()()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../other")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidRequestAndScanIdentitiesNeverReachHttp(string id)
    {
        var handler = Queue(); await using var client = new HostedScanClient(Options(), handler);
        Assert.Equal("SCAN_REQUEST_ID_REQUIRED", (await client.StartFixtureAsync(id)).ErrorCode);
        Assert.Equal("SCAN_ID_REQUIRED", (await client.ReportAsync(id)).ErrorCode); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExplicitAdmissionRetryUsesSameRequestAndReturnsOnlyAdmissionFacts()
    {
        var handler = Queue(() => Json(Admission(), 201), () => Json(Admission(), 201));
        await using var client = new HostedScanClient(Options(), handler); var requestId = Guid.NewGuid();
        var one = await client.StartFixtureAsync(requestId.ToString()); var two = await client.StartFixtureAsync(requestId.ToString("N"));
        Assert.Equal("scan-admitted", one.Status); Assert.Equal(ScanId, Data(two).GetProperty("scanId").GetGuid());
        Assert.Equal("NotEstablishedByAdmission", Data(one).GetProperty("execution").GetString());
        Assert.Equal("NotEstablishedByAdmission", Data(one).GetProperty("evidence").GetString());
        Assert.Equal("NotAssessed", Data(one).GetProperty("goalVerification").GetString());
        Assert.True(Data(one).GetProperty("workerContinuesAfterDisconnect").GetBoolean()); Assert.Null(one.SessionId);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("POST", r.Method); Assert.Equal($"/api/workspaces/{Organization:D}/scans", r.Path); Assert.Equal("scan-original-bearer-canary", r.Bearer);
            using var body = JsonDocument.Parse(r.Body!); Assert.Single(body.RootElement.EnumerateObject()); Assert.Equal(requestId, body.RootElement.GetProperty("requestId").GetGuid());
        });
        Assert.DoesNotContain("scan-original-bearer-canary", one.ToJson());
    }

    [Fact]
    public async Task LostAdmissionNeverRetriesOrGeneratesReplacementRequest()
    {
        var count = 0; var handler = new Handler(_ => ++count == 1 ? throw new HttpRequestException("scan-original-bearer-canary") : Task.FromResult(Json(Admission(), 201)));
        await using var client = new HostedScanClient(Options(), handler); var requestId = Guid.NewGuid();
        var unconfirmed = await client.StartFixtureAsync(requestId.ToString());
        Assert.Equal("HOSTED_SCAN_OPERATION_UNCONFIRMED", unconfirmed.ErrorCode); Assert.Single(handler.Requests);
        Assert.Equal(requestId, Data(unconfirmed).GetProperty("retryRequestId").GetGuid()); Assert.False(Data(unconfirmed).GetProperty("automaticRetry").GetBoolean());
        Assert.DoesNotContain("scan-original-bearer-canary", unconfirmed.ToJson());
        Assert.Equal("scan-admitted", (await client.StartFixtureAsync(requestId.ToString())).Status);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
    }

    [Fact]
    public async Task CancellationJoinsAdmissionAndDisposalDoesNotCancelDurableJob()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(_ => { entered.SetResult(); return finish.Task; });
        var client = new HostedScanClient(Options(), handler); using var cancellation = new CancellationTokenSource();
        var admission = client.StartFixtureAsync(Guid.NewGuid().ToString(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel(); var disposing = client.DisposeAsync().AsTask();
        Assert.False(admission.IsCompleted); Assert.False(disposing.IsCompleted); finish.SetResult(Json(Admission(), 201));
        Assert.Equal("scan-admitted", (await admission).Status); await disposing;
        Assert.Single(handler.Requests); Assert.Equal("POST", handler.Requests[0].Method);
        Assert.Equal("WORKSPACE_CLOSED", (await client.ListAsync()).ErrorCode);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("missing")]
    public async Task InvalidAdmissionReceiptsRetainCallerRequestIdentity(string failure)
    {
        var receipt = Tree(Admission());
        switch (failure)
        {
            case "scope": receipt["organizationId"] = Guid.NewGuid(); break;
            case "scan": receipt["scanId"] = Guid.Empty; break;
            case "job": receipt["jobId"] = Guid.Empty; break;
            default: receipt.AsObject().Remove("jobId"); break;
        }
        var handler = Queue(() => Json(receipt, 201)); await using var client = new HostedScanClient(Options(), handler); var id = Guid.NewGuid();
        var reply = await client.StartFixtureAsync(id.ToString()); Assert.Equal("error", reply.Status);
        Assert.Equal(id, Data(reply).GetProperty("requestId").GetGuid()); Assert.Equal(id, Data(reply).GetProperty("retryRequestId").GetGuid()); Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(50, false)]
    [InlineData(50, true)]
    public async Task RecentListRetainsBoundAndExplicitOmittedHistory(int count, bool hasMore)
    {
        var items = Enumerable.Range(0, count).Select(_ => Scan(id: Guid.NewGuid())).ToArray();
        var handler = Queue(() => Json(new { items, hasMore })); await using var client = new HostedScanClient(Options(), handler);
        var reply = await client.ListAsync(); Assert.Equal("scan-list", reply.Status);
        Assert.Equal(count, Data(reply).GetProperty("items").GetArrayLength()); Assert.Equal(hasMore, Data(reply).GetProperty("hasMore").GetBoolean());
        Assert.Equal("GET", Assert.Single(handler.Requests).Method);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("fixture")]
    [InlineData("profile")]
    [InlineData("state")]
    [InlineData("duplicate")]
    [InlineData("tooMany")]
    [InlineData("impossibleMore")]
    [InlineData("null")]
    public async Task InvalidRecentListsCannotExposeUnconfirmedRows(string failure)
    {
        var root = Tree(new { items = new[] { Scan() }, hasMore = false });
        switch (failure)
        {
            case "scope": root["items"]![0]!["organizationId"] = Guid.NewGuid(); break;
            case "fixture": root["items"]![0]!["fixture"] = "unapproved"; break;
            case "profile": root["items"]![0]!["readerProfile"] = "invented"; break;
            case "state": root["items"]![0]!["state"] = "success"; break;
            case "duplicate": root["items"]!.AsArray().Add(Tree(Scan())); break;
            case "tooMany": root["items"] = Tree(Enumerable.Range(0, 51).Select(_ => Scan(id: Guid.NewGuid())).ToArray()); break;
            case "impossibleMore": root["hasMore"] = true; break;
            default: root["items"]![0] = null; break;
        }
        var handler = Queue(() => Json(root)); await using var client = new HostedScanClient(Options(), handler);
        Assert.Equal("HOSTED_RESPONSE_INVALID", (await client.ListAsync()).ErrorCode);
    }
}
