using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

public sealed partial class ControlledScanSession
{
    /// <summary>Trusted read-only current pixels; no controller or simulated input authority is granted.</summary>
    public Task DeliverPixelsAsync(long epoch, Func<ViewerPixelFrame, Task> deliver, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ExpireIfNeeded(); EnsureOpen();
            if (_phase != ControllerPhase.Active || _role != ControllerRole.Automation)
                throw new BrowserOperationException("CONTROL_VIEW_UNAVAILABLE");
            return _owned.DeliverPixelsAsync(epoch, deliver, cancellationToken);
        }
    }
}
