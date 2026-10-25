using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedMcpTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Session = Guid.NewGuid();
    private static HostedReaderOptions Options() => new(new("http://127.0.0.1:6086/"), Organization, "original-caller-canary");
    private static object Metadata(Guid? id = null) => new { sessionId = id ?? Session, organizationId = Organization, fixture = "Foundation", readerProfile = ScreenReaderSession.Profile, state = "Open", expiresAt = DateTimeOffset.UtcNow.AddMinutes(2), readOnly = true };
    private static JsonElement Data(ToolReply reply) { using var json = JsonDocument.Parse(reply.ToJson()); return json.RootElement.GetProperty("data").Clone(); }
    private static HttpResponseMessage Json(object body, int status = 200)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
        response.Headers.CacheControl = new() { NoStore = true }; return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Bearer)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter)); return send(request); }
    }
    private static Handler Queue(params Func<HttpResponseMessage>[] responses)
    {
        var queue = new Queue<Func<HttpResponseMessage>>(responses);
        return new(_ => Task.FromResult(queue.Dequeue()()));
    }

    [Theory]
    [InlineData("https://localhost/")]
    [InlineData("http://example.com/")]
    [InlineData("http://127.0.0.2/")]
    [InlineData("http://localhost/api/")]
    [InlineData("http://user:pass@localhost/")]
    [InlineData("http://localhost/?url=secret")]
    [InlineData("http://localhost/#secret")]
    public void ConfigurationRejectsNonPrivateOrNonRootOrigins(string origin) =>
        Assert.Throws<ArgumentException>(() => new HostedReaderOptions(new(origin), Organization, "canary"));

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("header\r\ninjection")]
    [InlineData("unicodeé")]
    public void ConfigurationRejectsInvalidBearers(string bearer) => Assert.Throws<ArgumentException>(() => new HostedReaderOptions(new("http://localhost/"), Organization, bearer));

    [Fact]
    public void ConfigurationRedactsCredentialAndRejectsEmptyScopeAndOversizedBearer()
    {
        Assert.DoesNotContain("original-caller-canary", Options().ToString());
        Assert.Throws<ArgumentException>(() => new HostedReaderOptions(new("http://localhost/"), Guid.Empty, "valid"));
        Assert.Throws<ArgumentException>(() => new HostedReaderOptions(new("http://localhost/"), Organization, new('a', 8193)));
    }

    [Fact]
    public async Task FixedScopeOriginalCallerAndConfirmedReplacementArePreserved()
    {
        var replacement = Guid.NewGuid();
        var handler = Queue(() => Json(new { id = Organization, name = "workspace" }), () => Json(Metadata(), 201),
            () => Json(new { sessionId = Session, state = "Closed" }), () => Json(Metadata(replacement), 201), () => Json(new { sessionId = replacement, state = "Closed" }));
        await using var client = new HostedReaderClient(Options(), handler);
        Assert.Equal("ready", (await client.StatusAsync()).Status);
        Assert.Equal(Session.ToString("N"), (await client.OpenAsync()).SessionId);
        Assert.Equal("SESSION_NOT_FOUND", (await client.CloseAsync(Guid.NewGuid().ToString())).ErrorCode);
        Assert.Equal(replacement.ToString("N"), (await client.OpenAsync()).SessionId);
        Assert.Equal("SESSION_NOT_FOUND", (await client.CloseAsync(Session.ToString())).ErrorCode);
        Assert.Equal("closed", (await client.CloseAsync(replacement.ToString())).Status);
        Assert.True(client.CleanupConfirmed);
        Assert.Equal(new[] { "GET", "POST", "DELETE", "POST", "DELETE" }, handler.Requests.Select(r => r.Method));
        Assert.All(handler.Requests, r => { Assert.StartsWith($"/api/workspaces/{Organization:D}", r.Path); Assert.Equal("original-caller-canary", r.Bearer); });
        Assert.DoesNotContain("original-caller-canary", (await new HostedReaderTools(client).OpenFixture("Widgets")).StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public async Task LostAdmissionBlocksAutomaticReadmissionAndRetainsUnconfirmedCleanup()
    {
        var handler = new Handler(_ => throw new HttpRequestException("original-caller-canary"));
        var client = new HostedReaderClient(Options(), handler);
        Assert.Equal("HOSTED_OPERATION_UNCONFIRMED", (await client.OpenAsync()).ErrorCode);
        Assert.Equal("HOSTED_ADMISSION_UNCONFIRMED", (await client.OpenAsync()).ErrorCode);
        await client.DisposeAsync(); Assert.False(client.CleanupConfirmed); Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UnconfirmedCloseCannotReplaceButExplicitCloseCanRecover()
    {
        var handler = Queue(() => Json(Metadata(), 201), () => Json(new { title = "CONTROL_READER_CLOSE_UNCONFIRMED", detail = "original-caller-canary" }, 503),
            () => Json(new { title = "CONTROL_READER_CLOSE_UNCONFIRMED" }, 503), () => Json(new { sessionId = Session, state = "Closed" }));
        await using var client = new HostedReaderClient(Options(), handler);
        await client.OpenAsync();
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await client.CloseAsync(Session.ToString())).ErrorCode);
        Assert.False(client.CleanupConfirmed);
        Assert.Equal("error", (await client.OpenAsync()).Status);
        Assert.Equal("closed", (await client.CloseAsync(Session.ToString())).Status);
        Assert.True(client.CleanupConfirmed);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == "POST"));
    }

    [Fact]
    public async Task CallerCancellationJoinsDispatchedAdmissionAndKeepsOwnership()
    {
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(request => request.Method == HttpMethod.Delete ? Task.FromResult(Json(new { sessionId = Session, state = "Closed" })) : Start());
        Task<HttpResponseMessage> Start() { dispatched.SetResult(); return completion.Task; }
        await using var client = new HostedReaderClient(Options(), handler);
        using var cancellation = new CancellationTokenSource();
        var opening = client.OpenAsync(token: cancellation.Token); await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        Assert.False(opening.IsCompleted);
        completion.SetResult(Json(Metadata(), 201));
        Assert.Equal("opened", (await opening).Status); Assert.False(client.CleanupConfirmed);
        Assert.Equal("closed", (await client.CloseAsync(Session.ToString())).Status);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    public async Task KnownAdmissionRejectionCanRetryWithoutInventingOwnership(int status)
    {
        var support = Guid.NewGuid().ToString("N");
        var handler = Queue(() => Json(new { title = "CONTROL_ACCESS_DENIED", detail = "original-caller-canary", supportReference = support }, status), () => Json(Metadata(), 201), () => Json(new { sessionId = Session, state = "Closed" }));
        await using var client = new HostedReaderClient(Options(), handler);
        var rejected = await client.OpenAsync(); Assert.Equal("CONTROL_ACCESS_DENIED", rejected.ErrorCode);
        Assert.Equal(support, Data(rejected).GetProperty("supportReference").GetString());
        Assert.DoesNotContain("original-caller-canary", rejected.ToJson()); Assert.True(client.CleanupConfirmed);
        Assert.Equal("opened", (await client.OpenAsync()).Status);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("scope")]
    [InlineData("profile")]
    [InlineData("large")]
    [InlineData("chunked")]
    [InlineData("redirect")]
    [InlineData("cache")]
    [InlineData("duplicate")]
    public async Task InvalidAdmissionNeverClaimsCleanOrRetries(string failure)
    {
        var handler = Queue(() => failure switch
        {
            "malformed" => Json(new { sessionId = Session }, 201),
            "scope" => Json(new { sessionId = Session, organizationId = Guid.NewGuid(), fixture = "Foundation", readerProfile = ScreenReaderSession.Profile, state = "Open", expiresAt = DateTimeOffset.UtcNow, readOnly = true }, 201),
            "profile" => Json(new { sessionId = Session, organizationId = Organization, fixture = "Foundation", readerProfile = "other", state = "Open", expiresAt = DateTimeOffset.UtcNow, readOnly = true }, 201),
            "large" or "chunked" => Large(failure == "chunked"),
            "redirect" => new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new("http://example.invalid/") } },
            "cache" => new(HttpStatusCode.Created) { Content = new StringContent("{}", Encoding.UTF8, "application/json") },
            _ => new(HttpStatusCode.Created) { Content = new StringContent("{\"sessionId\":\"a\",\"sessionId\":\"b\"}", Encoding.UTF8, "application/json"), Headers = { CacheControl = new() { NoStore = true } } }
        });
        var client = new HostedReaderClient(Options(), handler);
        Assert.Equal("error", (await client.OpenAsync()).Status);
        Assert.Equal("HOSTED_ADMISSION_UNCONFIRMED", (await client.OpenAsync()).ErrorCode);
        await client.DisposeAsync(); Assert.False(client.CleanupConfirmed); Assert.Single(handler.Requests);
    }
    private static HttpResponseMessage Large(bool chunked)
    {
        HttpContent content = chunked ? new StreamContent(new MemoryStream(new byte[262145])) : new ByteArrayContent(new byte[262145]);
        return new(HttpStatusCode.Created) { Content = content };
    }

    [Theory]
    [InlineData("Tab", null)]
    [InlineData("Activate", null)]
    [InlineData("NextHeading", "Enter")]
    [InlineData("Typeahead", "a")]
    [InlineData("0", null)]
    public async Task InputCommandsAndKeysAreDeniedBeforeHttp(string command, string? key)
    {
        var handler = Queue(); await using var client = new HostedReaderClient(Options(), handler);
        Assert.Equal("HOSTED_READ_ONLY_COMMAND_DENIED", (await client.ExecuteAsync("absent", "doc", command, key)).ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void OversizedHostedCommandPreservesActualInputUncertainty()
    {
        var step = new ReaderStep(1, ReaderCommand.Activate, ReaderMode.Focus, ReaderOutcome.Unknown, new('x', 140000), "button", "button", 1, InputMayHaveOccurred: true, Verification: VerificationStatus.TimedOut);
        var reply = new ToolReply("step", Session.ToString(), ScreenReaderSession.Profile, new HostedReaderCommand(Session, ScreenReaderSession.Profile, "doc", false, step, 1, true));
        using var parsed = JsonDocument.Parse(reply.ToJson());
        Assert.Equal("RESPONSE_TOO_LARGE", parsed.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(1, parsed.RootElement.GetProperty("data").GetProperty("browserKeystrokes").GetInt32());
        Assert.True(parsed.RootElement.GetProperty("data").GetProperty("inputMayHaveOccurred").GetBoolean());
        Assert.Equal("TimedOut", parsed.RootElement.GetProperty("data").GetProperty("verification").GetString());
        Assert.Equal(1, parsed.RootElement.GetProperty("data").GetProperty("dispatchedInputs").GetInt32());
        Assert.True(parsed.RootElement.GetProperty("data").GetProperty("goalVerified").GetBoolean());
        Assert.InRange(Encoding.UTF8.GetByteCount(reply.ToJson()), 1, 131072);
    }
}
