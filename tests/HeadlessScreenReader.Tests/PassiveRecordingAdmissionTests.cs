using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class PassiveRecordingAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpAdmissionSelectsRecordingOnlyFromExplicitTypedCallerOption(bool recorded)
    {
        var project = new PortalProjectStub(); await using var server = await PortalHttpTests.Start(project);
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PortalProjectClientTests.Bearer);
        var id = Guid.NewGuid();
        using var response = await http.PostAsJsonAsync($"api/workspaces/{PortalProjectClientTests.Organization:D}/scans", new { requestId = id, recordObservation = recorded });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal(id, project.LastRequest);
        Assert.Equal(recorded ? 1 : 0, project.RecordedCreateCalls); Assert.Equal(1, project.CreateCalls);
        Assert.All(project.SeenBearers, token => Assert.Equal(PortalProjectClientTests.Bearer, token));
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("null")]
    [InlineData("1")]
    public async Task InvalidRecordingOptionCannotAdmitAnyJob(string value)
    {
        var project = new PortalProjectStub(); await using var server = await PortalHttpTests.Start(project);
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PortalProjectClientTests.Bearer);
        using var body = new StringContent($$"""{"requestId":"{{Guid.NewGuid():D}}","recordObservation":{{value}}} """, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"api/workspaces/{PortalProjectClientTests.Organization:D}/scans", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, project.CreateCalls); Assert.Equal(0, project.RecordedCreateCalls);
    }

    [Fact]
    public async Task RecordedAdmissionUsesFixedRpcAndOriginalCallerWithoutTargetOrCredentials()
    {
        var id = Guid.NewGuid();
        using var http = PortalProjectClientTests.Client(async (request, _) =>
        {
            Assert.EndsWith("/rest/v1/rpc/create_recorded_observation", request.RequestUri!.AbsoluteUri);
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization!.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(2, json.RootElement.EnumerateObject().Count());
            Assert.Equal(id, json.RootElement.GetProperty("p_request_id").GetGuid());
            Assert.Equal(PortalProjectClientTests.Organization, json.RootElement.GetProperty("p_organization_id").GetGuid());
            return PortalProjectClientTests.Response(JsonSerializer.Serialize(new[] { new { scan_id = PortalProjectClientTests.Scan, job_id = PortalProjectClientTests.Job } }));
        });
        var result = await new SupabasePortalClient(http, PortalProjectClientTests.Key).CreateRecordedObservationAsync(
            new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer), PortalProjectClientTests.Organization, id, default);
        Assert.Equal(PortalProjectClientTests.Scan, result.ScanId); Assert.Equal(PortalProjectClientTests.Job, result.JobId);
    }
}
