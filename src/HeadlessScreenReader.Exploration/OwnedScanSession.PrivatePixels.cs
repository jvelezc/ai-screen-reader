using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

public sealed partial class OwnedScanSession
{
    internal Task DeliverPixelsAsync(long epoch, Func<ViewerPixelFrame, Task> deliver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deliver); cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            Ensure(epoch);
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var actual = DeliverPixelsCoreAsync(epoch, deliver, cancellationToken);
        ObserveFault(actual); return actual;
    }
    private async Task DeliverPixelsCoreAsync(long epoch, Func<ViewerPixelFrame, Task> deliver, CancellationToken cancellationToken)
    {
        try
        {
            await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RetainedViewerObservation baseline; Task<BrowserPixelFrame> capture;
                lock (_sync)
                {
                    Ensure(epoch); cancellationToken.ThrowIfCancellationRequested();
                    baseline = _viewerObservation ?? throw new BrowserOperationException("OWNED_VIEW_UNAVAILABLE");
                    if (baseline.Epoch != epoch || baseline.Snapshot.DocumentUrl is not null || _browser is not IPrivatePixelCapture pixels)
                        throw new BrowserOperationException("OWNED_VIEW_UNAVAILABLE");
                    capture = pixels.CapturePixelsAsync(epoch, baseline.Snapshot.DocumentId, cancellationToken);
                }
                var frame = await capture.ConfigureAwait(false);
                Task send;
                lock (_sync)
                {
                    Ensure(epoch); cancellationToken.ThrowIfCancellationRequested();
                    if (frame is null || frame.Epoch != epoch || frame.DocumentId != baseline.Snapshot.DocumentId || !ViewerPixelFrame.HasBoundedPngEnvelope(frame.Png))
                        throw new BrowserOperationException("PIXEL_SOURCE_INVALID");
                    send = deliver(new(_store.ScanId, epoch, baseline.Ordinal, frame.DocumentId, frame.Png, _simulationIntroduced));
                }
                await send.ConfigureAwait(false);
            }
            finally { _commands.Release(); }
        }
        finally { lock (_sync) if (--_active == 0) _drained.TrySetResult(); }
    }
}
