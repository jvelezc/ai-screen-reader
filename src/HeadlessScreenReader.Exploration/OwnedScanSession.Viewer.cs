using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

public sealed partial class OwnedScanSession
{
    private RetainedViewerObservation? _viewerObservation;

    /// <summary>Trusted read-only delivery joins actual capture drain; it never performs a browser capture or input.</summary>
    internal Task DeliverRetainedObservationAsync(long epoch, Func<RetainedViewerObservation, Task> deliver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deliver); cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            Ensure(epoch);
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var actual = DeliverRetainedCoreAsync(epoch, deliver, cancellationToken);
        ObserveFault(actual); return actual;
    }
    private async Task DeliverRetainedCoreAsync(long epoch, Func<RetainedViewerObservation, Task> deliver, CancellationToken cancellationToken)
    {
        try
        {
            await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Task send;
                lock (_sync)
                {
                    Ensure(epoch); cancellationToken.ThrowIfCancellationRequested();
                    if (_viewerObservation is not { } observation || observation.Epoch != epoch)
                        throw new BrowserOperationException("OWNED_VIEW_UNAVAILABLE");
                    send = deliver(observation);
                }
                await send.ConfigureAwait(false);
            }
            finally { _commands.Release(); }
        }
        finally { lock (_sync) if (--_active == 0) _drained.TrySetResult(); }
    }
}
