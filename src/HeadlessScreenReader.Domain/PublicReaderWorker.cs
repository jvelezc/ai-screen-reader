namespace HeadlessScreenReader.Domain;

/// <summary>A declared HTTPS source, separate from an embedded fixture or a verified goal.</summary>
public sealed record PublicReaderSource(string Profile, string TargetUrl, string AllowedHost)
{
    public const string CurrentProfile = "hsr-approved-public-readonly-v1";

    /// <summary>Validate the submitted spelling before deriving a canonical source and exact DNS host.</summary>
    public static PublicReaderSource FromUrl(string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl) || targetUrl.Length > 2048 || targetUrl != targetUrl.Trim()
            || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
            throw new BrowserOperationException("PUBLIC_READER_SOURCE_INVALID");
        var submitted = new PublicReaderSource(CurrentProfile, targetUrl, uri.IdnHost.ToLowerInvariant());
        submitted.Validate();
        var canonical = submitted with { TargetUrl = new UriBuilder(uri) { Host = submitted.AllowedHost, Port = -1 }.Uri.AbsoluteUri };
        canonical.Validate(); return canonical;
    }

    public void Validate()
    {
        if (Profile != CurrentProfile || string.IsNullOrWhiteSpace(TargetUrl) || TargetUrl.Length > 2048
            || TargetUrl != TargetUrl.Trim() || string.IsNullOrWhiteSpace(AllowedHost) || AllowedHost.Length > 253)
            throw new BrowserOperationException("PUBLIC_READER_SOURCE_INVALID");
        try
        {
            var policy = new PublicTargetPolicy([AllowedHost]);
            if (!AllowsDocument(TargetUrl, policy)) throw new ArgumentException();
        }
        catch (ArgumentException) { throw new BrowserOperationException("PUBLIC_READER_SOURCE_INVALID"); }
    }

    public bool AllowsDocument(string? url) => AllowsDocument(url, new PublicTargetPolicy([AllowedHost]));

    private static bool AllowsDocument(string? url, PublicTargetPolicy policy) => url is not null
        && Uri.TryCreate(url, UriKind.Absolute, out var target) && target.Scheme == Uri.UriSchemeHttps
        && target.Port == 443 && target.Fragment.Length == 0 && policy.AssessUrl(url).Allowed;
}

/// <summary>Read-only public worker operations; no input, account credential or recording.</summary>
public sealed record PublicReaderWorkerRequest(string Schema, Guid SessionId, int Sequence, ReaderWorkerOperation Operation)
{
    public const string CurrentSchema = "hsr-public-reader-worker-v1";

    public void Validate()
    {
        if (Schema != CurrentSchema || SessionId == Guid.Empty || Sequence is < 1 or > ReaderWorkerRequest.MaximumCaptures + 1
            || Sequence == ReaderWorkerRequest.MaximumCaptures + 1 && Operation != ReaderWorkerOperation.Close
            || !Enum.IsDefined(Operation) || (Sequence == 1) != (Operation == ReaderWorkerOperation.Open))
            throw new BrowserOperationException("PUBLIC_READER_WORKER_REQUEST_INVALID");
    }
}

/// <summary>Observed public source or joined native shutdown. No fixture digest or collection/custody claim.</summary>
public sealed record PublicReaderWorkerResponse(string Schema, Guid SessionId, int Sequence, ReaderWorkerState State,
    PublicReaderSource Source, string DocumentId, AccessibilitySnapshot? Snapshot, BrowserShutdownStatus? Shutdown,
    int DispatchedInputs, bool GoalVerified)
{
    public void Validate(PublicReaderWorkerRequest expected, PublicReaderSource expectedSource, string? expectedDocument = null)
    {
        expected.Validate(); expectedSource.Validate();
        var state = expected.Operation switch
        {
            ReaderWorkerOperation.Open => ReaderWorkerState.Open,
            ReaderWorkerOperation.Observe => ReaderWorkerState.Observed,
            _ => ReaderWorkerState.Closed
        };
        if (Schema != PublicReaderWorkerRequest.CurrentSchema || SessionId != expected.SessionId || Sequence != expected.Sequence
            || State != state || Source != expectedSource || string.IsNullOrWhiteSpace(DocumentId)
            || expectedDocument is not null && DocumentId != expectedDocument || DispatchedInputs != 0 || GoalVerified)
            throw new BrowserOperationException("PUBLIC_READER_WORKER_RESPONSE_INVALID");
        if (state != ReaderWorkerState.Closed)
        {
            if (Shutdown is not null || Snapshot is null || Snapshot.DocumentId != DocumentId || Snapshot.CaptureEpoch != 1
                || Snapshot.Nodes.IsDefaultOrEmpty || Snapshot.Nodes.Length > ReaderWorkerRequest.MaximumNodes
                || !Source.AllowsDocument(Snapshot.DocumentUrl))
                throw new BrowserOperationException("PUBLIC_READER_WORKER_RESPONSE_INVALID");
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
            throw new BrowserOperationException("PUBLIC_READER_WORKER_CLOSE_UNCONFIRMED");
    }
}
