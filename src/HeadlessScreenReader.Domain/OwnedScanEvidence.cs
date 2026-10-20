using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public sealed record OwnedCommandIntent(string ScanId, long Ordinal, long Epoch, ReaderCommand Command)
{
    public string Schema => "hsr-owned-command-intent-v1";
    public long? ControllerGeneration { get; init; }
    public bool PriorSimulatedAssistance { get; init; }
}
public enum OwnedCompletionState { ResultStored, ResultExcluded, Cancelled, EngineError, WriteUnconfirmed }
public sealed record OwnedCommandCompletion(OwnedCommandIntent Intent, OwnedCompletionState State,
    int InputRequests, int CompletedInputCalls, bool InputUncertain, string? Code)
{
    public string Schema => "hsr-owned-command-completion-v1";
    public bool InputMayHaveOccurred => CompletedInputCalls > 0 || InputUncertain;
}
/// <summary>The last actual acquired snapshot, not proof of a post-input state or destination.</summary>
public sealed record OwnedCommandEvidence(OwnedCommandIntent Intent, ReaderStep Step, AccessibilitySnapshot? Snapshot,
    EvidenceInterval? CaptureWindow, ImmutableArray<ReaderEvent> Events, bool TimelineTruncated, TraversalClock Clock)
{
    public string Schema => "hsr-owned-command-evidence-v1";
}
public sealed record OwnedScanBaseline(string ScanId, long Epoch, AccessibilitySnapshot Snapshot)
{
    public string Schema => "hsr-owned-scan-baseline-v1";
}
public sealed record OwnedScanLifecycle(string ScanId, CaptureLifecycleEvent Event, string? Code)
{
    public string Schema => "hsr-owned-scan-lifecycle-v1";
}
public sealed record OwnedWriteReceipt(string ArtifactId, long Bytes, string Sha256);
public sealed record OwnedCommandReceipt(OwnedCommandCompletion Completion, OwnedCommandEvidence? Evidence,
    OwnedWriteReceipt IntentWrite, OwnedWriteReceipt? ResultWrite, OwnedWriteReceipt CompletionWrite)
{
    public string Schema => "hsr-owned-command-receipt-v1";
}
public sealed record OwnedScanStatus(string ScanId, CaptureStatus Capture, int ActiveOperations, int WritesInFlight, string? FaultCode)
{
    public string Schema => "hsr-owned-scan-status-v1";
    public bool SimulatedAssistanceIntroduced { get; init; }
}

/// <summary>Owned writes acknowledge their actual task, not a cancelled caller's wait. No raw input arguments in metadata.</summary>
public interface IOwnedEvidenceStore
{
    string ScanId { get; }
    Task<OwnedWriteReceipt> WriteIntentAsync(OwnedCommandIntent intent);
    Task<OwnedWriteReceipt> WriteResultAsync(OwnedCommandEvidence evidence);
    Task<OwnedWriteReceipt> WriteCompletionAsync(OwnedCommandCompletion completion);
    Task<OwnedWriteReceipt> WriteBaselineAsync(OwnedScanBaseline baseline);
    Task<OwnedWriteReceipt> WriteLifecycleAsync(OwnedScanLifecycle lifecycle);
}
