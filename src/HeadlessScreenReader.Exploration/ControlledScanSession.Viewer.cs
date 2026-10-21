using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

public sealed partial class ControlledScanSession
{
    /// <summary>One composition-owned session identity; never an exposed browser connection endpoint.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public Task DeliverRetainedObservationAsync(long epoch, Func<RetainedViewerObservation, Task> deliver, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ExpireIfNeeded(); EnsureOpen();
            if (_phase != ControllerPhase.Active || _role != ControllerRole.Automation)
                throw new BrowserOperationException("CONTROL_VIEW_UNAVAILABLE");
            return _owned.DeliverRetainedObservationAsync(epoch, deliver, cancellationToken);
        }
    }
}
