using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class PrivateViewerTransportTests
{
    [Fact]
    public async Task ActualHttp_ExchangesOnceReadsBoundObservationAndRevokesEveryReconnectWithoutInputEndpoint()
    {
        await using var host = await Harness.StartAsync();
        var connection = await host.ExchangeAsync();
        using var read = await host.SendAsync(HttpMethod.Get, host.ReadPath, connection);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", Assert.Single(read.Headers.GetValues("X-Content-Type-Options")));
        var view = JsonSerializer.Deserialize<ViewerObservationResponse>(await read.Content.ReadAsStringAsync())!;
        Assert.Equal(host.Scope.ScanId, view.ScanId); Assert.Equal(host.Scope.SessionId, view.SessionId);
        Assert.Equal("LastPersistedObservation", view.ObservationKind); Assert.Equal(1, view.Epoch);
        Assert.Equal("Safe control", Assert.Single(view.Snapshot.Nodes).Name); Assert.Null(view.Snapshot.DocumentUrl);
        using var replay = await host.ExchangeResponseAsync(); Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var revoke = await host.SendAsync(HttpMethod.Post, host.RevokePath, connection); Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var denied = await host.SendAsync(HttpMethod.Get, host.ReadPath, connection); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(1, host.SourceCalls); Assert.Equal(0, host.Broker.Status.ActiveConnections);
        foreach (var path in new[] { "/cdp", "/vnc", "/viewer/input", "/viewer/execute" })
        { using var missing = await host.SendAsync(HttpMethod.Post, path, connection); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode); }
        using var wrongMethod = await host.SendAsync(HttpMethod.Post, host.ReadPath, connection); Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        using var api = await host.SendAsync(HttpMethod.Get, "/openapi/v1.json");
        using var document = JsonDocument.Parse(await api.Content.ReadAsStringAsync());
        Assert.Equal("3.1.0", document.RootElement.GetProperty("openapi").GetString()); Assert.Equal(3, document.RootElement.GetProperty("paths").EnumerateObject().Count());
    }

    [Theory]
    [InlineData("identity", 401)]
    [InlineData("unknown-identity", 401)]
    [InlineData("origin", 403)]
    [InlineData("cookie", 403)]
    [InlineData("host", 403)]
    [InlineData("connection", 401)]
    [InlineData("token", 401)]
    [InlineData("scan", 401)]
    [InlineData("session", 401)]
    [InlineData("query-token", 400)]
    [InlineData("duplicate-epoch", 400)]
    [InlineData("invalid-epoch", 400)]
    public async Task DeniedHttpContextOrScope_NeverInvokesObservationSource(string partition, int status)
    {
        await using var host = await Harness.StartAsync(); var connection = await host.ExchangeAsync();
        var path = partition == "scan" ? host.ReadPath.Replace(host.Scope.ScanId, Guid.NewGuid().ToString("N"), StringComparison.Ordinal)
            : partition == "session" ? host.ReadPath.Replace(host.Scope.SessionId, Guid.NewGuid().ToString("N"), StringComparison.Ordinal)
            : partition == "query-token" ? host.ReadPath + "&token=private-query-canary"
            : partition == "duplicate-epoch" ? host.ReadPath + "&epoch=1"
            : partition == "invalid-epoch" ? host.ReadPath.Replace("epoch=1", "epoch=-1", StringComparison.Ordinal) : host.ReadPath;
        using var request = host.Request(HttpMethod.Get, path, connection);
        switch (partition)
        {
            case "identity": request.Headers.Authorization = null; break;
            case "unknown-identity": request.Headers.Authorization = new("Bearer", new string('0', 64)); break;
            case "origin": request.Headers.Add("Origin", "null"); break;
            case "cookie": request.Headers.Add("Cookie", "session=private-cookie-canary"); break;
            case "host": request.Headers.Host = "localhost:" + host.Server.BaseAddress.Port; break;
            case "connection": request.Headers.Remove("X-Viewer-Id"); break;
            case "token": request.Headers.Remove("X-Viewer-Token"); request.Headers.Add("X-Viewer-Token", new string('0', 64)); break;
        }
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode); Assert.Equal(0, host.SourceCalls); Assert.True(response.Headers.CacheControl!.NoStore);
        var error = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-query-canary", error); Assert.DoesNotContain("private-cookie-canary", error);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("null", 400)]
    [InlineData("duplicate", 400)]
    [InlineData("subject", 400)]
    [InlineData("malformed", 400)]
    [InlineData("oversize", 413)]
    [InlineData("content-type", 415)]
    public async Task InvalidExchangeBody_CannotConsumeBootstrapOrEchoItsPayload(string partition, int status)
    {
        await using var host = await Harness.StartAsync();
        var body = partition switch
        {
            "null" => "null",
            "duplicate" => "{\"ScanId\":\"private-body-canary\",\"ScanId\":\"another\"}",
            "subject" => JsonSerializer.Serialize(new { host.Scope.ScanId, host.Scope.SessionId, Bootstrap = host.Bootstrap.Secret.Value, Subject = "private-body-canary" }),
            "oversize" => new string('x', 4097),
            "content-type" => "private-body-canary",
            _ => "{private-body-canary"
        };
        using var request = host.Request(HttpMethod.Post, "/viewer/exchange");
        request.Content = new StringContent(body, Encoding.UTF8, partition == "content-type" ? "text/plain" : "application/json");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode); Assert.Equal(0, host.Broker.Status.ActiveConnections);
        Assert.DoesNotContain("private-body-canary", await response.Content.ReadAsStringAsync());
        Assert.NotNull(await host.ExchangeAsync());
    }

    [Fact]
    public async Task ActualHttpRevocation_WaitsIgnoredSourceCancellationAndDropsLatePrivatePayload()
    {
        var started = Signal(); var release = Signal();
        await using var host = await Harness.StartAsync(async (send, token, observation) =>
        { started.SetResult(); await release.Task; await send(observation with { Snapshot = observation.Snapshot with { DocumentId = "private-late-canary" } }); });
        var connection = await host.ExchangeAsync();
        var read = host.SendAsync(HttpMethod.Get, host.ReadPath, connection); await started.Task;
        var revoke = host.SendAsync(HttpMethod.Post, host.RevokePath, connection);
        await Until(() => host.Broker.Status.ActiveConnections == 0);
        Assert.False(revoke.IsCompleted); Assert.Equal(1, host.Broker.Status.ActiveReads);
        release.SetResult(); using var response = await read; using var stopped = await revoke;
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
        Assert.DoesNotContain("private-late-canary", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Broker.Status.ActiveReads);
    }

    [Fact]
    public async Task ActualHttpProviderFailure_ReturnsSanitizedProblemWithoutPrivateExceptionText()
    {
        await using var host = await Harness.StartAsync((_, _, _) => throw new IOException("private-exception-canary"));
        var connection = await host.ExchangeAsync(); using var read = await host.SendAsync(HttpMethod.Get, host.ReadPath, connection);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        var problem = await read.Content.ReadAsStringAsync(); Assert.Contains("VIEWER_OPERATION_UNCONFIRMED", problem);
        Assert.DoesNotContain("private-exception-canary", problem); Assert.Equal(0, host.Broker.Status.ActiveReads);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Until(Func<bool> predicate)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!predicate()) await Task.Delay(1, timeout.Token); }

    internal sealed class Harness : IAsyncDisposable
    {
        public ViewerScope Scope { get; } = new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        public Guid Subject { get; } = Guid.NewGuid();
        public ProcessViewerIdentity Identity { get; }
        public PrivateViewerBroker Broker { get; }
        public ViewerBootstrap Bootstrap { get; }
        public PrivateViewerServer Server { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public int SourceCalls { get; private set; }
        public string ReadPath => $"/viewer/{Scope.ScanId}/{Scope.SessionId}/observation?epoch=1";
        public string RevokePath => $"/viewer/{Scope.ScanId}/{Scope.SessionId}/revoke";
        private Harness() { Identity = new(Subject); Broker = new(Scope, Subject); Bootstrap = Broker.IssueBootstrap(Subject, Scope, TimeSpan.FromMinutes(1)); }
        public static async Task<Harness> StartAsync(Func<Func<RetainedViewerObservation, Task>, CancellationToken, RetainedViewerObservation, Task>? source = null)
        {
            var host = new Harness();
            var service = new PrivateViewerService(host.Broker, (epoch, send, token) =>
            {
                host.SourceCalls++;
                var observation = new RetainedViewerObservation(host.Scope.ScanId, epoch, 1, new OwnedBrowser().Snapshot() with { CaptureEpoch = epoch, DocumentUrl = null }, false);
                return source is null ? send(observation) : source(send, token, observation);
            });
            host.Server = await PrivateViewerServer.StartAsync(host.Identity, service);
            host.Client = new(new SocketsHttpHandler { UseProxy = false, UseCookies = false }) { BaseAddress = host.Server.BaseAddress, Timeout = TimeSpan.FromSeconds(10) };
            return host;
        }
        public HttpRequestMessage Request(HttpMethod method, string path, ViewerConnectionResponse? connection = null)
        {
            var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new("Bearer", Identity.Credential.Value);
            if (connection is not null) { request.Headers.Add("X-Viewer-Id", connection.ConnectionId); request.Headers.Add("X-Viewer-Token", connection.Token); }
            return request;
        }
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, ViewerConnectionResponse? connection = null)
        { using var request = Request(method, path, connection); return await Client.SendAsync(request); }
        public async Task<HttpResponseMessage> ExchangeResponseAsync()
        {
            using var request = Request(HttpMethod.Post, "/viewer/exchange");
            request.Content = new StringContent(JsonSerializer.Serialize(new ExchangeViewerRequest(Scope.ScanId, Scope.SessionId, Bootstrap.Secret.Value)), Encoding.UTF8, "application/json");
            return await Client.SendAsync(request);
        }
        public async Task<ViewerConnectionResponse> ExchangeAsync()
        { using var response = await ExchangeResponseAsync(); response.EnsureSuccessStatusCode(); return JsonSerializer.Deserialize<ViewerConnectionResponse>(await response.Content.ReadAsStringAsync())!; }
        public async ValueTask DisposeAsync() { Client.Dispose(); await Broker.DisposeAsync(); await Server.DisposeAsync(); }
    }
}
