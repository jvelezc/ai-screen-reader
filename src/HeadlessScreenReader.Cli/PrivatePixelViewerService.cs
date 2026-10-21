using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli;

/// <summary>Optional PNG-only response sink. It exposes no browser, input or clipboard contract.</summary>
public interface IPrivatePixelViewerService
{
    Task DeliverPixelsAsync(Guid subject, ViewerScope scope, Guid id, string secret, long epoch, HttpResponse response, CancellationToken cancellationToken);
}
public sealed class PrivatePixelViewerService(PrivateViewerBroker broker,
    Func<long, Func<ViewerPixelFrame, Task>, CancellationToken, Task> source) : IPrivatePixelViewerService
{
    public PrivatePixelViewerService(PrivateViewerBroker broker, ControlledScanSession controller) : this(broker, controller.DeliverPixelsAsync)
    {
        if (broker.Status.Scope != new ViewerScope(controller.Status.ScanId, controller.SessionId))
            throw new ArgumentException("VIEWER_OWNER_SCOPE_MISMATCH");
    }
    public Task DeliverPixelsAsync(Guid subject, ViewerScope scope, Guid id, string secret, long epoch, HttpResponse response, CancellationToken cancellationToken)
        => broker.DeliverPixelsAsync(subject, scope, id, secret, epoch, (send, token) => source(epoch, send, token), async (frame, token) =>
        {
            // The broker checks the bounded envelope before any HTTP dispatch. No payload is written to an evidence store.
            response.ContentType = "image/png"; response.ContentLength = frame.Png.Length;
            response.Headers["X-Viewer-Epoch"] = frame.Epoch.ToString(CultureInfo.InvariantCulture);
            response.Headers["X-Viewer-Baseline-Ordinal"] = frame.BaselineOrdinal.ToString(CultureInfo.InvariantCulture);
            response.Headers["X-Viewer-Document-Sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(frame.DocumentId)));
            response.Headers["X-Viewer-Pixel-Kind"] = frame.PixelKind;
            response.Headers["X-Viewer-Simulated-Assistance"] = frame.PriorSimulatedAssistance ? "true" : "false";
            await response.Body.WriteAsync(frame.Png.AsMemory(), token).ConfigureAwait(false);
            await response.Body.FlushAsync(token).ConfigureAwait(false);
            await response.CompleteAsync().ConfigureAwait(false);
        }, cancellationToken);
}
