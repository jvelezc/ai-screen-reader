using System.Net;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class WorkspaceCatalogTests
{
    private static readonly PortalIdentity Identity = new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static Guid Workspace(int number) => Guid.Parse($"a2000000-0000-0000-0000-{number:x12}");
    private static string Rows(int start, int count) => JsonSerializer.Serialize(Enumerable.Range(start, count)
        .Select(i => new { id = Workspace(i), name = "Workspace " + i }));

    [Fact]
    public async Task ProviderCatalogUsesOriginalRlsBearerFixedBoundAndExplicitContinuation()
    {
        using var handler = new CatalogHandler(_ => Rows(1, 51)); using var http = new HttpClient(handler);
        IPortalProjectClient project = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        var page = await project.WorkspacesAsync(Identity, null, default);
        Assert.Equal(50, page.Workspaces.Length);
        Assert.Equal(Enumerable.Range(1, 50).Select(Workspace), page.Workspaces.Select(w => w.Id));
        Assert.Equal("Workspace 1", page.Workspaces[0].Name);
        Assert.Equal(Workspace(50), page.NextCursor);
        var sent = Assert.Single(handler.DataRequests);
        Assert.Equal("/rest/v1/organizations?select=id,name&order=id.asc&limit=51", sent.Path);
        Assert.Equal(Identity.Token, sent.Bearer);
        Assert.Equal("hsr", sent.Profile);
        Assert.Equal(PortalProjectClientTests.Key, sent.ApiKey);
    }

    [Fact]
    public async Task ActualHttpCatalogPagesByPathWithoutCreatingOrRescopingWorkspace()
    {
        using var handler = new CatalogHandler(path => path.Contains("id=gt.", StringComparison.Ordinal) ? Rows(51, 1) : Rows(1, 51));
        using var providerHttp = new HttpClient(handler);
        await using var server = await Start(new SupabasePortalClient(providerHttp, PortalProjectClientTests.Key));
        using var http = PortalHttpTests.Authorized(server);
        using var first = await http.GetAsync("/api/workspaces");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        using var page = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal(50, page.RootElement.GetProperty("workspaces").GetArrayLength());
        Assert.Equal(Workspace(50), page.RootElement.GetProperty("nextCursor").GetGuid());
        using var second = await http.GetAsync("/api/workspaces/page/" + Workspace(50).ToString("D"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var last = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(Workspace(51), Assert.Single(last.RootElement.GetProperty("workspaces").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, last.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal(2, handler.AuthCalls);
        Assert.Equal(2, handler.DataRequests.Count);
        Assert.EndsWith("&id=gt." + Workspace(50).ToString("D"), handler.DataRequests[1].Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousCatalogCannotContactProviderOrEnumerateNames()
    {
        using var handler = new CatalogHandler(_ => Rows(1, 1)); using var providerHttp = new HttpClient(handler);
        await using var server = await Start(new SupabasePortalClient(providerHttp, PortalProjectClientTests.Key));
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        using var response = await http.GetAsync("/api/workspaces");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(handler.DataRequests); Assert.Equal(0, handler.AuthCalls);
        Assert.DoesNotContain("Workspace 1", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(50)]
    public async Task FinalCatalogPageDoesNotInventContinuationOrCreatePersonalWorkspace(int count)
    {
        using var handler = new CatalogHandler(_ => Rows(1, count)); using var http = new HttpClient(handler);
        IPortalProjectClient project = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        var page = await project.WorkspacesAsync(Identity, null, default);
        Assert.Equal(count, page.Workspaces.Length); Assert.Null(page.NextCursor);
        Assert.Equal(Enumerable.Range(1, count).Select(Workspace), page.Workspaces.Select(w => w.Id));
        Assert.Single(handler.DataRequests); Assert.Equal(0, handler.AuthCalls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[true]")]
    [InlineData("[{}]")]
    [InlineData("[{\"id\":\"00000000-0000-0000-0000-000000000000\",\"name\":\"Hidden\"}]")]
    [InlineData("[{\"id\":\"not-a-workspace\",\"name\":\"Hidden\"}]")]
    [InlineData("[{\"id\":\"a2000000-0000-0000-0000-000000000001\",\"name\":null}]")]
    [InlineData("[{\"id\":\"a2000000-0000-0000-0000-000000000001\",\"name\":7}]")]
    [InlineData("[{\"id\":\"a2000000-0000-0000-0000-000000000001\",\"name\":\"\"}]")]
    [InlineData("[{\"id\":\"a2000000-0000-0000-0000-000000000001\",\"name\":\" \"}]")]
    [InlineData("[{\"id\":\"a2000000-0000-0000-0000-000000000001\",\"name\":\"Line\\nbreak\"}]")]
    public async Task MalformedProviderCatalogCannotBecomeVisibleWorkspaceMetadata(string json)
    {
        using var handler = new CatalogHandler(_ => json); using var http = new HttpClient(handler);
        IPortalProjectClient project = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        var error = await Assert.ThrowsAsync<PortalFault>(() => project.WorkspacesAsync(Identity, null, default));
        Assert.Equal(503, error.Status); Assert.Equal("CONTROL_PROVIDER_RESPONSE_UNCONFIRMED", error.Code);
        Assert.Single(handler.DataRequests);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("reversed")]
    [InlineData("excess")]
    [InlineData("invalid-lookahead")]
    public async Task UnconfirmedOrderBoundOrHiddenLookaheadCannotAuthorizePage(string kind)
    {
        var json = kind switch
        {
            "duplicate" => JsonSerializer.Serialize(new[] { new { id = Workspace(1), name = "A" }, new { id = Workspace(1), name = "B" } }),
            "reversed" => JsonSerializer.Serialize(new[] { new { id = Workspace(2), name = "B" }, new { id = Workspace(1), name = "A" } }),
            "excess" => Rows(1, 52),
            _ => JsonSerializer.Serialize(Enumerable.Range(1, 51).Select(i => new { id = Workspace(i), name = i == 51 ? new string('x', 121) : "Visible" }))
        };
        using var handler = new CatalogHandler(_ => json); using var http = new HttpClient(handler);
        IPortalProjectClient project = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        var error = await Assert.ThrowsAsync<PortalFault>(() => project.WorkspacesAsync(Identity, null, default));
        Assert.Equal(503, error.Status); Assert.Equal("CONTROL_PROVIDER_RESPONSE_UNCONFIRMED", error.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ContinuationCannotRepeatOrMoveBeforeItsOriginalCursor(int first)
    {
        using var handler = new CatalogHandler(_ => Rows(first, 1)); using var http = new HttpClient(handler);
        IPortalProjectClient project = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        var error = await Assert.ThrowsAsync<PortalFault>(() => project.WorkspacesAsync(Identity, Workspace(2), default));
        Assert.Equal("CONTROL_PROVIDER_RESPONSE_UNCONFIRMED", error.Code);
        Assert.EndsWith("&id=gt." + Workspace(2).ToString("D"), Assert.Single(handler.DataRequests).Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401, "CONTROL_AUTHENTICATION_REQUIRED")]
    [InlineData(HttpStatusCode.Forbidden, 403, "CONTROL_WORKSPACE_ACCESS_DENIED")]
    [InlineData(HttpStatusCode.InternalServerError, 503, "CONTROL_DATABASE_UNAVAILABLE")]
    [InlineData(HttpStatusCode.TooManyRequests, 503, "CONTROL_DATABASE_UNAVAILABLE")]
    public async Task ProviderDenialOrOutageCannotBecomeEmptyCatalogOrRawDisclosure(HttpStatusCode status, int expected, string code)
    {
        using var handler = new CatalogHandler(_ => "{\"message\":\"PRIVATE_PROVIDER_DETAIL\"}") { DataStatus = status };
        using var http = new HttpClient(handler); IPortalProjectClient project = new SupabasePortalClient(http, PortalProjectClientTests.Key);
        var error = await Assert.ThrowsAsync<PortalFault>(() => project.WorkspacesAsync(Identity, null, default));
        Assert.Equal(expected, error.Status); Assert.Equal(code, error.Code);
        Assert.DoesNotContain("PRIVATE_PROVIDER_DETAIL", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Identity.Token, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/workspaces?userId=peer")]
    [InlineData("/api/workspaces?limit=5000")]
    [InlineData("/api/workspaces?access_token=query-canary")]
    [InlineData("/api/workspaces/page/a2000000-0000-0000-0000-000000000001?after=peer")]
    public async Task CatalogPreservesGlobalQueryDenialBeforeIdentityLookup(string path)
    {
        using var handler = new CatalogHandler(_ => Rows(1, 1)); using var providerHttp = new HttpClient(handler);
        await using var server = await Start(new SupabasePortalClient(providerHttp, PortalProjectClientTests.Key));
        using var http = PortalHttpTests.Authorized(server); using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CONTROL_QUERY_DENIED", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, handler.AuthCalls); Assert.Empty(handler.DataRequests);
    }

    [Fact]
    public async Task EmptyCursorCannotReachProviderOrRestoreMembership()
    {
        using var handler = new CatalogHandler(_ => Rows(1, 1)); using var providerHttp = new HttpClient(handler);
        IPortalProjectClient project = new SupabasePortalClient(providerHttp, PortalProjectClientTests.Key);
        var error = await Assert.ThrowsAsync<PortalFault>(() => project.WorkspacesAsync(Identity, Guid.Empty, default));
        Assert.Equal(400, error.Status); Assert.Equal("CONTROL_WORKSPACE_CURSOR_INVALID", error.Code); Assert.Empty(handler.DataRequests);
        await using var server = await Start(project); using var http = PortalHttpTests.Authorized(server);
        using var response = await http.GetAsync("/api/workspaces/page/" + Guid.Empty.ToString("D"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(handler.DataRequests); Assert.Equal(1, handler.AuthCalls);
    }

    [Fact]
    public async Task ProviderRevocationIsRecheckedBeforeNextPageWithoutCachedNames()
    {
        using var handler = new CatalogHandler(_ => Rows(1, 51)) { RevokeAfterAuthCalls = 1 };
        using var providerHttp = new HttpClient(handler);
        await using var server = await Start(new SupabasePortalClient(providerHttp, PortalProjectClientTests.Key));
        using var http = PortalHttpTests.Authorized(server);
        using var first = await http.GetAsync("/api/workspaces"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var next = await http.GetAsync("/api/workspaces/page/" + Workspace(50).ToString("D"));
        Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        var body = await next.Content.ReadAsStringAsync();
        Assert.Contains("CONTROL_AUTHENTICATION_REQUIRED", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace 1", body, StringComparison.Ordinal); Assert.DoesNotContain("PRIVATE_PROVIDER_DETAIL", body, StringComparison.Ordinal);
        Assert.Equal(2, handler.AuthCalls); Assert.Single(handler.DataRequests);
    }

    internal static Task<HostedControlServer> Start(IPortalProjectClient project) => HostedControlServer.StartAsync(
        new(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/portal-validation/evidence"), new string('a', 40), 0), project, new PortalObservationStub());

    internal sealed class CatalogHandler(Func<string, string> rows) : HttpMessageHandler
    {
        public int AuthCalls { get; private set; }
        public int? RevokeAfterAuthCalls { get; init; }
        public HttpStatusCode DataStatus { get; init; } = HttpStatusCode.OK;
        public List<(string Path, string? Bearer, string? Profile, string? ApiKey)> DataRequests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.PathAndQuery;
            string json;
            if (request.RequestUri.AbsolutePath == "/auth/v1/user")
            {
                AuthCalls++;
                if (RevokeAfterAuthCalls is { } allowed && AuthCalls > allowed)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    { Content = new StringContent("PRIVATE_PROVIDER_DETAIL", Encoding.UTF8, "text/plain") });
                json = PortalProjectClientTests.UserJson;
            }
            else
            {
                Assert.Equal("/rest/v1/organizations", request.RequestUri.AbsolutePath);
                DataRequests.Add((path, request.Headers.Authorization?.Parameter,
                    request.Headers.TryGetValues("Accept-Profile", out var profile) ? Assert.Single(profile) : null,
                    request.Headers.TryGetValues("apikey", out var key) ? Assert.Single(key) : null));
                json = rows(path);
            }
            var status = request.RequestUri.AbsolutePath == "/auth/v1/user" ? HttpStatusCode.OK : DataStatus;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
