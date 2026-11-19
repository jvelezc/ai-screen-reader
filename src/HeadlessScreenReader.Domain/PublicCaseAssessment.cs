using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HeadlessScreenReader.Domain;

/// <summary>Counsel's criterion selections and the exact task evidence to screen.</summary>
public sealed record PublicCaseAssessmentRequest(string Profile, Guid JobId,
    [property: JsonRequired] string? ReportSha256, string Coverage, string WebsiteConnection,
    string ClaimantAccess, string Standing)
{
    public const string SupportedProfile = "NinthCircuitTitleIIIInjunctive";
    public const string Unassessed = "Unassessed";
    public const string Supported = "Supported";
    public const string Unsupported = "Unsupported";
    public const string Conflicting = "Conflicting";

    public bool HasSupportedInput() => Profile == SupportedProfile && JobId != Guid.Empty
        && (ReportSha256 is null || PublicTaskRules.Digest(ReportSha256))
        && IsFinding(Coverage) && IsFinding(WebsiteConnection) && IsFinding(ClaimantAccess) && IsFinding(Standing);

    private static bool IsFinding(string? value) => value is Unassessed or Supported or Unsupported or Conflicting;
}

/// <summary>One primary legal authority for counsel's review of a screening criterion.</summary>
public sealed record PublicCaseAuthority(string Label, string Url, string Pinpoint);

/// <summary>One screening criterion, the source of its status, and what the status addresses.</summary>
public sealed record PublicCaseCriterion(string Id, string Status, string Source, string Explanation, PublicCaseAuthority Authority);

/// <summary>Read-only case screening of one task and counsel's selections under the named profile.</summary>
public sealed record PublicCaseAssessment(Guid OrganizationId, Guid ScanId, Guid RequestId, Guid JobId,
    string? ReportSha256, string Profile, ImmutableArray<PublicCaseCriterion> Criteria)
{
    public const string CurrentSchema = "hsr-case-assessment-v1";
    public string Schema => CurrentSchema;

    public string Outcome => Criteria.Any(criterion => criterion.Status == PublicCaseAssessmentRequest.Conflicting)
        ? "ConflictingEvidence"
        : Criteria.Any(criterion => criterion.Status == PublicCaseAssessmentRequest.Unsupported)
            ? "ContraryEvidence"
            : Criteria.Any(criterion => criterion.Status == PublicCaseAssessmentRequest.Unassessed)
                ? "EvidenceGaps"
                : "CounselReview";

    private static readonly PublicCaseAuthority CoverageAuthority = new("Americans with Disabilities Act, Title III",
        "https://www.ada.gov/law-and-regs/ada/", "42 U.S.C. §§ 12181(7), 12182(a)");
    private static readonly PublicCaseAuthority WebsiteAuthority = new("Robles v. Domino’s Pizza, LLC",
        "https://cdn.ca9.uscourts.gov/datastore/opinions/2019/01/15/17-55504.pdf", "913 F.3d 898, 904–05 (9th Cir. 2019)");
    private static readonly PublicCaseAuthority BarrierAuthority = WebsiteAuthority with { Pinpoint = "913 F.3d 898, 907–08 (9th Cir. 2019)" };
    private static readonly PublicCaseAuthority StandingAuthority = new("Chapman v. Pier 1 Imports (U.S.) Inc.",
        "https://cdn.ca9.uscourts.gov/datastore/opinions/2011/01/07/07-16326.pdf", "631 F.3d 939, 944–45, 948–50 (9th Cir. 2011) (en banc)");

    public static PublicCaseAssessment Assess(PublicReviewAssessment review, PublicReviewTaskEvidence task,
        PublicCaseAssessmentRequest request)
    {
        if (!request.HasSupportedInput() || task.JobId != request.JobId || task.ReportSha256 != request.ReportSha256
            || !review.Tasks.Contains(task))
            throw new ArgumentException("Case screening requires the exact selected evidence in the review.", nameof(request));

        var barrier = task.Availability != "VerifiedOriginals"
            ? PublicCaseAssessmentRequest.Unassessed
            : task.Assessment?.Outcome switch
            {
                "WebsiteEvidenceReady" => PublicCaseAssessmentRequest.Supported,
                "NoBlockingBarrierInTestedTask" => PublicCaseAssessmentRequest.Unsupported,
                _ => PublicCaseAssessmentRequest.Unassessed
            };
        var set = review.TaskSet;
        return FromEvidence(set.OrganizationId, set.ParentScanId, set.RequestId, request, barrier);
    }

    /// <summary>Independently checks that a returned screening preserves the authorized scope and exact selections.</summary>
    public bool MatchesRequest(Guid organization, Guid scan, Guid requestId, PublicCaseAssessmentRequest request)
    {
        if (!request.HasSupportedInput() || OrganizationId != organization || ScanId != scan || RequestId != requestId
            || JobId != request.JobId || ReportSha256 != request.ReportSha256 || Profile != request.Profile
            || Criteria.IsDefault || Criteria.Length != 5)
            return false;
        var status = Criteria[2]?.Status;
        if (status is not (PublicCaseAssessmentRequest.Supported or PublicCaseAssessmentRequest.Unsupported or PublicCaseAssessmentRequest.Unassessed)
            || (ReportSha256 is null && status != PublicCaseAssessmentRequest.Unassessed))
            return false;
        return Criteria.SequenceEqual(FromEvidence(organization, scan, requestId, request, status).Criteria);
    }

    private static PublicCaseAssessment FromEvidence(Guid organization, Guid scan, Guid requestId,
        PublicCaseAssessmentRequest request, string barrier)
    {
        var observed = barrier switch
        {
            PublicCaseAssessmentRequest.Supported =>
                "Verified originals document that both keyboard attempts failed to reach the selected destination and a separate mouse attempt reached it.",
            PublicCaseAssessmentRequest.Unsupported =>
                "The verified keyboard attempts reached the selected destination. This evidence addresses only the reported obstacle as tested.",
            _ => "The selected task has no conclusive verified finding about a barrier. Review the task records and fill the identified evidence gaps."
        };
        return new(organization, scan, requestId, request.JobId, request.ReportSha256, request.Profile,
        [
            new("Coverage", request.Coverage, "Counsel",
                "Counsel’s assessment of whether the entity owns, leases, or operates a covered physical public accommodation.", CoverageAuthority),
            new("WebsiteConnection", request.WebsiteConnection, "Counsel",
                "Counsel’s assessment of how the tested website function connects to the goods or services of the identified physical place.", WebsiteAuthority),
            new("Barrier", barrier, "RecordedTask", observed, BarrierAuthority),
            new("ClaimantAccess", request.ClaimantAccess, "Counsel",
                "Counsel’s assessment of the claimant’s disability-related access experience and its relationship to this barrier.", StandingAuthority),
            new("Standing", request.Standing, "Counsel",
                "Counsel’s assessment of standing for prospective relief, including injury, traceability, redressability, and a real threat of continued or renewed injury.", StandingAuthority)
        ]);
    }
}
