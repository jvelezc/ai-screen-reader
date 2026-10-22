namespace HeadlessScreenReader.Domain;

public enum ReaderWorkerOperation { Open, Observe, Close }
public enum ReaderWorkerState { Open, Observed, Closed }

/// <summary>Nonsecret fixed-fixture commands. No target, keyboard input or account authority.</summary>
public sealed record ReaderWorkerRequest(string Schema, Guid SessionId, int Sequence, ReaderWorkerOperation Operation)
{
    public const string CurrentSchema = "hsr-reader-worker-v1";
    public const int MaximumCaptures = 100;
    public const int MaximumNodes = 2048;
    public const int MaximumRequestBytes = 1024;
    public const int MaximumResponseBytes = 262144;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public void Validate()
    {
        if (Schema != CurrentSchema || SessionId == Guid.Empty || Sequence is < 1 or > MaximumCaptures + 1
            || Sequence == MaximumCaptures + 1 && Operation != ReaderWorkerOperation.Close
            || !Enum.IsDefined(Operation) || (Sequence == 1) != (Operation == ReaderWorkerOperation.Open))
            throw new BrowserOperationException("READER_WORKER_REQUEST_INVALID");
    }
}

/// <summary>Source observation or confirmed native close. An acknowledgement is never a goal claim.</summary>
public sealed record ReaderWorkerResponse(string Schema, Guid SessionId, int Sequence, ReaderWorkerState State,
    string FixtureSha256, string DocumentId, AccessibilitySnapshot? Snapshot, BrowserShutdownStatus? Shutdown,
    int DispatchedInputs, bool GoalVerified)
{
    // Exact embedded UTF-8 Foundation fixture; changing it requires renewed worker acceptance.
    public const string ContentSha256 = "625c58d518e9655e523ccb92ebe8a460fa87657ef61beffc77afb6d83aac091c";

    public void Validate(ReaderWorkerRequest expected, string? expectedDocument = null)
    {
        expected.Validate();
        var state = expected.Operation switch
        {
            ReaderWorkerOperation.Open => ReaderWorkerState.Open,
            ReaderWorkerOperation.Observe => ReaderWorkerState.Observed,
            _ => ReaderWorkerState.Closed
        };
        if (Schema != ReaderWorkerRequest.CurrentSchema || SessionId != expected.SessionId || Sequence != expected.Sequence
            || State != state || FixtureSha256 != ContentSha256 || string.IsNullOrWhiteSpace(DocumentId)
            || expectedDocument is not null && DocumentId != expectedDocument || DispatchedInputs != 0 || GoalVerified)
            throw new BrowserOperationException("READER_WORKER_RESPONSE_INVALID");

        if (state != ReaderWorkerState.Closed)
        {
            if (Shutdown is not null || Snapshot is null || Snapshot.DocumentId != DocumentId || Snapshot.CaptureEpoch != 1
                || Snapshot.Nodes.IsDefaultOrEmpty || Snapshot.Nodes.Length > ReaderWorkerRequest.MaximumNodes)
                throw new BrowserOperationException("READER_WORKER_RESPONSE_INVALID");
        }
        else if (Snapshot is not null || Shutdown is not
        {
            Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0, ErrorCode: null,
            ContextCallSettled: true, BrowserCallSettled: true, CollectorCallSettled: true,
            CollectorStopAcknowledged: true, TransportDisposed: true, TransportCallSettled: true,
            ProcessCallSettled: true, ProcessExitConfirmed: true, BrowserDeadlineExceeded: false, BrowserCallRejected: false
        }
            || Shutdown.ProcessExitConfirmationMilliseconds is not (>= 0 and < 4000)
            || Shutdown.NativeElapsedMilliseconds is not (>= 0 and < 5000)
            || Shutdown.OwnedProcessStopRequested && !Shutdown.OwnedProcessStopCallSettled)
            throw new BrowserOperationException("READER_WORKER_CLOSE_UNCONFIRMED");
    }
}
