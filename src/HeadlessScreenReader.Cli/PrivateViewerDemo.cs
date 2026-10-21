using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public sealed record PrivateViewerProof(string ScanId, string SessionId, bool Ready, string Stage, string? ErrorCode,
    bool SingleExchange, bool WrongIdentityDenied, bool GapDenied, bool StaleEpochDenied, bool RevokedReadDenied,
    bool SameProcessContextDocument, bool LastPersistedObservationVerified, bool NoViewerInput, bool CredentialsAbsentFromOwnedFiles,
    bool ServerCloseConfirmed, bool BrowserCloseConfirmed, string Report)
{
    public string Schema => "hsr-private-viewer-proof-v1";
    public bool RemoteIdentityProviderValidated => false;
    public bool HumanAcceptancePerformed => false;
    public bool PublicListenerEnabled => false;
}

public static class PrivateViewerDemo
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var proof = await ExecuteAsync(Path.Combine("artifacts", "private-viewer"), cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(proof, TraversalDemo.JsonOptions));
        return proof.Ready ? 0 : 3;
    }
    public static async Task<PrivateViewerProof> ExecuteAsync(string root, CancellationToken cancellationToken = default)
    {
        var store = new OwnedEvidenceFileStore(root);
        var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: cancellationToken);
        var control = new ControlledScanSession(browser, store);
        var scope = new ViewerScope(store.ScanId, control.SessionId); var subject = Guid.NewGuid();
        var identity = new ProcessViewerIdentity(subject); var broker = new PrivateViewerBroker(scope, subject);
        var bootstrap = broker.IssueBootstrap(subject, scope, TimeSpan.FromMinutes(1));
        PrivateViewerServer? server = null; Uri? address = null; FixtureProcessIdentity? before = null;
        var secrets = new List<string> { identity.Credential.Value, bootstrap.Secret.Value };
        var stage = "launch"; string? error = null;
        var once = false; var deniedIdentity = false; var gap = false; var stale = false; var revoked = false;
        var same = false; var retained = false; var noInput = false; var hostClosed = false; var browserClosed = false;
        try
        {
            before = await browser.CaptureFixtureProcessIdentityAsync(cancellationToken);
            var ai = await control.AcquireAsync(TimeSpan.FromMinutes(2));
            var initial = await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent, cancellationToken: cancellationToken);
            server = await PrivateViewerServer.StartAsync(identity, new PrivateViewerService(broker, control), cancellationToken);
            address = server.BaseAddress;
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false })
            { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
            stage = "exchange";
            using var exchanged = await ExchangeAsync(client, identity.Credential.Value, scope, bootstrap.Secret.Value, cancellationToken);
            exchanged.EnsureSuccessStatusCode();
            var connection = JsonSerializer.Deserialize<ViewerConnectionResponse>(await exchanged.Content.ReadAsStringAsync(cancellationToken))!;
            secrets.Add(connection.Token);
            using var replay = await ExchangeAsync(client, identity.Credential.Value, scope, bootstrap.Secret.Value, cancellationToken);
            once = replay.StatusCode == HttpStatusCode.Unauthorized;
            stage = "read";
            using var read = await SendAsync(client, HttpMethod.Get, ObservationPath(scope, 1), identity.Credential.Value, connection, cancellationToken);
            read.EnsureSuccessStatusCode();
            var observation = JsonSerializer.Deserialize<ViewerObservationResponse>(await read.Content.ReadAsStringAsync(cancellationToken), PrivateViewerService.JsonOptions)!;
            retained = observation.ScanId == scope.ScanId && observation.SessionId == scope.SessionId && observation.Epoch == 1
                && observation.Ordinal == initial.Completion.Intent.Ordinal && observation.ObservationKind == "LastPersistedObservation"
                && observation.Snapshot.DocumentUrl is null
                && JsonSerializer.Serialize(observation.Snapshot) == JsonSerializer.Serialize(initial.Evidence!.Snapshot);
            using var wrongIdentity = await SendAsync(client, HttpMethod.Get, ObservationPath(scope, 1), new string('0', 64), connection, cancellationToken);
            deniedIdentity = wrongIdentity.StatusCode == HttpStatusCode.Unauthorized;
            stage = "exclude";
            await control.ReleaseAsync(ai, cancellationToken);
            using var excluded = await SendAsync(client, HttpMethod.Get, ObservationPath(scope, 1), identity.Credential.Value, connection, cancellationToken);
            gap = excluded.StatusCode == HttpStatusCode.Conflict;
            stage = "resume";
            await control.AcquireAsync(TimeSpan.FromMinutes(1));
            using var staleRead = await SendAsync(client, HttpMethod.Get, ObservationPath(scope, 1), identity.Credential.Value, connection, cancellationToken);
            stale = staleRead.StatusCode == HttpStatusCode.BadRequest;
            using var fresh = await SendAsync(client, HttpMethod.Get, ObservationPath(scope, 2), identity.Credential.Value, connection, cancellationToken);
            fresh.EnsureSuccessStatusCode();
            var baseline = JsonSerializer.Deserialize<ViewerObservationResponse>(await fresh.Content.ReadAsStringAsync(cancellationToken), PrivateViewerService.JsonOptions)!;
            same = before == await browser.CaptureFixtureProcessIdentityAsync(cancellationToken)
                && baseline.Snapshot.DocumentId == observation.Snapshot.DocumentId && baseline.Epoch == 2 && baseline.Ordinal == 0;
            stage = "revoke";
            using var stop = await SendAsync(client, HttpMethod.Post, $"/viewer/{scope.ScanId}/{scope.SessionId}/revoke", identity.Credential.Value, connection, cancellationToken);
            stop.EnsureSuccessStatusCode();
            using var stopped = await SendAsync(client, HttpMethod.Get, ObservationPath(scope, 2), identity.Credential.Value, connection, cancellationToken);
            revoked = stopped.StatusCode == HttpStatusCode.Unauthorized && broker.Status is { ActiveConnections: 0, ActiveReads: 0 };
            noInput = Directory.EnumerateFiles(store.Folder, "command-*.intent.json").Count() == 1 && initial.Completion is { InputRequests: 0, CompletedInputCalls: 0, InputUncertain: false };
            stage = "close";
        }
        catch (OperationCanceledException) { error = "PRIVATE_VIEWER_CANCELLED"; }
        catch { error = "PRIVATE_VIEWER_PROOF_UNCONFIRMED"; }
        finally
        {
            try { await broker.DisposeAsync(); }
            catch { error ??= "PRIVATE_VIEWER_BROKER_CLOSE_UNCONFIRMED"; }
            try { if (server is not null) await server.DisposeAsync(); }
            catch { error ??= "PRIVATE_VIEWER_SERVER_CLOSE_UNCONFIRMED"; }
            try { await control.DisposeAsync(); }
            catch { error ??= "PRIVATE_VIEWER_BROWSER_CLOSE_UNCONFIRMED"; }
        }
        if (address is not null)
        {
            // One native connection attempt avoids HttpClient's connection-refused retry delays.
            using var disconnected = new TcpClient(AddressFamily.InterNetwork);
            try { await disconnected.ConnectAsync(IPAddress.Loopback, address.Port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused) { hostClosed = true; }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or SocketException)
            { /* A timeout, cancellation or another transport failure does not confirm closure. */ }
        }
        if (before is not null) browserClosed = await HeadedSessionDemo.ConfirmProcessExitAsync(before.BrowserProcessId);
        var absent = true;
        foreach (var file in Directory.EnumerateFiles(store.Folder))
        {
            var bytes = await File.ReadAllTextAsync(file, CancellationToken.None);
            if (secrets.Any(bytes.Contains)) absent = false;
        }
        var ready = error is null && once && deniedIdentity && gap && stale && revoked && same && retained && noInput && absent && hostClosed && browserClosed;
        if (ready) stage = "completed";
        else error ??= "PRIVATE_VIEWER_ACCEPTANCE_UNCONFIRMED";
        var proof = new PrivateViewerProof(scope.ScanId, scope.SessionId, ready, stage, error, once, deniedIdentity, gap, stale, revoked,
            same, retained, noInput, absent, hostClosed, browserClosed, Path.Combine(store.Folder, "index.md"));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "viewer-proof.json"), JsonSerializer.Serialize(proof, TraversalDemo.JsonOptions));
        await TraversalArtifactWriter.WriteNewAsync(proof.Report,
            "# Private read-only viewer feasibility\n\nInspect viewer-proof.json and the owned command/baseline/lifecycle files. "
            + "A process-provisioned identity exchanges a bootstrap once, reads only the last acknowledged observation, is denied during exclusion and for a stale epoch, then is revoked. "
            + "Actual Chromium process/context/document remains the same. Viewing sends no input. Bearers and bootstrap values are absent from these files.\n\n"
            + "This loopback-only header-client proof is not a browser UI, public endpoint, Supabase identity-provider acceptance, pixel/VNC viewer, actual human acceptance or credential-entry authorization.\n");
        return proof;
    }
    private static string ObservationPath(ViewerScope scope, long epoch) => $"/viewer/{scope.ScanId}/{scope.SessionId}/observation?epoch={epoch}";
    private static async Task<HttpResponseMessage> ExchangeAsync(HttpClient client, string identity, ViewerScope scope, string bootstrap, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/viewer/exchange");
        request.Headers.Authorization = new("Bearer", identity);
        request.Content = new StringContent(JsonSerializer.Serialize(new ExchangeViewerRequest(scope.ScanId, scope.SessionId, bootstrap)), Encoding.UTF8, "application/json");
        return await client.SendAsync(request, token);
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string identity,
        ViewerConnectionResponse connection, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new("Bearer", identity);
        request.Headers.Add("X-Viewer-Id", connection.ConnectionId); request.Headers.Add("X-Viewer-Token", connection.Token);
        return await client.SendAsync(request, token);
    }
}
