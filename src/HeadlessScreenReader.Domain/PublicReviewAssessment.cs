using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace HeadlessScreenReader.Domain;

/// <summary>A current, read-only evidence assessment of the independent tasks in one source-bound set.</summary>
public sealed record PublicReviewAssessment(string Schema, PublicTaskSet TaskSet,
    ImmutableArray<PublicReviewTaskEvidence> Tasks)
{
    public const string CurrentSchema = "hsr-public-review-evidence-v1";
    public string Scope => "CapturedPageServiceLinks";
    public bool IsAssessment => true;
    public bool WebsiteInputStartedByAssessment => false;
    public bool FullSiteCoverageEstablished => false;
    public bool LegalCaseDetermined => false;
    public string AssistiveTechnologyComparison => "NotEstablishedByThisAssessment";
    public int VerifiedTaskCount => Tasks.IsDefault ? 0 : Tasks.Count(task => task.Availability == "VerifiedOriginals");
    public ImmutableArray<Guid> BarrierJobIds => Tasks.IsDefault ? [] : [.. Tasks.Where(task =>
        task.Availability == "VerifiedOriginals" && task.Assessment?.Outcome == "WebsiteEvidenceReady").Select(task => task.JobId)];
    public string Outcome => !BarrierJobIds.IsEmpty ? "WebsiteEvidenceReady"
        : !Tasks.IsDefaultOrEmpty && Tasks.All(task => task.Availability == "VerifiedOriginals"
            && task.Assessment?.Outcome == "NoBlockingBarrierInTestedTask") ? "NoBlockingBarrierInTestedTasks"
        : "InsufficientEvidence";

    public void Validate(Guid organization, Guid parent, Guid actor, Guid request)
    {
        if (Schema != CurrentSchema || TaskSet is null || Tasks.IsDefault || Tasks.Any(task => task is null))
            throw new BrowserOperationException("PUBLIC_REVIEW_ASSESSMENT_INVALID");
        TaskSet.Validate(organization, parent, actor, request);
        if (Tasks.Length != TaskSet.Tasks.Length || Tasks.Select(task => task.JobId).Distinct().Count() != Tasks.Length)
            throw new BrowserOperationException("PUBLIC_REVIEW_ASSESSMENT_MEMBERS_INVALID");
        for (var i = 0; i < Tasks.Length; i++) Tasks[i].Validate(TaskSet.Tasks[i]);
    }
}

/// <summary>Evidence verified for one exact task, or an explicit reason its originals were not assessed.</summary>
public sealed record PublicReviewTaskEvidence(string Schema, Guid JobId, string Availability,
    string? ReportSha256, Guid? ExecutionId, PublicTaskGoal? Goal, PublicTaskAssessment? Assessment,
    ImmutableArray<PublicTaskFile> OriginalFiles)
{
    public const string CurrentSchema = "hsr-public-review-task-evidence-v1";
    public const int MaximumFiles = 10;
    public static ImmutableArray<string> ConditionIds { get; } = ["MeaningfulTask", "RecordedBarrier", "ExactTrigger",
        "AccessibilityContext", "RepeatedFailure", "PointerComparison", "ReproducibleEnvironment", "OriginalArtifacts"];

    public void Validate(PublicTaskSetMember member)
    {
        void Require([DoesNotReturnIf(false)] bool valid)
        { if (!valid) throw new BrowserOperationException("PUBLIC_REVIEW_TASK_EVIDENCE_INVALID"); }
        Require(member is not null && member.Job is not null && Schema == CurrentSchema && JobId != Guid.Empty && JobId == member.Job.JobId
            && !OriginalFiles.IsDefault && OriginalFiles.Length <= MaximumFiles);
        if (Availability != "VerifiedOriginals")
        {
            Require(Availability switch
            {
                "Pending" => member.State is "queued" or "leased",
                "Stopped" => member.State is "blocked" or "cancelled" or "interrupted",
                "OriginalsUnavailable" or "OriginalsNotRead" => member.State == "completed",
                _ => false
            });
            Require(ReportSha256 is null && ExecutionId is null && Goal is null && Assessment is null && OriginalFiles.IsEmpty);
            return;
        }
        Require(member.State == "completed" && PublicTaskRules.Digest(ReportSha256) && ReportSha256 == member.ReportSha256
            && ExecutionId is { } execution && execution != Guid.Empty && Assessment is not null);
        Goal?.Validate(member.Job.Source);
        Require(Goal is null || Goal.Kind == member.Job.GoalKind);
        Require(!Assessment!.Conditions.IsDefault && Assessment.Conditions.Length == ConditionIds.Length
            && Assessment.Conditions.All(condition => condition is not null)
            && Assessment.Conditions.Select(condition => condition.Id).SequenceEqual(ConditionIds, StringComparer.Ordinal));
        Require(OriginalFiles.All(file => file is not null && PublicTaskRules.FileName(file.Path)
            && PublicTaskRules.Digest(file.Sha256) && file.Bytes is > 0 and <= PublicTaskReport.MaximumArchiveBytes
            && file.MediaType is "application/json" or "video/webm")
            && OriginalFiles.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() == OriginalFiles.Length
            && OriginalFiles.Sum(file => (long)file.Bytes) <= PublicTaskReport.MaximumArchiveBytes);
        bool Condition(string id) => Assessment.Conditions.Single(condition => condition.Id == id).Satisfied;
        Require(Condition("ExactTrigger") == Condition("RepeatedFailure")
            && Condition("RecordedBarrier") == (Condition("RepeatedFailure") && Condition("PointerComparison")));
        if (Condition("OriginalArtifacts"))
            Require(OriginalFiles.Count(file => file.MediaType == "video/webm") == 3
                && OriginalFiles.Count(file => file.MediaType == "application/json") >= 6);
        if (!Condition("MeaningfulTask")) Require(Goal is null);
        else Require(Goal is not null);
        Require(Assessment.Outcome switch
        {
            "WebsiteEvidenceReady" => Assessment.Conditions.All(condition => condition.Satisfied),
            "NoBlockingBarrierInTestedTask" => Condition("MeaningfulTask") && Condition("AccessibilityContext")
                && Condition("OriginalArtifacts")
                && !Condition("RecordedBarrier") && !Condition("ExactTrigger") && !Condition("RepeatedFailure"),
            "InsufficientEvidence" => !Assessment.Conditions.All(condition => condition.Satisfied),
            _ => false
        });
    }
}
