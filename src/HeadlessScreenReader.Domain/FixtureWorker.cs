namespace HeadlessScreenReader.Domain;

/// <summary>Nonsecret, fixed observation descriptor. No URL, input or lease capability.</summary>
public sealed record FixtureJob(Guid OrganizationId, Guid ScanId, Guid JobId, long Generation)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool RecordObservation { get; init; }
    public string Reference => $"{OrganizationId:N}/{ScanId:N}/{JobId:N}/{Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)}/observation.json";
    public void Validate()
    {
        if (OrganizationId == Guid.Empty || ScanId == Guid.Empty || JobId == Guid.Empty || Generation is < 1 or > 3)
            throw new BrowserOperationException("WORKER_DESCRIPTOR_INVALID");
    }
}

/// <summary>Observed fixture semantics and actual shutdown; no goal or input success claim.</summary>
public sealed record FixtureObservation(string Schema, FixtureJob Job, string Fixture, string ReaderProfile,
    string FixtureSha256, AccessibilitySnapshot Snapshot, int DispatchedInputs, bool GoalVerified,
    BrowserShutdownStatus Shutdown)
{
    public const string CurrentSchema = "hsr-fixture-worker-observation-v1";
    public const string Profile = "hsr-desktop-reader-v13";
    // SHA256 of the exact embedded UTF-8 traversal fixture. Changing it requires renewed acceptance.
    public const string ContentSha256 = "61327e8f2f92a817a6e4616bcebeb93b1c162ffe6937a36f000cf9313c646a8b";
    public const string RecordingContentSha256 = "b49eef1ff4f1e225ea263fc559c2e4ab5ea75a75a3c6ee091b57980e210a0d7b";
    public const int MaximumBytes = 1_048_576;

    public void Validate(FixtureJob expected)
    {
        expected.Validate();
        if (Job != expected || Schema != CurrentSchema || Fixture != "synthetic-shop" || ReaderProfile != Profile
            || FixtureSha256 != (expected.RecordObservation ? RecordingContentSha256 : ContentSha256) || DispatchedInputs != 0 || GoalVerified
            || Snapshot is null || string.IsNullOrWhiteSpace(Snapshot.DocumentId)
            || Snapshot.Nodes.IsDefaultOrEmpty || Snapshot.Nodes.Length > 2048 || Snapshot.CaptureEpoch != 1
            || expected.RecordObservation && Snapshot.Nodes.Any(node => node.Role is "textbox" or "code")
            || Shutdown is not
            {
                Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0, ErrorCode: null,
                ContextCallSettled: true, BrowserCallSettled: true, CollectorCallSettled: true,
                CollectorStopAcknowledged: true, TransportDisposed: true, TransportCallSettled: true,
                ProcessCallSettled: true, ProcessExitConfirmed: true, BrowserDeadlineExceeded: false, BrowserCallRejected: false
            }
            || Shutdown.ProcessExitConfirmationMilliseconds is not (>= 0 and < 4000)
            || Shutdown.NativeElapsedMilliseconds is not (>= 0 and < 5000)
            || Shutdown.OwnedProcessStopRequested && !Shutdown.OwnedProcessStopCallSettled)
            throw new BrowserOperationException("WORKER_OBSERVATION_UNCONFIRMED");
    }
}
