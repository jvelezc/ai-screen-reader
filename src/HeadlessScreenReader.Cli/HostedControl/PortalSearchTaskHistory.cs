using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Current-caller metadata for one controlled sample, never an assessment or input authority.</summary>
internal sealed record PortalSearchHistory(Guid OrganizationId, string SourceId,
    SyntheticSearchTaskStatus[] Items, Guid? NextBeforeJobId)
{
    public const string CurrentSchema = "hsr-search-task-history-v1";
    public string Schema => CurrentSchema;
    public bool WebsiteInputStarted => false;
    public bool IsAssessment => false;
}

internal sealed partial class SupabasePortalClient
{
    public async Task<PortalSearchHistory> ReadSearchHistoryAsync(PortalIdentity actor, Guid organization,
        string sourceId, Guid? beforeJob, CancellationToken token) =>
        await SearchTaskRpcAsync<PortalSearchHistory>(actor, "read_search_task_history", new
        { p_organization_id = organization, p_source_id = sourceId, p_before_job_id = beforeJob }, token).ConfigureAwait(false)
        ?? throw new PortalFault(503, "CONTROL_SEARCH_HISTORY_RESPONSE_INVALID");
}

internal sealed partial class PortalSearchTasks
{
    internal const int HistoryPageSize = 10;

    internal async Task<PortalSearchHistory> HistoryAsync(PortalIdentity actor, Guid organization,
        string sourceId, Guid? beforeJob, CancellationToken token)
    {
        if (organization == Guid.Empty || beforeJob == Guid.Empty
            || sourceId is not ("search-working" or "search-keyboard-blocked"))
            throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var page = await Client.ReadSearchHistoryAsync(actor, organization, sourceId, beforeJob, token).ConfigureAwait(false);
        ValidateHistory(page, actor.UserId, organization, sourceId, beforeJob);
        return page;
    }

    internal static void ValidateHistory(PortalSearchHistory page, Guid user, Guid organization,
        string sourceId, Guid? beforeJob)
    {
        if (page is null || page.OrganizationId != organization || page.SourceId != sourceId || page.Items is null
            || page.Items.Length > HistoryPageSize || page.NextBeforeJobId == Guid.Empty
            || page.NextBeforeJobId is not null && page.Items.Length != HistoryPageSize) throw Invalid();
        var jobs = new HashSet<Guid>();
        var requests = new HashSet<Guid>();
        SyntheticSearchTaskStatus? previous = null;
        foreach (var status in page.Items)
        {
            if (status is null) throw Invalid();
            Validate(status, organization);
            if (status.CreatedBy != user || status.Job.Source.Id != sourceId
                || status.Job.JobId == beforeJob || !jobs.Add(status.Job.JobId) || !requests.Add(status.RequestId)
                || previous is not null && (previous.CreatedAt < status.CreatedAt
                    || previous.CreatedAt == status.CreatedAt && StringComparer.Ordinal.Compare(
                        previous.Job.JobId.ToString("D"), status.Job.JobId.ToString("D")) >= 0)) throw Invalid();
            previous = status;
        }
        if (page.NextBeforeJobId is not null && page.NextBeforeJobId != page.Items[^1].Job.JobId
            || JsonSerializer.SerializeToUtf8Bytes(page, PortalJson.Options).Length > 128 * 1024) throw Invalid();
        static PortalFault Invalid() => new(503, "CONTROL_SEARCH_HISTORY_RESPONSE_INVALID");
    }
}
