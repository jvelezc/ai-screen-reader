using System.Net;
using System.Net.Sockets;
using System.Text;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Egress;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>Read-only public browser wire host. Network proof precedes browser creation.</summary>
internal static class PublicReaderWorker
{
    internal static async Task<int> RunAsync(string[] args, Stream input, Stream output, TextWriter diagnostics,
        CancellationToken token = default, TimeProvider? time = null)
    {
        PublicReaderSource source; IPAddress proxy;
        try
        {
            if (args.Length != 4 || !IPAddress.TryParse(args[0], out proxy!)
                || proxy.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(proxy)
                || proxy.Equals(IPAddress.Any) || !Guid.TryParseExact(args[2], "N", out var owner)
                || owner == Guid.Empty || owner.ToString("N") != args[2]) throw new ArgumentException();
            source = new(PublicReaderSource.CurrentProfile, args[3], args[1]); source.Validate();
        }
        catch (Exception) { await diagnostics.WriteLineAsync("PUBLIC_READER_WORKER_CONFIGURATION_REQUIRED"); return 2; }
        if (!OperatingSystem.IsLinux()) { await diagnostics.WriteLineAsync("PUBLIC_READER_ISOLATION_REQUIRED"); return 2; }
        time ??= TimeProvider.System;
        using var expiry = new CancellationTokenSource(ReaderWorkerRequest.Lifetime, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        ChromiumFixtureBrowser? browser = null; Task? heartbeat = null;
        var lease = false; var exit = 3;
        try
        {
            // Console pipe reads may ignore cancellation once a native read starts.
            // Abandon this one-use transport on expiry; process exit settles it.
            var request = await PublicReaderWorkerWire.ReadRequestAsync(input, stop.Token).WaitAsync(stop.Token).ConfigureAwait(false);
            if (request.Operation != ReaderWorkerOperation.Open) throw new BrowserOperationException("PUBLIC_READER_WORKER_REQUEST_INVALID");
            var policy = new PublicTargetPolicy([source.AllowedHost]);
            using (var proofDeadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                proofDeadline.CancelAfter(TimeSpan.FromSeconds(20));
                var proof = await IsolationProbe.VerifyAsync(proxy, policy, proofDeadline.Token).ConfigureAwait(false);
                if (!proof.Passed) throw new BrowserOperationException("PUBLIC_READER_ISOLATION_UNCONFIRMED");
            }
            await PulseAsync(proxy, args[2], false, stop.Token).ConfigureAwait(false); lease = true;
            heartbeat = RenewAsync(proxy, args[2], stop, heartbeatStop.Token, time);
            browser = await ChromiumFixtureBrowser.LaunchPublicPageAsync(source.TargetUrl, proxy, policy, stop.Token).ConfigureAwait(false);
            var snapshot = await browser.CaptureAsync(stop.Token).ConfigureAwait(false);
            var document = snapshot.DocumentId; var session = request.SessionId; var sequence = 1; var captures = 1;
            var response = Response(request, ReaderWorkerState.Open, snapshot, null);
            response.Validate(request, source); stop.Token.ThrowIfCancellationRequested();
            await PublicReaderWorkerWire.WriteResponseAsync(output, response, stop.Token).ConfigureAwait(false);
            while (true)
            {
                request = await PublicReaderWorkerWire.ReadRequestAsync(input, stop.Token).WaitAsync(stop.Token).ConfigureAwait(false);
                if (request.SessionId != session || request.Sequence != ++sequence)
                    throw new BrowserOperationException("PUBLIC_READER_WORKER_REQUEST_INVALID");
                if (request.Operation == ReaderWorkerOperation.Close)
                {
                    await browser.DisposeAsync().ConfigureAwait(false);
                    response = Response(request, ReaderWorkerState.Closed, null, browser.ShutdownStatus);
                    response.Validate(request, source, document);
                    heartbeatStop.Cancel(); await heartbeat.ConfigureAwait(false);
                    await PulseAsync(proxy, args[2], true, stop.Token).ConfigureAwait(false); lease = false;
                    browser = null; stop.Token.ThrowIfCancellationRequested();
                    await PublicReaderWorkerWire.WriteResponseAsync(output, response, stop.Token).ConfigureAwait(false);
                    exit = 0; break;
                }
                if (request.Operation != ReaderWorkerOperation.Observe || ++captures > ReaderWorkerRequest.MaximumCaptures)
                    throw new BrowserOperationException("PUBLIC_READER_WORKER_REQUEST_INVALID");
                snapshot = await browser.CaptureAsync(stop.Token).ConfigureAwait(false);
                response = Response(request, ReaderWorkerState.Observed, snapshot, null);
                response.Validate(request, source, document); stop.Token.ThrowIfCancellationRequested();
                await PublicReaderWorkerWire.WriteResponseAsync(output, response, stop.Token).ConfigureAwait(false);
            }

            PublicReaderWorkerResponse Response(PublicReaderWorkerRequest request, ReaderWorkerState state,
                AccessibilitySnapshot? captured, BrowserShutdownStatus? shutdown) =>
                new(request.Schema, request.SessionId, request.Sequence, state, source, document, captured, shutdown, 0, false);
        }
        catch (Exception) { exit = 3; }
        finally
        {
            if (browser is not null) { try { await browser.DisposeAsync().ConfigureAwait(false); } catch (Exception) { exit = 3; } }
            heartbeatStop.Cancel();
            if (heartbeat is not null) { try { await heartbeat.ConfigureAwait(false); } catch (Exception) { exit = 3; } }
            if (lease) { try { await PulseAsync(proxy, args[2], true, CancellationToken.None).ConfigureAwait(false); } catch (Exception) { exit = 3; } }
        }
        if (exit != 0) await diagnostics.WriteLineAsync("PUBLIC_READER_WORKER_UNCONFIRMED").ConfigureAwait(false);
        return exit;
    }

    private static async Task RenewAsync(IPAddress proxy, string owner, CancellationTokenSource lifetime,
        CancellationToken token, TimeProvider time)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), time);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                await PulseAsync(proxy, owner, false, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { lifetime.Cancel(); }
    }

    private static async Task PulseAsync(IPAddress proxy, string owner, bool close, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(proxy, 3128, deadline.Token).ConfigureAwait(false);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"{(close ? "HSR-CLOSE" : "HSR-LEASE")} {owner} HTTP/1.1\r\n\r\n"), deadline.Token).ConfigureAwait(false);
        var response = await ProxyProtocol.ReadHeaderAsync(client.GetStream(), deadline.Token).ConfigureAwait(false);
        if (!response.StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal)) throw new IOException("PUBLIC_READER_LEASE_UNCONFIRMED");
    }
}
