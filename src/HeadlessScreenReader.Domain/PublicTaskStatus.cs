namespace HeadlessScreenReader.Domain;

public sealed record PublicTaskPublication(string Schema, string ParentOriginalSha256, string ReportSha256, int ReportBytes,
    Guid ExecutionId, bool NativeRemoved, bool OriginalsVerified, PublicTaskAssessment Assessment)
{
    public void Validate(PublicTaskJob job)
    {
        if (Schema != "hsr-public-task-publication-v1" || ParentOriginalSha256 != job.ParentOriginalSha256
            || !PublicTaskRules.Digest(ReportSha256) || ReportBytes is < 1 or > PublicTaskReport.MaximumReportBytes || ExecutionId == Guid.Empty
            || !NativeRemoved || !OriginalsVerified || Assessment is null || Assessment.Conditions.IsDefault || Assessment.Conditions.Length != 8
            || !Assessment.Conditions.Select(c => c.Id).SequenceEqual(new[] { "MeaningfulTask", "RecordedBarrier", "ExactTrigger", "AccessibilityContext",
                "RepeatedFailure", "PointerComparison", "ReproducibleEnvironment", "OriginalArtifacts" })
            || Assessment.Outcome is not ("WebsiteEvidenceReady" or "NoBlockingBarrierInTestedTask" or "InsufficientEvidence")
            || Assessment.Outcome == "WebsiteEvidenceReady" && Assessment.Conditions.Any(c => !c.Satisfied))
            throw new BrowserOperationException("PUBLIC_TASK_PUBLICATION_INVALID");
    }
}

/// <summary>Authorized metadata only. A leased state is not input authority or proof of native cleanup.</summary>
public sealed record PublicTaskStatus(string Schema, PublicTaskJob Job, string State, string Phase, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? FinishedAt, Guid? ProducerId,
    string? ReportSha256, PublicTaskPublication? Publication, string? ErrorCode)
{
    public void Validate(Guid organization, Guid parent, Guid? job = null)
    {
        Job.Validate();
        if (Schema != "hsr-public-task-status-v1" || CreatedBy == Guid.Empty || Job.OrganizationId != organization || Job.ParentScanId != parent
            || job is { } id && Job.JobId != id || CreatedAt == default || UpdatedAt < CreatedAt
            || State is not ("queued" or "leased" or "completed" or "blocked" or "cancelled" or "interrupted")
            || Phase is not ("Queued" or "Starting" or "KeyboardAttempt1" or "KeyboardAttempt2" or "PointerComparison" or "Finalizing" or "Finished")
            || ErrorCode is not null && (ErrorCode.Length > 100 || !ErrorCode.StartsWith("PUBLIC_TASK_", StringComparison.Ordinal)
                || ErrorCode.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')))
            throw new BrowserOperationException("PUBLIC_TASK_STATUS_INVALID");
        if (State == "completed")
        {
            if (ProducerId is null || ProducerId == Guid.Empty || Publication is null || ReportSha256 != Publication.ReportSha256
                || FinishedAt is null || ErrorCode is not null) throw new BrowserOperationException("PUBLIC_TASK_STATUS_INVALID");
            Publication.Validate(Job);
        }
        else if (Publication is not null || ProducerId is not null || ReportSha256 is not null) throw new BrowserOperationException("PUBLIC_TASK_STATUS_INVALID");
    }
}
