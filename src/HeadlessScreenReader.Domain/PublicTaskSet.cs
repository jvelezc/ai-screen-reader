using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>One atomic admission of independent service-link tasks; results require each task's verified originals.</summary>
public sealed record PublicTaskSet(string Schema, Guid OrganizationId, Guid SetId, Guid ParentScanId,
    Guid RequestId, Guid CreatedBy, DateTimeOffset CreatedAt, string ParentOriginalSha256,
    int MaximumTasks, ImmutableArray<string> GoalKinds, PublicReaderSource Source, ImmutableArray<PublicTaskSetMember> Tasks)
{
    public const string CurrentSchema = "hsr-public-task-set-v1";
    public const int MaximumMembers = 4;
    public static ImmutableArray<string> SupportedKinds { get; } = ["Pricing", "Catalog", "Contact", "Help"];
    public string Scope => "CapturedPageServiceLinks";
    public bool TaskResultsRequireOriginalRead => true;
    public bool IsAssessment => false;

    public static bool ValidKinds(ImmutableArray<string> kinds, int maximum) => maximum is >= 1 and <= MaximumMembers
        && !kinds.IsDefault && kinds.Length <= maximum
        && kinds.SequenceEqual(SupportedKinds.Where(kind => kinds.Contains(kind, StringComparer.Ordinal)), StringComparer.Ordinal);

    public void Validate(Guid organization, Guid parent, Guid actor, Guid? request = null)
    {
        if (Schema != CurrentSchema || OrganizationId == Guid.Empty || OrganizationId != organization
            || SetId == Guid.Empty || ParentScanId == Guid.Empty || ParentScanId != parent
            || RequestId == Guid.Empty || request is { } expected && RequestId != expected
            || CreatedBy == Guid.Empty || CreatedBy != actor || CreatedAt == default
            || !PublicTaskRules.Digest(ParentOriginalSha256) || Source is null || !ValidKinds(GoalKinds, MaximumTasks)
            || Tasks.IsDefault || Tasks.Length != GoalKinds.Length || Tasks.Any(task => task is null))
            throw new BrowserOperationException("PUBLIC_TASK_SET_INVALID");
        Source.Validate();
        if (Source != PublicReaderSource.FromUrl(Source.TargetUrl) || new Uri(Source.TargetUrl).Query.Length != 0)
            throw new BrowserOperationException("PUBLIC_TASK_SET_SOURCE_INVALID");
        if (Tasks.Select(task => task.Job?.JobId).Distinct().Count() != Tasks.Length
            || Tasks.Select(task => task.Job?.RequestId).Distinct().Count() != Tasks.Length)
            throw new BrowserOperationException("PUBLIC_TASK_SET_MEMBERS_INVALID");
        for (var i = 0; i < Tasks.Length; i++)
        {
            var task = Tasks[i];
            if (task.Job is null) throw new BrowserOperationException("PUBLIC_TASK_SET_MEMBERS_INVALID");
            task.Validate(organization, parent);
            if (task.CreatedBy != actor || task.CreatedAt < CreatedAt || task.Job.ParentOriginalSha256 != ParentOriginalSha256
                || task.Job.Source != Source || task.Job.GoalKind != GoalKinds[i])
                throw new BrowserOperationException("PUBLIC_TASK_SET_MEMBERS_INVALID");
        }
    }
}

/// <summary>Compact current child metadata, deliberately excluding assessment/publication claims and original bytes.</summary>
public sealed record PublicTaskSetMember(string Schema, PublicTaskJob Job, string State, string Phase, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? FinishedAt, string? ReportSha256, string? ErrorCode)
{
    public const string CurrentSchema = "hsr-public-task-set-member-v1";

    public void Validate(Guid organization, Guid parent)
    {
        if (Job is null) throw new BrowserOperationException("PUBLIC_TASK_SET_MEMBERS_INVALID");
        Job.Validate();
        if (Schema != CurrentSchema || Job.OrganizationId != organization || Job.ParentScanId != parent
            || CreatedBy == Guid.Empty || CreatedAt == default || UpdatedAt < CreatedAt || FinishedAt < CreatedAt
            || State is not ("queued" or "leased" or "completed" or "blocked" or "cancelled" or "interrupted")
            || Phase is not ("Queued" or "Starting" or "KeyboardAttempt1" or "KeyboardAttempt2" or "PointerComparison" or "Finalizing" or "Finished")
            || ErrorCode is not null && (ErrorCode.Length > 100 || !ErrorCode.StartsWith("PUBLIC_TASK_", StringComparison.Ordinal)
                || ErrorCode.Any(c => !char.IsAsciiLetterUpper(c) && c != '_'))
            || State == "completed" && (!PublicTaskRules.Digest(ReportSha256) || FinishedAt is null || ErrorCode is not null)
            || State != "completed" && ReportSha256 is not null)
            throw new BrowserOperationException("PUBLIC_TASK_SET_MEMBERS_INVALID");
    }
}
