namespace HeadlessScreenReader.Domain;

public enum BrowserShutdownPhase { NotStarted, ClosingContext, ClosingBrowser, DrainingCalls, Closed, Faulted, StoppingCollectors }
/// <summary>Sanitized actual shutdown facts. Settled and acknowledged are separate observations.</summary>
public sealed record BrowserShutdownStatus(BrowserShutdownPhase Phase, int ActiveCalls,
    bool ContextCloseAcknowledged, bool BrowserCloseAcknowledged, bool ContextCallSettled, bool BrowserCallSettled,
    bool TransportDisposed, bool ContextEscalated, string? ErrorCode)
{
    public bool CollectorStopAcknowledged { get; init; }
    public bool CollectorCallSettled { get; init; }
    public bool BrowserDeadlineExceeded { get; init; }
    public bool BrowserCallRejected { get; init; }
    public bool ProcessExitConfirmed { get; init; }
    public bool ProcessCallSettled { get; init; }
    public bool TransportEscalatedAfterProcessExit { get; init; }
    /// <summary>Transport escalation was chosen while exit confirmation was unavailable; this does not measure kernel exit time.</summary>
    public bool TransportEscalatedBeforeProcessExit { get; init; }
    public bool BrowserCloseAcknowledgedBeforeTransport { get; init; }
    public double? NativeElapsedMilliseconds { get; init; }
    public bool TransportCallSettled { get; init; }
    public double? TransportStopDurationMilliseconds { get; init; }
    /// <summary>Observed OS-exit confirmation relative to native shutdown start, not exact kernel exit time.</summary>
    public double? ProcessExitConfirmationMilliseconds { get; init; }
    public bool OwnedProcessStopRequested { get; init; }
    public bool OwnedProcessStopCallSettled { get; init; }
    public bool OwnedProcessStopAcknowledged { get; init; }
    public double? OwnedProcessStopDurationMilliseconds { get; init; }
}
public interface IBrowserShutdownDiagnostics
{
    BrowserShutdownStatus ShutdownStatus { get; }
}
