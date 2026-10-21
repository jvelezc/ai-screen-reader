using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PrivateClientProtocolTests
{
    [Theory]
    [InlineData("http://localhost:1234/")]
    [InlineData("https://127.0.0.1:1234/")]
    [InlineData("http://127.0.0.2:1234/")]
    [InlineData("http://127.0.0.1:1234/other")]
    [InlineData("http://127.0.0.1:1234/?token=x")]
    [InlineData("http://user@127.0.0.1:1234/")]
    [InlineData("http://127.0.0.1:1234/#x")]
    public void NativeClient_RejectsAnyAddressOutsideExactLoopbackRoot(string address)
        => Assert.Throws<ArgumentException>(() => new PrivateViewerHttpClient(new(address), new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")), "identity"));

    [Theory]
    [InlineData("scope")]
    [InlineData("session")]
    [InlineData("epoch")]
    [InlineData("snapshot-epoch")]
    [InlineData("kind")]
    [InlineData("document")]
    [InlineData("pixel-epoch")]
    [InlineData("baseline")]
    [InlineData("assistance")]
    [InlineData("pixel-kind")]
    [InlineData("media-type")]
    [InlineData("no-store")]
    [InlineData("png")]
    public async Task NativeClient_RejectsUnboundOrMalformedResponsesBeforePresentation(string partition)
    {
        var handler = new Handler(partition); using var client = handler.Client(); await client.ExchangeAsync("bootstrap");
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => client.ReadAsync(1));
        Assert.StartsWith("VIEW_CLIENT_", error.Code); Assert.DoesNotContain("identity", error.ToString());
    }
    [Theory]
    [InlineData(401, "VIEW_CLIENT_ENDED")]
    [InlineData(403, "VIEW_CLIENT_ENDED")]
    [InlineData(409, "VIEW_CLIENT_EXCLUDED")]
    [InlineData(302, "VIEW_CLIENT_RESPONSE_INVALID")]
    [InlineData(500, "VIEW_CLIENT_RESPONSE_INVALID")]
    public async Task NativeClient_DistinguishesDeniedExcludedAndFailedRefreshWithoutInventingCause(int code, string expected)
    {
        var handler = new Handler(status: code); using var client = handler.Client(); await client.ExchangeAsync("bootstrap");
        Assert.Equal(expected, (await Assert.ThrowsAsync<BrowserOperationException>(() => client.ReadAsync(1))).Code);
        Assert.Equal(2, handler.Paths.Count); // No pixel request after a rejected semantic read.
    }
    [Fact]
    public async Task NativeClient_SendsOnlyExactScopeHeadersAndJoinsSequentialReads()
    {
        var handler = new Handler(); using var client = handler.Client(); await client.ExchangeAsync("bootstrap");
        var content = await client.ReadAsync(1); await client.RevokeAsync();
        Assert.Equal(1, content.Epoch); Assert.Equal(1, content.ObservationOrdinal); Assert.Equal(0, content.PixelBaselineOrdinal);
        Assert.Equal(new[] { "/viewer/exchange", $"/viewer/{handler.Scope.ScanId}/{handler.Scope.SessionId}/observation?epoch=1", $"/viewer/{handler.Scope.ScanId}/{handler.Scope.SessionId}/pixels?epoch=1", $"/viewer/{handler.Scope.ScanId}/{handler.Scope.SessionId}/revoke" }, handler.Paths);
        Assert.Equal(4, client.Requests); Assert.Equal(3, handler.ScopedRequests);
        Assert.Equal("button: Safe", Assert.Single(content.Lines));
    }
    [Theory]
    [InlineData(7, false, false)]
    [InlineData(8, false, false)]
    [InlineData(9, false, true)]
    [InlineData(9, true, true)]
    public async Task NativeClient_BoundsActualBytesWithAndWithoutDeclaredLength(int bytes, bool declared, bool rejected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[bytes])) };
        if (declared) response.Content.Headers.ContentLength = bytes;
        if (rejected) Assert.Equal("VIEW_CLIENT_RESPONSE_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(() => PrivateViewerHttpClient.ReadBoundedAsync(response, 8, default))).Code);
        else Assert.Equal(bytes, (await PrivateViewerHttpClient.ReadBoundedAsync(response, 8, default)).Length);
    }
    [Fact]
    public async Task ActualNativeHttp_RejectsRedirectWithoutSendingCredentialsToTheDestination()
    {
        var first = new TcpListener(IPAddress.Loopback, 0); var destination = new TcpListener(IPAddress.Loopback, 0);
        first.Start(); destination.Start(); using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var destinationPort = ((IPEndPoint)destination.LocalEndpoint).Port;
            var reply = Task.Run(async () =>
            {
                using var socket = await first.AcceptTcpClientAsync(bound.Token); await using var stream = socket.GetStream();
                var buffer = new byte[8192]; Assert.True(await stream.ReadAsync(buffer, bound.Token) > 0);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{destinationPort}/\r\nCache-Control: no-store\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), bound.Token);
            });
            using var client = new PrivateViewerHttpClient(new($"http://127.0.0.1:{((IPEndPoint)first.LocalEndpoint).Port}/"), new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")), "never-forward-this");
            Assert.Equal("VIEW_CLIENT_RESPONSE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => client.ExchangeAsync("bootstrap", bound.Token))).Code);
            await reply; Assert.False(destination.Pending()); Assert.Equal(1, client.Requests);
        }
        finally { first.Stop(); destination.Stop(); }
    }
    private sealed class Handler(string partition = "", int status = 200) : HttpMessageHandler
    {
        public ViewerScope Scope { get; } = new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        public List<string> Paths { get; } = [];
        public int ScopedRequests { get; private set; }
        public PrivateViewerHttpClient Client() => new(new("http://127.0.0.1:1234/"), Scope, "identity", this);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery); Assert.Equal("127.0.0.1", request.RequestUri.Host); Assert.Equal("Bearer identity", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("Cookie")); Assert.False(request.Headers.Contains("Origin"));
            var response = new HttpResponseMessage(HttpStatusCode.OK); response.Headers.CacheControl = new() { NoStore = true };
            if (request.RequestUri.AbsolutePath == "/viewer/exchange") response.Content = Json(new ViewerConnectionResponse(Guid.Empty.ToString("N"), "connection", DateTimeOffset.UtcNow.AddMinutes(1)));
            else
            {
                Assert.Equal(Guid.Empty.ToString("N"), Assert.Single(request.Headers.GetValues("X-Viewer-Id"))); Assert.Equal("connection", Assert.Single(request.Headers.GetValues("X-Viewer-Token"))); ScopedRequests++;
                response.StatusCode = (HttpStatusCode)status;
                if (request.RequestUri.AbsolutePath.EndsWith("/observation", StringComparison.Ordinal))
                {
                    var snapshot = new AccessibilitySnapshot("doc", [new("n", "button", "Safe")], []) { CaptureEpoch = partition == "snapshot-epoch" ? 2 : 1 };
                    response.Content = Json(new ViewerObservationResponse(partition == "scope" ? "wrong" : Scope.ScanId, partition == "session" ? "wrong" : Scope.SessionId,
                        partition == "epoch" ? 2 : 1, 1, partition == "kind" ? "Fresh" : "LastPersistedObservation", snapshot, false));
                }
                else if (request.RequestUri.AbsolutePath.EndsWith("/pixels", StringComparison.Ordinal))
                {
                    response.Content = new ByteArrayContent(partition == "png" ? [] : PrivatePixelTests.Envelope().ToArray()); response.Content.Headers.ContentType = new(partition == "media-type" ? "text/html" : "image/png");
                    response.Headers.Add("X-Viewer-Epoch", partition == "pixel-epoch" ? "2" : "1");
                    response.Headers.Add("X-Viewer-Baseline-Ordinal", partition == "baseline" ? "2" : "0");
                    response.Headers.Add("X-Viewer-Document-Sha256", partition == "document" ? "wrong" : Convert.ToHexStringLower(SHA256.HashData("doc"u8)));
                    response.Headers.Add("X-Viewer-Pixel-Kind", partition == "pixel-kind" ? "Other" : "FreshFixturePixels");
                    response.Headers.Add("X-Viewer-Simulated-Assistance", partition == "assistance" ? "true" : "false");
                }
                if (partition == "no-store") response.Headers.CacheControl = null;
            }
            return Task.FromResult(response);
        }
        private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    }
}
