using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class PortalReaderHttpTests
{
    internal static string Collection => $"/api/workspaces/{PortalReaderSessionTests.Organization:D}/reader-sessions";
    internal static Task<HostedControlServer> Start(ReaderProjectStub project, PortalReaderSessions? readers = null) =>
        HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/reader-session-validation"), new string('a', 40), 0),
            project, new PortalObservationStub(), readerSessions: readers);
    internal static HttpClient Client(HostedControlServer server, string bearer = "reader-first")
    {
        var http = new HttpClient { BaseAddress = server.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return http;
    }

    [Fact]
    public async Task RealChromiumHttpSessionObservesExposedSemanticsAndConfirmsCleanup()
    {
        var project = new ReaderProjectStub();
        await using var server = await Start(project);
        using var http = Client(server);
        using var created = await http.PostAsJsonAsync(Collection, new { });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var opened = await created.Content.ReadFromJsonAsync<PortalReaderSessionResponse>();
        Assert.NotNull(opened);
        Assert.Equal("Foundation", opened.Fixture); Assert.True(opened.ReadOnly);
        Assert.Equal("Open", opened.State); Assert.Equal(PortalReaderSessionTests.Organization, opened.OrganizationId);
        var location = created.Headers.Location!;
        Assert.Equal(Collection + "/" + opened.SessionId.ToString("D"), location.ToString());
        Assert.Equal(opened, await http.GetFromJsonAsync<PortalReaderSessionResponse>(location));
        using var captured = await http.PostAsJsonAsync(location + "/observations", new { });
        Assert.Equal(HttpStatusCode.OK, captured.StatusCode);
        var observed = await captured.Content.ReadFromJsonAsync<PortalReaderSnapshotResponse>();
        Assert.NotNull(observed);
        Assert.Equal(opened.SessionId, observed.SessionId);
        Assert.Equal(opened.ReaderProfile, observed.ReaderProfile);
        Assert.False(observed.GoalVerified); Assert.Equal(0, observed.DispatchedInputs);
        Assert.Contains(observed.Snapshot.Nodes, node => node.Role == "heading" && node.HeadingLevel == 1 && node.Name == "Screen reader fixture");
        Assert.Contains(observed.Snapshot.Nodes, node => node.Role == "button" && node.Name == "Open details");
        Assert.NotEmpty(observed.Snapshot.DocumentId);
        var payload = await captured.Content.ReadAsStringAsync();
        Assert.DoesNotContain("synthetic-account-value", payload);
        Assert.DoesNotContain("fixture-secret-do-not-log", payload);
        Assert.DoesNotContain("Hidden diagnostic text", payload);
        Assert.InRange((await captured.Content.ReadAsByteArrayAsync()).Length, 1, 262144);
        Assert.Equal("no-store", captured.Headers.CacheControl!.ToString());
        using var closed = await http.DeleteAsync(location);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(new(opened.SessionId, "Closed"), await closed.Content.ReadFromJsonAsync<PortalReaderClosedResponse>());
        using var stale = await http.GetAsync(location);
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("read")]
    [InlineData("observe")]
    [InlineData("command")]
    [InlineData("timeline")]
    [InlineData("close")]
    public async Task HttpAuthenticationPrecedesEveryReaderResource(string operation)
    {
        var project = new ReaderProjectStub(); var browser = new ReaderBrowserStub(); var opens = 0;
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), () => { opens++; return Task.FromResult<IPortalReaderBrowser>(browser); });
        await using var server = await Start(project, readers);
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        var path = Collection + "/" + Guid.NewGuid().ToString("D");
        using var response = operation switch
        {
            "open" => await http.PostAsJsonAsync(Collection, new { }),
            "observe" => await http.PostAsJsonAsync(path + "/observations", new { }),
            "command" => await http.PostAsJsonAsync(path + "/commands", new PortalReaderCommandRequest("NextHeading", "document")),
            "timeline" => await http.PostAsJsonAsync(path + "/timeline", new PortalReaderTimelineRequest(0, 100)),
            "close" => await http.DeleteAsync(path),
            _ => await http.GetAsync(path)
        };
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal(0, opens); Assert.Equal(0, project.WorkspaceCalls);
        await readers.DisposeAsync(); // DI may never resolve this instance for a rejected request.
    }

    [Theory]
    [InlineData("read")]
    [InlineData("observe")]
    [InlineData("command")]
    [InlineData("timeline")]
    [InlineData("close")]
    public async Task HttpOtherSubjectCannotUseOrCloseAnOwnedSession(string operation)
    {
        var project = new ReaderProjectStub(); var browser = new ReaderBrowserStub();
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), () => Task.FromResult<IPortalReaderBrowser>(browser));
        await using var server = await Start(project, readers);
        using var owner = Client(server); using var other = Client(server, "reader-second");
        using var created = await owner.PostAsJsonAsync(Collection, new { });
        var path = created.Headers.Location!;
        using var denied = operation switch
        {
            "observe" => await other.PostAsJsonAsync(path + "/observations", new { }),
            "command" => await other.PostAsJsonAsync(path + "/commands", new PortalReaderCommandRequest("NextHeading", "document")),
            "timeline" => await other.PostAsJsonAsync(path + "/timeline", new PortalReaderTimelineRequest(0, 100)),
            "close" => await other.DeleteAsync(path),
            _ => await other.GetAsync(path)
        };
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(0, browser.Captures); Assert.Equal(0, browser.Closes);
        using var own = await owner.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.DoesNotContain("private-bearer-canary", await own.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{\"url\":\"https://example.invalid\"}")]
    [InlineData("{\"userId\":\"forged\"}")]
    [InlineData("{\"fixture\":\"CapturePrivacy\"}")]
    [InlineData("{\"key\":\"Enter\"}")]
    public async Task HttpReaderAdmissionRejectsUnimplementedCapabilitiesBeforeLaunch(string json)
    {
        var project = new ReaderProjectStub(); var opens = 0;
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), () => { opens++; return Task.FromResult<IPortalReaderBrowser>(new ReaderBrowserStub()); });
        await using var server = await Start(project, readers);
        using var http = Client(server);
        using var response = await http.PostAsync(Collection, new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, opens); Assert.Equal(0, project.WorkspaceCalls);
    }

    [Fact]
    public async Task HttpCleanupFailureIsSafe503AndDoesNotAcknowledgeClosed()
    {
        var project = new ReaderProjectStub(); var browser = new ReaderBrowserStub { FailClose = true };
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), () => Task.FromResult<IPortalReaderBrowser>(browser));
        await using var server = await Start(project, readers);
        using var http = Client(server);
        using var created = await http.PostAsJsonAsync(Collection, new { });
        using var failed = await http.DeleteAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        using var json = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", json.RootElement.GetProperty("title").GetString());
        Assert.Matches("^[a-f0-9]{32}$", json.RootElement.GetProperty("supportReference").GetString()!);
        Assert.DoesNotContain("private-close-canary", json.RootElement.GetRawText());
        browser.FailClose = false;
        using var retry = await http.DeleteAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }
}
