using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class S3ReadResilienceTests
{
    private static readonly byte[] Original = [1, 2, 3, 4];
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Reference = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly Guid Organization = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Job = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Uri Address = new($"https://example.invalid/hsr-evidence/public-tasks/{Organization:N}/{Job:N}/{Digest}?private-query=NEVER_LOG_QUERY");
    private sealed class Transport(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests.Add(request); return send(Requests.Count, request, token); }
    }
    private static HttpResponseMessage Body() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Original) };
    private static HttpClient Client(Transport transport, List<S3ReadDiagnostic> diagnostics) =>
        new(new S3ReadResilienceHandler(transport, diagnostics.Add));

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task SettledTransientGetRecoversOnceWithOriginalHeadersAndCorrelatedSafeFacts(int status)
    {
        using var correlation = new Activity("test").Start().SetTag(S3ReadResilienceHandler.SupportReferenceTag, Reference);
        var diagnostics = new List<S3ReadDiagnostic>();
        var transport = new Transport((number, _, _) => Task.FromResult(number == 1
            ? new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("NEVER_LOG_PROVIDER_BODY") } : Body()));
        using var client = Client(transport, diagnostics);
        using var request = new HttpRequestMessage(HttpMethod.Get, Address);
        request.Headers.Authorization = new("Bearer", "NEVER_LOG_AUTHORIZATION");
        using var response = await client.SendAsync(request);
        Assert.Equal(Original, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, sent =>
        { Assert.Equal(HttpMethod.Get, sent.Method); Assert.Equal(Address, sent.RequestUri); Assert.Equal(request.Headers.Authorization, sent.Headers.Authorization); });
        Assert.NotSame(transport.Requests[0], transport.Requests[1]);
        Assert.Equal(["RetryingRead", "RecoveredRead"], diagnostics.Select(value => value.Outcome));
        Assert.Equal(status, diagnostics[0].HttpStatus); Assert.Equal(200, diagnostics[1].HttpStatus);
        Assert.Equal(4, diagnostics[1].ReceivedBytes);
        Assert.All(diagnostics, value =>
        {
            Assert.Equal(Reference, value.SupportReference); Assert.Equal(Organization, value.OrganizationId); Assert.Equal(Job, value.JobId);
            Assert.Equal(Digest, value.OriginalSha256); Assert.False(value.WebsiteInputRetried); Assert.Equal(64, value.ObjectKeySha256.Length);
        });
        var json = JsonSerializer.Serialize(diagnostics);
        foreach (var forbidden in new[] { "NEVER_LOG", "example.invalid", "Authorization", "private-query", "public-tasks" }) Assert.DoesNotContain(forbidden, json);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(501)]
    public async Task PermanentGetFailureIsReturnedWithoutRetry(int status)
    {
        var diagnostics = new List<S3ReadDiagnostic>();
        var transport = new Transport((_, _, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var client = Client(transport, diagnostics); using var response = await client.GetAsync(Address);
        Assert.Equal(status, (int)response.StatusCode); Assert.Single(transport.Requests);
        var fact = Assert.Single(diagnostics); Assert.Equal("ReturnedHttpFailure", fact.Outcome); Assert.Equal(1, fact.Attempt);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public async Task NonGetOperationsAreNeverRepeatedOrBuffered(string method)
    {
        var diagnostics = new List<S3ReadDiagnostic>();
        var transport = new Transport((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = Client(transport, diagnostics); using var request = new HttpRequestMessage(new(method), Address);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Single(transport.Requests); Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RepeatedTransientFailureStopsAfterTwoReads()
    {
        var diagnostics = new List<S3ReadDiagnostic>();
        var transport = new Transport((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = Client(transport, diagnostics); using var response = await client.GetAsync(Address);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(["RetryingRead", "ReturnedHttpFailure"], diagnostics.Select(value => value.Outcome));
    }

    [Fact]
    public async Task SettledTransportFailureRecoversWithoutExposingExceptionText()
    {
        var diagnostics = new List<S3ReadDiagnostic>();
        var transport = new Transport((number, _, _) => number == 1
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException(HttpRequestError.ConnectionError, "NEVER_LOG_EXCEPTION")) : Task.FromResult(Body()));
        using var client = Client(transport, diagnostics); using var response = await client.GetAsync(Address);
        Assert.Equal(Original, await response.Content.ReadAsByteArrayAsync()); Assert.Equal(2, transport.Requests.Count);
        Assert.Equal("ConnectionError", diagnostics[0].TransportReason); Assert.Equal("TransportFailure", diagnostics[0].Cause);
        Assert.DoesNotContain("NEVER_LOG", JsonSerializer.Serialize(diagnostics));
    }

    [Fact]
    public async Task CancellationAfterFirstFailureCannotDispatchASecondRead()
    {
        using var cancellation = new CancellationTokenSource(); var diagnostics = new List<S3ReadDiagnostic>();
        var transport = new Transport((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(new S3ReadResilienceHandler(transport, value => { diagnostics.Add(value); cancellation.Cancel(); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(Address, cancellation.Token));
        Assert.Single(transport.Requests); Assert.DoesNotContain(diagnostics, value => value.Outcome == "RecoveredRead");
    }

    [Fact]
    public async Task OversizedMetadataKeepsTheCallersGuardWithoutReadingOrRetrying()
    {
        var diagnostics = new List<S3ReadDiagnostic>();
        var transport = new Transport((_, _, _) =>
        { var response = Body(); response.Content.Headers.ContentLength = S3ReadResilienceHandler.MaximumBodyBytes + 1; return Task.FromResult(response); });
        using var client = Client(transport, diagnostics);
        using var response = await client.GetAsync(Address, HttpCompletionOption.ResponseHeadersRead); Assert.Single(transport.Requests);
        Assert.Equal(S3ReadResilienceHandler.MaximumBodyBytes + 1, response.Content.Headers.ContentLength);
        Assert.Equal("BodyLimitExceeded", Assert.Single(diagnostics).Cause); Assert.Equal(0, diagnostics[0].ReceivedBytes);
        Assert.Equal("DeferredToCaller", diagnostics[0].Outcome);
    }

    [Fact]
    public async Task RangeHeadersAndExactBytesSurviveTheReadWrapper()
    {
        var diagnostics = new List<S3ReadDiagnostic>();
        var transport = new Transport((_, request, _) =>
        {
            Assert.Equal("bytes=0-3", request.Headers.Range!.ToString()); var response = Body(); response.StatusCode = HttpStatusCode.PartialContent;
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 3, 4); return Task.FromResult(response);
        });
        using var client = Client(transport, diagnostics); using var request = new HttpRequestMessage(HttpMethod.Get, Address);
        request.Headers.Range = new RangeHeaderValue(0, 3); using var response = await client.SendAsync(request);
        Assert.Equal(Original, await response.Content.ReadAsByteArrayAsync()); Assert.Equal("bytes 0-3/4", response.Content.Headers.ContentRange!.ToString());
        Assert.Single(transport.Requests); Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ObserverFailureCannotChangeARecoveredRead()
    {
        var transport = new Transport((number, _, _) => Task.FromResult(number == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Body()));
        using var client = new HttpClient(new S3ReadResilienceHandler(transport, _ => throw new IOException("NEVER_LOG_OBSERVER")));
        using var response = await client.GetAsync(Address); Assert.Equal(Original, await response.Content.ReadAsByteArrayAsync()); Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task ActualPrematureTcpBodySettlesBeforeTheOnlyRecoveryAndRetainsReceivedCount()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var address = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/original");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var accepted = 0;
        var server = Task.Run(async () =>
        {
            for (var number = 0; number < 2; number++)
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token); Interlocked.Increment(ref accepted);
                await using var stream = socket.GetStream(); var request = new byte[8192]; var length = 0;
                while (!Encoding.ASCII.GetString(request, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
                    length += await stream.ReadAsync(request.AsMemory(length), timeout.Token);
                var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, timeout.Token); await stream.WriteAsync(Original.AsMemory(0, number == 0 ? 2 : 4), timeout.Token);
            }
        }, timeout.Token);
        var diagnostics = new List<S3ReadDiagnostic>();
        using var client = new HttpClient(new S3ReadResilienceHandler(new SocketsHttpHandler { UseProxy = false }, diagnostics.Add));
        using var response = await client.GetAsync(address, timeout.Token); await server;
        Assert.Equal(Original, await response.Content.ReadAsByteArrayAsync()); Assert.Equal(2, accepted);
        Assert.Equal("BodyReadInterrupted", diagnostics[0].Cause); Assert.Equal(2, diagnostics[0].ReceivedBytes);
        Assert.Equal("RecoveredRead", diagnostics[1].Outcome); Assert.Equal(4, diagnostics[1].ReceivedBytes);
    }
}
