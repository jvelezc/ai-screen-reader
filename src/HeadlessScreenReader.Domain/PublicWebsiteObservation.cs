using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Admitted passive collection; it cannot acquire fixture, recording or task-outcome provenance.</summary>
public sealed record PublicWebsiteJob(Guid OrganizationId, Guid ScanId, Guid JobId, long Generation, PublicReaderSource Source)
{
    public void Validate()
    {
        if (OrganizationId == Guid.Empty || ScanId == Guid.Empty || JobId == Guid.Empty || Generation is < 1 or > 3 || Source is null)
            throw new BrowserOperationException("PUBLIC_WEBSITE_JOB_INVALID");
        Source.Validate();
        if (Source != PublicReaderSource.FromUrl(Source.TargetUrl)) throw new BrowserOperationException("PUBLIC_WEBSITE_SOURCE_NOT_CANONICAL");
    }

    public override string ToString() => $"PublicWebsiteJob({OrganizationId:N}, {JobId:N}, generation {Generation})";
}

/// <summary>Actual passive snapshots retained only after confirmed native closure and resource absence.</summary>
public sealed record PublicWebsiteObservation(string Schema, PublicWebsiteJob Job, Guid WorkerId,
    ImmutableArray<AccessibilitySnapshot> Captures, DateTimeOffset ReceivedAt,
    bool NativeShutdownConfirmed, bool ResourcesAbsenceConfirmed, int DispatchedInputs, bool GoalVerified)
{
    public const string CurrentSchema = "hsr-public-website-observation-v1";
    public const int MaximumBytes = 1024 * 1024;

    public void Validate(PublicWebsiteJob expected, Guid worker)
    {
        expected.Validate();
        if (Schema != CurrentSchema || Job != expected || worker == Guid.Empty || WorkerId != worker || ReceivedAt == default
            || Captures.IsDefault || Captures.Length != 2 || !NativeShutdownConfirmed || !ResourcesAbsenceConfirmed
            || DispatchedInputs != 0 || GoalVerified)
            throw new BrowserOperationException("PUBLIC_WEBSITE_OBSERVATION_INVALID");
        string? document = null;
        foreach (var snapshot in Captures)
        {
            if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.DocumentId) || snapshot.CaptureEpoch != 1
                || document is not null && document != snapshot.DocumentId || snapshot.Nodes.IsDefaultOrEmpty
                || snapshot.Nodes.Length > ReaderWorkerRequest.MaximumNodes || !Job.Source.AllowsDocument(snapshot.DocumentUrl))
                throw new BrowserOperationException("PUBLIC_WEBSITE_OBSERVATION_INVALID");
            document = snapshot.DocumentId;
        }
    }
}
