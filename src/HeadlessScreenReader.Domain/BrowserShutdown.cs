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
    public bool BrowserCloseAcknowledgedBeforeTransport { get; init; }
    public double? NativeElapsedMilliseconds { get; init; }
}
public interface IBrowserShutdownDiagnostics
{
    BrowserShutdownStatus ShutdownStatus { get; }
}
