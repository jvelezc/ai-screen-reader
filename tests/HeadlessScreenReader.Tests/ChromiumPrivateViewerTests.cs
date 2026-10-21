using System.Net;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumPrivateViewerTests
{
    [Fact]
    public async Task ActualHttpAndChromium_RetainedReadExclusionFreshBaselineAndRevocationShareOneOwnedSession()
    {
        var proof = await PrivateViewerDemo.ExecuteAsync(Path.Combine("artifacts", "private-viewer-tests"));
        Assert.True(proof.Ready, proof.Stage + ":" + proof.ErrorCode);
        Assert.True(proof.SingleExchange); Assert.True(proof.WrongIdentityDenied); Assert.True(proof.GapDenied); Assert.True(proof.StaleEpochDenied);
        Assert.True(proof.RevokedReadDenied); Assert.True(proof.SameProcessContextDocument); Assert.True(proof.LastPersistedObservationVerified);
        Assert.True(proof.NoViewerInput); Assert.True(proof.CredentialsAbsentFromOwnedFiles);
        Assert.True(proof.ServerCloseConfirmed); Assert.True(proof.BrowserCloseConfirmed);
        Assert.False(proof.RemoteIdentityProviderValidated); Assert.False(proof.HumanAcceptancePerformed); Assert.False(proof.PublicListenerEnabled);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(proof.Report)!, "*.pending"));
    }

    [Fact]
    public async Task ActualViewerRead_ReturnsAcknowledgedEvidenceInsteadOfRecapturingChangedBrowserOrMovingFocus()
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "private-viewer-tests"));
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        await using var control = new ControlledScanSession(browser, store);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        var captured = await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var focus = await browser.DiagnosticPage.EvaluateAsync<string>("() => document.activeElement.tagName");
        await browser.DiagnosticPage.EvaluateAsync("() => { window.viewerKeyCount = 0; document.addEventListener('keydown', () => window.viewerKeyCount++); document.querySelector('h1').textContent = 'Changed after persisted evidence'; }");
        var subject = Guid.NewGuid(); var scope = new ViewerScope(store.ScanId, control.SessionId);
        await using var broker = new PrivateViewerBroker(scope, subject);
        var identity = new ProcessViewerIdentity(subject); var bootstrap = broker.IssueBootstrap(subject, scope, TimeSpan.FromMinutes(1));
        var connection = broker.Exchange(subject, scope, bootstrap.Secret.Value, TimeSpan.FromMinutes(1));
        await using var server = await PrivateViewerServer.StartAsync(identity, new PrivateViewerService(broker, control));
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = server.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/viewer/{scope.ScanId}/{scope.SessionId}/observation?epoch=1");
        request.Headers.Authorization = new("Bearer", identity.Credential.Value);
        request.Headers.Add("X-Viewer-Id", connection.Id.ToString("N")); request.Headers.Add("X-Viewer-Token", connection.Secret.Value);
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("Changed after persisted evidence", text);
        Assert.DoesNotContain("traversal-secret-canary", text); Assert.DoesNotContain("traversal-account-canary", text);
        var observed = JsonSerializer.Deserialize<ViewerObservationResponse>(text, TraversalDemo.JsonOptions)!;
        Assert.Equal(JsonSerializer.Serialize(captured.Evidence!.Snapshot), JsonSerializer.Serialize(observed.Snapshot));
        Assert.Equal(0, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.viewerKeyCount"));
        Assert.Equal(focus, await browser.DiagnosticPage.EvaluateAsync<string>("() => document.activeElement.tagName"));
        Assert.Single(Directory.EnumerateFiles(store.Folder, "command-*.intent.json"));
    }
}
