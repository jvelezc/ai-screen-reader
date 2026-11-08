using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class PortalRequestDiagnosticTests
{
    [Theory]
    [InlineData("CONTROL_PUBLIC_WEBSITE_PROCESSOR_UNAVAILABLE", "CONTROL_PUBLIC_WEBSITE_PROCESSOR_UNAVAILABLE")]
    [InlineData("DATABASE_UNAVAILABLE", "DATABASE_UNAVAILABLE")]
    [InlineData("AUTH_PROVIDER_UNAVAILABLE", "AUTH_PROVIDER_UNAVAILABLE")]
    [InlineData("unknown-private-detail", "CONTROL_REQUEST_FAILED")]
    public async Task DependencyFailureLogsPreserveSafeCodeAndSupportReference(string fault, string expected)
    {
        var events = new ConcurrentQueue<PortalRequestDiagnostic>();
        var project = new PortalProjectStub { BeforeAuth = _ => throw new PortalFault(503, fault) };
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath("artifacts/portal-validation/evidence"), new string('a', 40), 0), project,
            new PortalObservationStub(), diagnostic: events.Enqueue);
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var logged = Assert.Single(events);
        Assert.Equal(expected, logged.ErrorCode);
        Assert.Equal(response.Headers.GetValues("X-HSR-Request-Id").Single(), logged.SupportReference);
        Assert.DoesNotContain("unknown-private-detail", JsonSerializer.Serialize(logged));
    }

    [Fact]
    public async Task RequestsAndProblemsShareGeneratedReferencesWithoutLoggingCallerContent()
    {
        var events = new ConcurrentQueue<PortalRequestDiagnostic>();
        var project = new PortalProjectStub();
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath("artifacts/portal-validation/evidence"), new string('a', 40), 0), project, new PortalObservationStub(), diagnostic: events.Enqueue);
        using var http = PortalHttpTests.Authorized(server);
        http.DefaultRequestHeaders.Add("X-HSR-Request-Id", "caller-must-not-control-reference");
        using var success = await http.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var first = success.Headers.GetValues("X-HSR-Request-Id").Single();
        Assert.Matches("^[a-f0-9]{32}$", first);
        using var denied = await http.GetAsync("/api/session?secret=never-log-this-value");
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        var second = denied.Headers.GetValues("X-HSR-Request-Id").Single();
        Assert.Equal(second, problem.RootElement.GetProperty("supportReference").GetString());
        Assert.NotEqual(first, second);
        var logged = events.ToArray();
        Assert.Equal(2, logged.Length);
        Assert.Equal(first, logged[0].SupportReference); Assert.Equal("PortalSession", logged[0].Operation);
        Assert.Equal(200, logged[0].Status); Assert.Null(logged[0].ErrorCode);
        Assert.Equal(second, logged[1].SupportReference); Assert.Equal(400, logged[1].Status);
        Assert.Equal("CONTROL_QUERY_DENIED", logged[1].ErrorCode); Assert.Null(logged[1].OrganizationId);
        Assert.All(logged, value => Assert.InRange(value.ElapsedMilliseconds, 0, 60_000));
        var text = JsonSerializer.Serialize(logged);
        Assert.DoesNotContain("never-log-this-value", text); Assert.DoesNotContain("caller-must-not-control", text);
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, text); Assert.DoesNotContain("reader@example.invalid", text);
    }

    [Fact]
    public async Task BrokenDiagnosticSinkCannotTurnAnAuthorizedRequestIntoFailure()
    {
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath("artifacts/portal-validation/evidence"), new string('a', 40), 0), new PortalProjectStub(),
            new PortalObservationStub(), diagnostic: _ => throw new IOException("sink unavailable"));
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PortalProjectClientTests.Actor, (await response.Content.ReadFromJsonAsync<PortalSessionResponse>(PortalJson.Options))!.UserId);
    }
}
