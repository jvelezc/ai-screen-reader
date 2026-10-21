using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser : IPrivatePixelCapture
{
    private bool _pixelViewingEnabled;
    /// <summary>Explicit opt-in for in-memory PNGs of the fixed synthetic fixture. Starts no recording collector.</summary>
    public static async Task<ChromiumFixtureBrowser> LaunchPrivatePixelFixtureAsync(bool headed = false, CancellationToken cancellationToken = default)
    {
        var browser = await LaunchAsync(headed, cancellationToken, BundledFixture.SyntheticHandoff).ConfigureAwait(false);
        browser._pixelViewingEnabled = true;
        return browser;
    }
    public Task<BrowserPixelFrame> CapturePixelsAsync(long epoch, string documentId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_pixelViewingEnabled || _fixture != BundledFixture.SyntheticHandoff || _publicPolicy is not null)
            throw new BrowserOperationException("PIXEL_VIEW_NOT_ENABLED");
        if (epoch < 1 || string.IsNullOrEmpty(documentId)) throw new BrowserOperationException("PIXEL_BINDING_INVALID");
        // Return the actual native operation, not a cancelled/timeout waiter. Exclusion joins all screenshot work.
        return _captureAdmission.RunAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_syntheticCanaryMayRemain || CaptureStatus.Epoch != epoch) throw new BrowserOperationException("PIXEL_CAPTURE_DENIED");
            var before = await CaptureCoreAsync().ConfigureAwait(false);
            if (before.DocumentId != documentId) throw new BrowserOperationException("PIXEL_DOCUMENT_CHANGED");
            var bytes = await _page.ScreenshotAsync(new() { Type = ScreenshotType.Png, FullPage = false, Timeout = 5000 }).ConfigureAwait(false);
            var after = await CaptureCoreAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (_syntheticCanaryMayRemain || after.DocumentId != documentId || CaptureStatus.Epoch != epoch)
                throw new BrowserOperationException("PIXEL_DOCUMENT_CHANGED");
            var png = ImmutableArray.CreateRange(bytes);
            if (!ViewerPixelFrame.HasBoundedPngEnvelope(png)) throw new BrowserOperationException("PIXEL_FORMAT_UNCONFIRMED");
            return new BrowserPixelFrame(documentId, epoch, png);
        });
    }
}
