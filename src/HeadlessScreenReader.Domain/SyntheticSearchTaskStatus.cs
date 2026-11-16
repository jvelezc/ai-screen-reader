namespace HeadlessScreenReader.Domain;

/// <summary>Publication describes a controlled demonstration, never findings about a submitted website.</summary>
public sealed record SyntheticSearchTaskPublication(string Schema, Guid ExecutionId, string ManifestSha256, int ManifestBytes,
    string ArchiveSha256, int ArchiveBytes, int Files, long Bytes, string Observation, bool NativeRemoved, bool OriginalsVerified, string IndexSha256)
{
    public const string CurrentSchema = "hsr-search-task-publication-v1";
    public string Scope => "ControlledEmbeddedSearchTaskExperiments";
    public string WebsiteEvidenceAssessment => "NotAssessed";
    public string VideoAlignment => "UnverifiedWithoutCalibrationMarkers";
    public bool LegalCaseDetermined => false;

    public void Validate() => ValidateCore(verified: true);
    public void ValidatePrepared() => ValidateCore(verified: false);

    private void ValidateCore(bool verified)
    {
        if (Schema != CurrentSchema || ExecutionId == Guid.Empty || !Digest(IndexSha256) || !Digest(ManifestSha256) || ManifestBytes is < 1 or > 512 * 1024
            || !Digest(ArchiveSha256) || ArchiveBytes is < 1 or > 26 * 1024 * 1024 || Files is < 28 or > 1400
            || Bytes is < 1 or > 25 * 1024 * 1024 || Bytes > ArchiveBytes || !NativeRemoved || OriginalsVerified != verified
            || Observation is not ("AllTestedKeyboardGoalsObserved" or "AKeyboardAlternativeReachedTheGoal"
                or "KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved"))
            throw new BrowserOperationException("SEARCH_TASK_PUBLICATION_INVALID");
    }

    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
}

public sealed record SyntheticSearchTaskStatus(string Schema, SyntheticSearchTaskJob Job, Guid RequestId, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string State, string Phase, DateTimeOffset? FinishedAt,
    Guid? ProducerId, SyntheticSearchTaskPublication? Publication, string? ErrorCode)
{
    public const string CurrentSchema = "hsr-search-task-status-v1";
    public void Validate(Guid organization, Guid? job = null)
    {
        if (Schema != CurrentSchema || Job is null || organization == Guid.Empty || Job.OrganizationId != organization
            || job is { } expected && Job.JobId != expected || RequestId == Guid.Empty || CreatedBy == Guid.Empty
            || CreatedAt == default || UpdatedAt < CreatedAt || ProducerId == Guid.Empty
            || State is not ("queued" or "leased" or "publishing" or "completed" or "blocked" or "cancelled" or "interrupted")
            || Phase is not ("Queued" or "Starting" or "Collecting" or "Publishing" or "Finished")
            || State == "queued" && (Phase != "Queued" || ProducerId is not null)
            || State == "leased" && (ProducerId is null || Phase is not ("Starting" or "Collecting" or "Publishing"))
            || State == "publishing" && (ProducerId is null || Phase != "Publishing")
            || (State == "completed") != (Publication is not null)
            || State == "completed" && (ProducerId is null || Phase != "Finished" || ErrorCode is not null)
            || State is "queued" or "leased" or "publishing" && (FinishedAt is not null || ErrorCode is not null)
            || State is "completed" or "blocked" or "cancelled" or "interrupted" && (FinishedAt is null || FinishedAt < CreatedAt)
            || State == "cancelled" && ErrorCode is not ("SEARCH_TASK_CANCELLED" or "SEARCH_TASK_AUTHORITY_LOST")
            || State == "interrupted" && ErrorCode != "SEARCH_TASK_LEASE_EXPIRED"
            || State == "blocked" && ErrorCode is not ("SEARCH_TASK_AUTHORITY_LOST" or "SEARCH_TASK_EXECUTION_UNCONFIRMED"
                or "SEARCH_TASK_CLEANUP_UNCONFIRMED" or "SEARCH_TASK_PUBLICATION_UNCONFIRMED" or "SEARCH_TASK_PUBLICATION_INVALID"))
            throw new BrowserOperationException("SEARCH_TASK_STATUS_INVALID");
        Job.Validate();
        Publication?.Validate();
    }
}
