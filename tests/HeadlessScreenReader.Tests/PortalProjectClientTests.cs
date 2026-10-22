using System.Net;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class PortalProjectClientTests
{
    internal const string Key = "sb_publishable_protocol_test_credential";
    internal const string Bearer = "eyJzdWIiOiJ1bnRydXN0ZWQifQ.e30.c2ln";
    internal static readonly Guid Actor = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    internal static readonly Guid Organization = Guid.Parse("a2000000-0000-0000-0000-000000000001");
    internal static readonly Guid Scan = Guid.Parse("a3000000-0000-0000-0000-000000000001");
    internal static readonly Guid Job = Guid.Parse("a4000000-0000-0000-0000-000000000001");
    internal static string UserJson => JsonSerializer.Serialize(new
    {
        id = Actor,
        email = "reader@example.invalid",
        email_confirmed_at = "2026-10-21T00:00:00Z",
        is_anonymous = false,
        user_metadata = new { sub = Organization }
    });

    [Fact]
    public async Task IdentityComesFromFixedProjectUserLookupAndNeverFromCallerClaims()
    {
        using var http = Client((request, _) =>
        {
            Assert.Equal("https://gpnijaxrgtwzwtrqsbcu.supabase.co/auth/v1/user", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(Bearer, request.Headers.Authorization.Parameter);
            Assert.Equal(Key, request.Headers.GetValues("apikey").Single());
            Assert.False(request.Headers.Contains("Accept-Profile"));
            Assert.Null(request.Content);
            return Task.FromResult(Response(UserJson));
        });
        var actor = await new SupabasePortalClient(http, Key).AuthenticateAsync(Bearer, default);
        Assert.Equal(Actor, actor.UserId);
        Assert.Equal("reader@example.invalid", actor.Email);
        Assert.DoesNotContain(Bearer, actor.ToString());
        Assert.DoesNotContain(Bearer, JsonSerializer.Serialize(actor));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing_segment")]
    [InlineData("empty_signature")]
    [InlineData("newline")]
    [InlineData("oversize")]
    [InlineData("url")]
    public async Task MalformedBearerNeverContactsProvider(string scenario)
    {
        var value = scenario switch
        {
            "empty" => "",
            "missing_segment" => "one.two",
            "empty_signature" => "one.two.",
            "newline" => "one.two.three\n",
            "oversize" => new string('a', 8193),
            "url" => "https://example.invalid/token",
            _ => throw new InvalidOperationException()
        };
        var calls = 0;
        using var http = Client((_, _) => { calls++; throw new InvalidOperationException(); });
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, Key).AuthenticateAsync(value, default));
        Assert.Equal(401, fault.Status);
        Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", fault.Code);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task ProviderDeniedIdentityNeverBecomesWorkspaceAuthority(int status)
    {
        using var http = Client((_, _) => Task.FromResult(Response("{\"message\":\"private-provider-canary\"}", status)));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, Key).AuthenticateAsync(Bearer, default));
        Assert.Equal(401, fault.Status);
        Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", fault.Code);
        Assert.DoesNotContain("private-provider-canary", fault.ToString());
    }

    [Fact]
    public async Task AuthOutageStaysUnavailableAndDoesNotExposeBearerOrRawException()
    {
        using var http = Client((_, _) => throw new HttpRequestException("private-provider-canary " + Bearer));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, Key).AuthenticateAsync(Bearer, default));
        Assert.Equal(503, fault.Status);
        Assert.Equal("CONTROL_AUTH_PROVIDER_UNAVAILABLE", fault.Code);
        Assert.DoesNotContain("private-provider-canary", fault.ToString());
        Assert.DoesNotContain(Bearer, fault.ToString());
    }

    [Theory]
    [InlineData("unconfirmed")]
    [InlineData("anonymous")]
    public async Task UnconfirmedOrAnonymousIdentityDoesNotEnterPortal(string scenario)
    {
        var user = scenario == "anonymous" ? UserJson.Replace("\"is_anonymous\":false", "\"is_anonymous\":true", StringComparison.Ordinal)
            : UserJson.Replace("\"2026-10-21T00:00:00Z\"", "null", StringComparison.Ordinal);
        using var http = Client((_, _) => Task.FromResult(Response(user)));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, Key).AuthenticateAsync(Bearer, default));
        Assert.Equal(403, fault.Status);
        Assert.Equal("CONTROL_EMAIL_CONFIRMATION_REQUIRED", fault.Code);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing_id")]
    [InlineData("empty_id")]
    [InlineData("array")]
    [InlineData("oversize")]
    public async Task InvalidAuthResponseCannotProduceAnIdentity(string scenario)
    {
        var body = scenario switch
        {
            "duplicate" => "{\"id\":\"first\",\"id\":\"second\"}",
            "missing_id" => "{}",
            "empty_id" => UserJson.Replace(Actor.ToString(), Guid.Empty.ToString(), StringComparison.Ordinal),
            "array" => "[]",
            "oversize" => new string('x', 131073),
            _ => throw new InvalidOperationException()
        };
        using var http = Client((_, _) => Task.FromResult(Response(body)));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, Key).AuthenticateAsync(Bearer, default));
        Assert.Equal(503, fault.Status);
    }

    [Fact]
    public async Task WorkspaceRpcUsesOriginalBearerAndHasNoCallerIdentityBody()
    {
        using var http = Client(async (request, token) =>
        {
            Assert.Equal("/rest/v1/rpc/ensure_personal_workspace", request.RequestUri!.AbsolutePath);
            Assert.Equal("hsr", request.Headers.GetValues("Content-Profile").Single());
            Assert.Equal(Bearer, request.Headers.Authorization!.Parameter);
            Assert.Equal("{}", await request.Content!.ReadAsStringAsync(token));
            return Response(JsonSerializer.Serialize(new[] { new { organization_id = Organization, created = true } }));
        });
        var result = await new SupabasePortalClient(http, Key).EnsureWorkspaceAsync(new(Actor, "reader@example.invalid", Bearer), default);
        Assert.Equal(Organization, result.Id);
        Assert.True(result.Created);
    }

    [Fact]
    public async Task AdmissionFixesFixtureAndProfileAndBindsTenantAndRequest()
    {
        var requestId = Guid.NewGuid();
        using var http = Client(async (request, token) =>
        {
            Assert.Equal("/rest/v1/rpc/create_scan", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Bearer, request.Headers.Authorization!.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(Organization, json.RootElement.GetProperty("p_organization_id").GetGuid());
            Assert.Equal(requestId, json.RootElement.GetProperty("p_request_id").GetGuid());
            Assert.Equal("synthetic-shop", json.RootElement.GetProperty("p_fixture").GetString());
            Assert.Equal("hsr-desktop-reader-v13", json.RootElement.GetProperty("p_reader_profile").GetString());
            Assert.Equal(4, json.RootElement.EnumerateObject().Count());
            return Response(JsonSerializer.Serialize(new[] { new { scan_id = Scan, job_id = Job, queue_message_id = 1 } }));
        });
        var result = await new SupabasePortalClient(http, Key).CreateScanAsync(new(Actor, "reader@example.invalid", Bearer), Organization, requestId, default);
        Assert.Equal(Organization, result.OrganizationId);
        Assert.Equal(Scan, result.ScanId);
        Assert.Equal(Job, result.JobId);
    }

    [Fact]
    public async Task PeerTenantProviderRowsCannotBeAcceptedAsRequestedScope()
    {
        var rows = JsonSerializer.Serialize(new[] { new { organization_id = Guid.NewGuid(), id = Scan,
            fixture = "synthetic-shop", reader_profile = "hsr-desktop-reader-v13", state = "queued", created_at = "2026-10-21T00:00:00Z" } });
        using var http = Client((request, _) =>
        {
            Assert.Equal("hsr", request.Headers.GetValues("Accept-Profile").Single());
            Assert.Contains("organization_id=eq." + Organization.ToString("D"), request.RequestUri!.Query);
            return Task.FromResult(Response(rows));
        });
        var fault = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, Key).ScanAsync(new(Actor, "reader@example.invalid", Bearer), Organization, Scan, default));
        Assert.Equal(503, fault.Status);
        Assert.Equal("CONTROL_PROVIDER_RESPONSE_UNCONFIRMED", fault.Code);
    }

    [Fact]
    public async Task CallerCancellationSettlesActualProviderRequestWithoutOutageClassification()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Client(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        });
        using var cancellation = new CancellationTokenSource();
        var actual = new SupabasePortalClient(http, Key).AuthenticateAsync(Bearer, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
        Assert.True(actual.IsCompleted);
    }

    internal static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => new(new Handler(send));
    internal static HttpResponseMessage Response(string body, int status = 200) => new((HttpStatusCode)status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
