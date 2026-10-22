using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>One persistent trusted fixture. The supervisor must separately provide process/container isolation.</summary>
internal static class FixtureReaderWorker
{
    internal static async Task<int> RunAsync(Stream input, Stream output, TextWriter diagnostics,
        CancellationToken token = default, TimeProvider? time = null,
        Func<CancellationToken, Task<ChromiumFixtureBrowser>>? open = null)
    {
        time ??= TimeProvider.System;
        using var expiry = new CancellationTokenSource(ReaderWorkerRequest.Lifetime, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
        ChromiumFixtureBrowser? browser = null;
        var exit = 3;
        try
        {
            var request = await ReaderWorkerWire.ReadRequestAsync(input, stop.Token).ConfigureAwait(false);
            if (request.Operation != ReaderWorkerOperation.Open
                || BundledFixtureCatalog.Describe(BundledFixture.Foundation).ContentSha256 != ReaderWorkerResponse.ContentSha256)
                throw new BrowserOperationException("READER_WORKER_REQUEST_INVALID");
            browser = await (open is null
                ? ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Foundation, cancellationToken: stop.Token)
                : open(stop.Token)).ConfigureAwait(false);
            var snapshot = await browser.CaptureAsync(stop.Token).ConfigureAwait(false);
            var document = snapshot.DocumentId;
            var response = Response(request, ReaderWorkerState.Open, document, snapshot, null);
            response.Validate(request);
            await ReaderWorkerWire.WriteResponseAsync(output, response, stop.Token).ConfigureAwait(false);
            var captures = 1;
            var sequence = 1;
            var session = request.SessionId;
            while (true)
            {
                request = await ReaderWorkerWire.ReadRequestAsync(input, stop.Token).ConfigureAwait(false);
                if (request.SessionId != session || request.Sequence != ++sequence)
                    throw new BrowserOperationException("READER_WORKER_REQUEST_INVALID");
                if (request.Operation == ReaderWorkerOperation.Close)
                {
                    await browser.DisposeAsync().ConfigureAwait(false);
                    response = Response(request, ReaderWorkerState.Closed, document, null, browser.ShutdownStatus);
                    response.Validate(request, document);
                    browser = null;
                    await ReaderWorkerWire.WriteResponseAsync(output, response, stop.Token).ConfigureAwait(false);
                    exit = 0;
                    break;
                }
                if (request.Operation != ReaderWorkerOperation.Observe || ++captures > ReaderWorkerRequest.MaximumCaptures)
                    throw new BrowserOperationException("READER_WORKER_REQUEST_INVALID");
                snapshot = await browser.CaptureAsync(stop.Token).ConfigureAwait(false);
                response = Response(request, ReaderWorkerState.Observed, document, snapshot, null);
                response.Validate(request, document);
                await ReaderWorkerWire.WriteResponseAsync(output, response, stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Runtime/transport exceptions may contain private values. Never forward their messages.
            exit = 3;
        }
        finally
        {
            if (browser is not null)
            {
                try { await browser.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { exit = 3; }
            }
        }
        if (exit != 0) await diagnostics.WriteLineAsync("READER_WORKER_UNCONFIRMED").ConfigureAwait(false);
        return exit;
    }

    private static ReaderWorkerResponse Response(ReaderWorkerRequest request, ReaderWorkerState state,
        string document, AccessibilitySnapshot? snapshot, BrowserShutdownStatus? shutdown) =>
        new(ReaderWorkerRequest.CurrentSchema, request.SessionId, request.Sequence, state,
            ReaderWorkerResponse.ContentSha256, document, snapshot, shutdown, 0, false);
}
