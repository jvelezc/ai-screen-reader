using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public sealed record PrivatePixelProof(string ScanId, bool Ready, string Stage, string? ErrorCode, bool Headed,
    bool ActualPixelsVerified, bool InputAndClipboardDenied, bool NoViewerInput, bool ExcludedRedPositiveControl,
    bool GapDenied, bool StaleEpochDenied, bool RevokedReadDenied, bool SameProcessContextDocument,
    bool PayloadsAbsentFromOwnedFiles, bool ServerCloseConfirmed, bool BrowserCloseConfirmed,
    PixelFixtureColors? BeforePixels, PixelFixtureColors? AfterPixels, string? BeforeSha256, string? AfterSha256,
    HeadedDisplayObservation? Display, HeadedKernelObservation? Kernel, FixtureProcessIdentity? Before, FixtureProcessIdentity? After, string Report)
{
    public string Schema => "hsr-private-pixel-proof-v1";
    public string CaseName => "PrivatePixels_ActualReadOnlyRelayExcludesSyntheticGapAndRevokesOwnedDelivery";
    public bool HumanAcceptancePerformed => false;
    public bool RemoteIdentityProviderValidated => false;
    public bool PublicListenerEnabled => false;
    public BrowserShutdownStatus? Shutdown { get; init; }
}
public static class PrivatePixelDemo
{
    public static async Task<int> RunAsync(bool headed = false, CancellationToken cancellationToken = default)
    {
        var proof = await ExecuteAsync(Path.Combine("artifacts", "private-pixels"), headed, cancellationToken);
        if (headed) await TraversalArtifactWriter.WriteNewAsync("run-proof.json", JsonSerializer.Serialize(proof, TraversalDemo.JsonOptions));
        Console.WriteLine(JsonSerializer.Serialize(proof, TraversalDemo.JsonOptions)); return proof.Ready ? 0 : 3;
    }
    public static async Task<PrivatePixelProof> ExecuteAsync(string root, bool headed = false, CancellationToken cancellationToken = default)
    {
        var store = new OwnedEvidenceFileStore(root); var stage = "display"; string? error = null;
        ChromiumFixtureBrowser? browser = null; ControlledScanSession? control = null; PrivateViewerBroker? broker = null; PrivateViewerServer? server = null;
        HeadedDisplayObservation? display = null; HeadedKernelObservation? kernel = null; FixtureProcessIdentity? before = null, after = null;
        Uri? address = null; var secrets = new List<string>();
        PixelFixtureColors? firstColors = null, lastColors = null; string? firstHash = null, lastHash = null;
        var pixelsVerified = false; var denied = false; var noInput = false; var positive = false; var gap = false; var stale = false;
        var revoked = false; var same = false; var absent = false; var hostClosed = false; var browserClosed = false;
        try
        {
            if (headed)
            {
                display = HeadedDisplayObservation.ReadCurrent(); kernel = HeadedKernelObservation.ReadCurrent();
                if (!kernel.Restricted) throw new BrowserOperationException("PIXEL_HEADED_KERNEL_UNCONFIRMED");
                await TraversalArtifactWriter.WriteNewAsync("display-number.txt", display.Number.ToString(CultureInfo.InvariantCulture) + "\n");
                await TraversalArtifactWriter.WriteNewAsync("display-pid.txt", display.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
            }
            stage = "launch"; browser = await ChromiumFixtureBrowser.LaunchPrivatePixelFixtureAsync(headed, cancellationToken);
            control = new(browser, store); var scope = new ViewerScope(store.ScanId, control.SessionId); var subject = Guid.NewGuid();
            broker = new(scope, subject); var identity = new ProcessViewerIdentity(subject);
            var bootstrap = broker.IssueBootstrap(subject, scope, TimeSpan.FromMinutes(1)); secrets.Add(identity.Credential.Value); secrets.Add(bootstrap.Secret.Value);
            before = await browser.CaptureFixtureProcessIdentityAsync(cancellationToken);
            if (headed && HeadedProcessObservation.Read(before) is { } process && (process.HeadlessSwitchObserved || process.NoSandboxSwitchObserved))
                throw new BrowserOperationException("PIXEL_HEADED_PROCESS_UNCONFIRMED");
            var ai = await control.AcquireAsync(TimeSpan.FromMinutes(2));
            var initial = await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent, cancellationToken: cancellationToken);
            await control.ExecuteAsync(ai, 1, ReaderCommand.Tab, cancellationToken: cancellationToken);
            server = await PrivateViewerServer.StartAsync(identity, new PrivateViewerService(broker, control), cancellationToken, new PrivatePixelViewerService(broker, control));
            address = server.BaseAddress;
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false }) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
            stage = "exchange";
            using var exchange = new HttpRequestMessage(HttpMethod.Post, "/viewer/exchange"); exchange.Headers.Authorization = new("Bearer", identity.Credential.Value);
            exchange.Content = new StringContent(JsonSerializer.Serialize(new ExchangeViewerRequest(scope.ScanId, scope.SessionId, bootstrap.Secret.Value)), Encoding.UTF8, "application/json");
            using var exchanged = await client.SendAsync(exchange, cancellationToken); exchanged.EnsureSuccessStatusCode();
            var connection = JsonSerializer.Deserialize<ViewerConnectionResponse>(await exchanged.Content.ReadAsStringAsync(cancellationToken))!; secrets.Add(connection.Token);
            HttpRequestMessage Request(string suffix, HttpMethod? method = null)
            {
                var request = new HttpRequestMessage(method ?? HttpMethod.Get, $"/viewer/{scope.ScanId}/{scope.SessionId}/{suffix}");
                request.Headers.Authorization = new("Bearer", identity.Credential.Value); request.Headers.Add("X-Viewer-Id", connection.ConnectionId);
                request.Headers.Add("X-Viewer-Token", connection.Token); return request;
            }
            async Task<HttpResponseMessage> Send(string suffix, HttpMethod? method = null)
            { using var request = Request(suffix, method); return await client.SendAsync(request, cancellationToken); }
            stage = "pixels-before"; var state = await browser.ReadPixelFixtureStateAsync();
            using var first = await Send("pixels?epoch=1"); first.EnsureSuccessStatusCode();
            var firstBytes = ImmutableArray.CreateRange(await first.Content.ReadAsByteArrayAsync(cancellationToken));
            firstColors = await browser.DecodePixelFixturePngAsync(firstBytes); firstHash = Convert.ToHexStringLower(SHA256.HashData(firstBytes.AsSpan()));
            var expectedDocumentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(initial.Evidence!.Snapshot!.DocumentId)));
            pixelsVerified = first.Content.Headers.ContentType?.MediaType == "image/png" && firstColors is { Width: 320, Height: 240, Red: 0, Green: > 38400 }
                && first.Headers.GetValues("X-Viewer-Document-Sha256").Single() == expectedDocumentHash
                && first.Headers.GetValues("X-Viewer-Pixel-Kind").Single() == "FreshFixturePixels";
            denied = true;
            foreach (var path in new[] { "keyboard", "pointer", "clipboard", "navigate", "pixels?epoch=1" })
            { using var result = await Send(path, HttpMethod.Post); denied &= result.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed; }
            using (var clipboard = await Send("clipboard")) denied &= clipboard.StatusCode == HttpStatusCode.NotFound;
            noInput = state == await browser.ReadPixelFixtureStateAsync() && initial.Completion.CompletedInputCalls == 0;
            stage = "synthetic-gap"; await control.ReleaseAsync(ai, cancellationToken);
            var assist = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
            var enter = await control.ExecuteSimulatedAssistanceAsync(assist, SyntheticInputAction.EnterCanary, cancellationToken);
            positive = enter.Completion.Input is { CompletedInputCalls: 1, InputUncertain: false, ErrorCode: null }
                && await browser.ObservePrivatePixelPositiveControlAsync();
            using (var excluded = await Send("pixels?epoch=1")) gap = excluded.StatusCode == HttpStatusCode.Conflict;
            var clear = await control.ExecuteSimulatedAssistanceAsync(assist, SyntheticInputAction.ClearCanary, cancellationToken);
            if (clear.Completion.Input is not { CanaryCleared: true, InputUncertain: false, ErrorCode: null }) throw new BrowserOperationException("PIXEL_SYNTHETIC_CLEAR_UNCONFIRMED");
            await control.ReleaseAsync(assist, cancellationToken); await control.AcquireAsync(TimeSpan.FromMinutes(1));
            stage = "pixels-after";
            using (var old = await Send("pixels?epoch=1")) stale = old.StatusCode == HttpStatusCode.BadRequest;
            var afterClear = await browser.ReadPixelFixtureStateAsync();
            using var last = await Send("pixels?epoch=2"); last.EnsureSuccessStatusCode();
            var lastBytes = ImmutableArray.CreateRange(await last.Content.ReadAsByteArrayAsync(cancellationToken));
            lastColors = await browser.DecodePixelFixturePngAsync(lastBytes); lastHash = Convert.ToHexStringLower(SHA256.HashData(lastBytes.AsSpan()));
            pixelsVerified &= lastColors is { Width: 320, Height: 240, Red: 0, Blue: > 38400 } && firstHash != lastHash
                && last.Headers.GetValues("X-Viewer-Document-Sha256").Single() == expectedDocumentHash
                && last.Headers.GetValues("X-Viewer-Epoch").Single() == "2" && last.Headers.GetValues("X-Viewer-Baseline-Ordinal").Single() == "0"
                && last.Headers.GetValues("X-Viewer-Simulated-Assistance").Single() == "true";
            noInput &= afterClear == await browser.ReadPixelFixtureStateAsync() && afterClear.ValueLength == 0;
            after = await browser.CaptureFixtureProcessIdentityAsync(cancellationToken);
            same = before == after && last.Headers.GetValues("X-Viewer-Document-Sha256").Single() == expectedDocumentHash;
            stage = "revoke";
            using (var stop = await Send("revoke", HttpMethod.Post)) stop.EnsureSuccessStatusCode();
            using (var stopped = await Send("pixels?epoch=2")) revoked = stopped.StatusCode == HttpStatusCode.Unauthorized && broker.Status is { ActiveConnections: 0, ActiveReads: 0 };
            stage = "close";
        }
        catch (OperationCanceledException) { error = "PRIVATE_PIXEL_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch (HttpRequestException exception) { error = exception.StatusCode is { } status ? $"PRIVATE_PIXEL_HTTP_{(int)status}" : "PRIVATE_PIXEL_HTTP_UNCONFIRMED"; }
        catch { error = "PRIVATE_PIXEL_PROOF_UNCONFIRMED"; }
        finally
        {
            try { if (broker is not null) await broker.DisposeAsync(); } catch { error ??= "PRIVATE_PIXEL_BROKER_CLOSE_UNCONFIRMED"; }
            try { if (server is not null) await server.DisposeAsync(); } catch { error ??= "PRIVATE_PIXEL_SERVER_CLOSE_UNCONFIRMED"; }
            try { if (control is not null) await control.DisposeAsync(); else if (browser is not null) await browser.DisposeAsync(); }
            catch { error ??= "PRIVATE_PIXEL_BROWSER_CLOSE_UNCONFIRMED"; }
        }
        if (before is not null) browserClosed = await HeadedSessionDemo.ConfirmProcessExitAsync(before.BrowserProcessId);
        if (address is not null)
        {
            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            try { await tcp.ConnectAsync(IPAddress.Loopback, address.Port, CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused) { hostClosed = true; }
            catch (Exception exception) when (exception is SocketException or TimeoutException) { error ??= "PRIVATE_PIXEL_SERVER_EXIT_UNCONFIRMED"; }
        }
        absent = true;
        foreach (var file in Directory.EnumerateFiles(store.Folder, "*", SearchOption.AllDirectories))
        {
            var text = await File.ReadAllTextAsync(file, CancellationToken.None);
            if (secrets.Any(text.Contains) || file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || text.Contains("synthetic-private-", StringComparison.Ordinal) || text.Contains("\"Png\"", StringComparison.Ordinal)) absent = false;
        }
        var ready = error is null && pixelsVerified && denied && noInput && positive && gap && stale && revoked && same && absent && hostClosed && browserClosed
            && (!headed || display is { PrivateAuthority: true, TcpDisabled: true } && kernel is { Restricted: true });
        if (ready) stage = "completed"; else error ??= "PRIVATE_PIXEL_ACCEPTANCE_UNCONFIRMED";
        var proof = new PrivatePixelProof(store.ScanId, ready, stage, error, headed, pixelsVerified, denied, noInput, positive, gap, stale, revoked,
            same, absent, hostClosed, browserClosed, firstColors, lastColors, firstHash, lastHash, display, kernel, before, after, Path.Combine(store.Folder, "index.md"))
        { Shutdown = control?.Status.Shutdown };
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "pixel-proof.json"), JsonSerializer.Serialize(proof, TraversalDemo.JsonOptions));
        await TraversalArtifactWriter.WriteNewAsync(proof.Report, "# Private pixel relay proof\n\nInspect pixel-proof.json and the separate owned intent/completion/lifecycle records. "
            + "Actual delivered PNGs decode to green before assistance and blue after a cleared, excluded synthetic gap. A separate memory-only red positive control was never delivered. "
            + "The scoped relay denies input/clipboard paths, excluded and stale capture, and revoked reconnects; viewing preserves actual focus/value/input counters. "
            + "Only hashes and decoded color counts are retained. Pixels do not verify a goal. This is a loopback header-client proof; human, browser UI and remote identity acceptance remain open.\n");
        return proof;
    }
}
