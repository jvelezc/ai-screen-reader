namespace HeadlessScreenReader.Domain;

/// <summary>Trusted host-supplied environment evidence, not a website instruction or authorization token.</summary>
public sealed record ReplayEnvironment(string Profile, string BrowserVersion, string DriverVersion,
    string FixtureId, string FixtureSha256, string PolicyId, bool Headed, string FramePolicy);

public sealed record ReplaySourceContract(string Version, string Sha256, int Nodes);
public enum ReplayDisposition { Rejected, AttemptRecorded, CancelledBeforeAttempt, EngineErrorBeforeAttempt }
public sealed record ReplayReceipt(string RequestId, string HistoricalRunId, ReplayDisposition Disposition,
    string Code, ReplayEnvironment RecordedEnvironment, ReplayEnvironment CurrentEnvironment,
    ReplaySourceContract? HistoricalSource, ReplaySourceContract? FreshSource, TraversalReceipt? Result)
{
    public string Schema => "hsr-controlled-replay-v1";
    public AccessibilitySnapshot? PreflightObservation { get; init; }
    public AccessibilitySnapshot? RejectedObservation { get; init; }
}
