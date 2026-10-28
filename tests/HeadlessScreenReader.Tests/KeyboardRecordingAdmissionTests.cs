using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class KeyboardRecordingAdmissionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task HttpAdmissionPreservesExactAuthoredRouteAndOriginalCaller(int route)
    {
        var project = new PortalProjectStub(); await using var server = await PortalHttpTests.Start(project);
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PortalProjectClientTests.Bearer);
        var request = Guid.NewGuid(); using var response = await http.PostAsJsonAsync($"api/workspaces/{PortalProjectClientTests.Organization:D}/scans", new { requestId = request, traversalRoute = route });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal(route, project.LastTraversalRoute);
        Assert.Equal(request, project.LastRequest); Assert.Equal(1, project.CreateCalls); Assert.Equal(0, project.RecordedCreateCalls);
        Assert.All(project.SeenBearers, bearer => Assert.Equal(PortalProjectClientTests.Bearer, bearer));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(5, false)]
    [InlineData(1, true)]
    public async Task UnknownRouteOrCombinedPassiveRequestCannotAdmitInput(int route, bool passive)
    {
        var project = new PortalProjectStub(); await using var server = await PortalHttpTests.Start(project);
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PortalProjectClientTests.Bearer);
        using var response = await http.PostAsJsonAsync($"api/workspaces/{PortalProjectClientTests.Organization:D}/scans", new { requestId = Guid.NewGuid(), traversalRoute = route, recordObservation = passive });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, project.CreateCalls); Assert.Equal(0, project.LastTraversalRoute);
    }

    [Fact]
    public async Task ProviderKeyboardAdmissionUsesOnlyFixedRpcRouteAndOriginalScope()
    {
        var requestId = Guid.NewGuid();
        using var http = PortalProjectClientTests.Client(async (request, _) =>
        {
            Assert.EndsWith("/rest/v1/rpc/create_recorded_traversal", request.RequestUri!.AbsoluteUri);
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization!.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(3, body.RootElement.EnumerateObject().Count()); Assert.Equal(3, body.RootElement.GetProperty("p_route").GetInt32());
            Assert.Equal(requestId, body.RootElement.GetProperty("p_request_id").GetGuid());
            Assert.Equal(PortalProjectClientTests.Organization, body.RootElement.GetProperty("p_organization_id").GetGuid());
            return PortalProjectClientTests.Response(JsonSerializer.Serialize(new[] { new { scan_id = PortalProjectClientTests.Scan, job_id = PortalProjectClientTests.Job } }));
        });
        var result = await new SupabasePortalClient(http, PortalProjectClientTests.Key).CreateRecordedTraversalAsync(
            new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer), PortalProjectClientTests.Organization, requestId, 3, default);
        Assert.Equal(PortalProjectClientTests.Job, result.JobId);
    }
}
