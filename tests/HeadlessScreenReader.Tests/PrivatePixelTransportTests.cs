using System.Net;
using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using static HeadlessScreenReader.Tests.PrivatePixelTests;

namespace HeadlessScreenReader.Tests;

public sealed class PrivatePixelTransportTests
{
    [Fact]
    public async Task ActualPixelHttp_DeliversBoundPngWithPrivateHeadersThenRevokesReconnect()
    {
        await using var h = await Host.StartAsync();
        using var result = await h.Client.SendAsync(h.Request()); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(Envelope().ToArray(), await result.Content.ReadAsByteArrayAsync()); Assert.Equal("image/png", result.Content.Headers.ContentType!.MediaType);
        Assert.Equal("1", result.Headers.GetValues("X-Viewer-Epoch").Single()); Assert.Equal("0", result.Headers.GetValues("X-Viewer-Baseline-Ordinal").Single());
        Assert.Equal("FreshFixturePixels", result.Headers.GetValues("X-Viewer-Pixel-Kind").Single()); Assert.Equal("false", result.Headers.GetValues("X-Viewer-Simulated-Assistance").Single());
        Assert.Matches("^[a-f0-9]{64}$", result.Headers.GetValues("X-Viewer-Document-Sha256").Single());
        Assert.True(result.Headers.CacheControl!.NoStore); Assert.Equal("nosniff", result.Headers.GetValues("X-Content-Type-Options").Single());
        using var revoke = h.Request(); revoke.Method = HttpMethod.Post; revoke.RequestUri = new Uri(h.Path("revoke"), UriKind.Relative);
        using var stopped = await h.Client.SendAsync(revoke); Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
        using var denied = await h.Client.SendAsync(h.Request()); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(1, h.SourceCalls); Assert.Equal(0, h.Broker.Status.ActiveReads);
        using var spec = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json"); spec.Headers.Authorization = new("Bearer", h.Identity.Credential.Value);
        using var protocol = await h.Client.SendAsync(spec); using var json = JsonDocument.Parse(await protocol.Content.ReadAsStringAsync());
        Assert.Equal(4, json.RootElement.GetProperty("paths").EnumerateObject().Count());
        Assert.True(json.RootElement.GetProperty("paths").GetProperty("/viewer/{scanId}/{sessionId}/pixels").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("image/png", out _));
    }
    [Theory]
    [InlineData("identity", 401)]
    [InlineData("origin", 403)]
    [InlineData("cookie", 403)]
    [InlineData("host", 403)]
    [InlineData("scan", 401)]
    [InlineData("session", 401)]
    [InlineData("connection", 401)]
    [InlineData("secret", 401)]
    [InlineData("query-secret", 400)]
    [InlineData("duplicate-epoch", 400)]
    [InlineData("epoch-zero", 400)]
    [InlineData("epoch-negative", 400)]
    [InlineData("body", 400)]
    public async Task PixelHttp_DeniesClientScopeOrMalformedRequestBeforeSource(string partition, int status)
    {
        await using var h = await Host.StartAsync(); using var request = h.Request();
        switch (partition)
        {
            case "identity": request.Headers.Authorization = new("Bearer", new string('0', 64)); break;
            case "origin": request.Headers.Add("Origin", "null"); break;
            case "cookie": request.Headers.Add("Cookie", "private-canary"); break;
            case "host": request.Headers.Host = "localhost:" + h.Server.BaseAddress.Port; break;
            case "scan": request.RequestUri = new Uri(h.Path("pixels?epoch=1").Replace(h.Scope.ScanId, new string('0', 32)), UriKind.Relative); break;
            case "session": request.RequestUri = new Uri(h.Path("pixels?epoch=1").Replace(h.Scope.SessionId, new string('0', 32)), UriKind.Relative); break;
            case "connection": request.Headers.Remove("X-Viewer-Id"); break;
            case "secret": request.Headers.Remove("X-Viewer-Token"); request.Headers.Add("X-Viewer-Token", new string('0', 64)); break;
            case "query-secret": request.RequestUri = new Uri(h.Path("pixels?epoch=1&token=private-canary"), UriKind.Relative); break;
            case "duplicate-epoch": request.RequestUri = new Uri(h.Path("pixels?epoch=1&epoch=1"), UriKind.Relative); break;
            case "epoch-zero": request.RequestUri = new Uri(h.Path("pixels?epoch=0"), UriKind.Relative); break;
            case "epoch-negative": request.RequestUri = new Uri(h.Path("pixels?epoch=-1"), UriKind.Relative); break;
            case "body": request.Content = new StringContent("private-canary"); break;
        }
        using var result = await h.Client.SendAsync(request); Assert.Equal(status, (int)result.StatusCode); Assert.Equal(0, h.SourceCalls);
        Assert.DoesNotContain("private-canary", await result.Content.ReadAsStringAsync()); Assert.Equal("application/problem+json", result.Content.Headers.ContentType!.MediaType);
    }
    [Theory]
    [InlineData("POST", "pixels?epoch=1", 405)]
    [InlineData("PUT", "pixels?epoch=1", 405)]
    [InlineData("DELETE", "pixels?epoch=1", 405)]
    [InlineData("POST", "keyboard", 404)]
    [InlineData("POST", "pointer", 404)]
    [InlineData("POST", "clipboard", 404)]
    [InlineData("GET", "clipboard", 404)]
    [InlineData("POST", "navigate", 404)]
    [InlineData("GET", "websocket", 404)]
    public async Task PixelRelay_RejectsMutationClipboardAndControlPathsWithoutDispatch(string method, string suffix, int status)
    {
        await using var h = await Host.StartAsync(); using var request = h.Request(); request.Method = new(method);
        request.RequestUri = new Uri(h.Path(suffix), UriKind.Relative);
        using var result = await h.Client.SendAsync(request); Assert.Equal(status, (int)result.StatusCode); Assert.Equal(0, h.SourceCalls);
        Assert.Equal(0, h.Broker.Status.ActiveReads);
    }
    [Fact]
    public async Task PixelEndpoint_IsAbsentUnlessHostExplicitlyEnablesIt()
    {
        await using var h = await Host.StartAsync(enabled: false); using var result = await h.Client.SendAsync(h.Request());
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode); Assert.Equal(0, h.SourceCalls);
    }
    [Fact]
    public async Task ActualPixelHttpRevocation_WaitsActualSourceAndDropsItsLateBytes()
    {
        var entered = Signal(); var finish = Signal();
        await using var h = await Host.StartAsync(async (scope, send) => { entered.SetResult(); await finish.Task; await send(new(scope.ScanId, 1, 0, "document", Envelope(), false)); });
        var read = h.Client.SendAsync(h.Request()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var request = h.Request(); request.Method = HttpMethod.Post; request.RequestUri = new Uri(h.Path("revoke"), UriKind.Relative);
        var stop = h.Client.SendAsync(request);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (h.Broker.Status.ActiveConnections != 0) { deadline.Token.ThrowIfCancellationRequested(); await Task.Yield(); }
        Assert.False(stop.IsCompleted); Assert.Equal(1, h.Broker.Status.ActiveReads); finish.SetResult();
        using var denied = await read; Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var revoked = await stop; Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode); Assert.Equal(0, h.Broker.Status.ActiveReads);
        Assert.DoesNotContain("PNG", await denied.Content.ReadAsStringAsync());
    }
    private sealed class Host : IAsyncDisposable
    {
        public ViewerScope Scope { get; private init; } = null!; public Guid Subject { get; private init; }
        public PrivateViewerBroker Broker { get; private init; } = null!; public ViewerConnection Connection { get; private init; } = null!;
        public ProcessViewerIdentity Identity { get; private init; } = null!; public PrivateViewerServer Server { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!; public int SourceCalls { get; private set; }
        public static async Task<Host> StartAsync(Func<ViewerScope, Func<ViewerPixelFrame, Task>, Task>? source = null, bool enabled = true)
        {
            var s = Setup(); var h = new Host { Scope = s.Scope, Subject = s.Subject, Broker = s.Broker, Connection = s.Connection, Identity = new(s.Subject) };
            var pixels = new PrivatePixelViewerService(s.Broker, (_, send, _) => { h.SourceCalls++; return source?.Invoke(s.Scope, send) ?? send(new(s.Scope.ScanId, 1, 0, "document", Envelope(), false)); });
            h.Server = await PrivateViewerServer.StartAsync(h.Identity, new PrivateViewerService(s.Broker, (_, _, _) => Task.CompletedTask), pixels: enabled ? pixels : null);
            h.Client = new(new SocketsHttpHandler { UseProxy = false, UseCookies = false }) { BaseAddress = h.Server.BaseAddress, Timeout = TimeSpan.FromSeconds(10) }; return h;
        }
        public string Path(string suffix) => $"/viewer/{Scope.ScanId}/{Scope.SessionId}/{suffix}";
        public HttpRequestMessage Request()
        {
            var r = new HttpRequestMessage(HttpMethod.Get, Path("pixels?epoch=1")); r.Headers.Authorization = new("Bearer", Identity.Credential.Value);
            r.Headers.Add("X-Viewer-Id", Connection.Id.ToString("N")); r.Headers.Add("X-Viewer-Token", Connection.Secret.Value); return r;
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await Broker.DisposeAsync(); await Server.DisposeAsync(); }
    }
}
