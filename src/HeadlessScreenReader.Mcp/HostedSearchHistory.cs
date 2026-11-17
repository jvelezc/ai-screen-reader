using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

internal sealed record HostedSearchHistory(string Schema, Guid OrganizationId, string SourceId,
    SyntheticSearchTaskStatus[] Items, Guid? NextBeforeJobId, bool WebsiteInputStarted, bool IsAssessment);

public sealed partial class HostedScanClient
{
    public Task<ToolReply> SearchHistoryAsync(string sourceId, string? beforeJobId = null, CancellationToken token = default)
    {
        Guid before = default;
        if (sourceId is not ("search-working" or "search-keyboard-blocked")
            || beforeJobId is not null && (!Guid.TryParseExact(beforeJobId, "D", out before) || before == Guid.Empty))
            return Task.FromResult(new ToolReply("error", null, SyntheticSearchTaskJob.CurrentProfile, ErrorCode: "SEARCH_TASK_REQUEST_INVALID"));
        return LockedAsync(async () =>
        {
            var path = $"/api/workspaces/{_options.Organization:D}/search-demonstrations/history/{sourceId}";
            if (beforeJobId is not null) path += $"/{before:D}";
            using var document = await _transport.SendAsync(HttpMethod.Get, path, null, 200).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var page = SearchValue<HostedSearchHistory>(document.RootElement);
            Require(page.Schema == "hsr-search-task-history-v1" && page.OrganizationId == _options.Organization
                && page.SourceId == sourceId && !page.WebsiteInputStarted && !page.IsAssessment && page.Items is { Length: <= 10 }
                && page.NextBeforeJobId != Guid.Empty && (page.NextBeforeJobId is null || page.Items.Length == 10));
            var jobs = new HashSet<Guid>(); var requests = new HashSet<Guid>();
            SyntheticSearchTaskStatus? previous = null;
            foreach (var item in page.Items ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"))
            {
                var status = item ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
                try { status.Validate(_options.Organization); }
                catch (BrowserOperationException) { Require(false); }
                Require(status.Job.Source.Id == sourceId && status.Job.JobId != before && jobs.Add(status.Job.JobId)
                    && requests.Add(status.RequestId) && (previous is null || previous.CreatedBy == status.CreatedBy
                        && (previous.CreatedAt > status.CreatedAt || previous.CreatedAt == status.CreatedAt
                            && StringComparer.Ordinal.Compare(previous.Job.JobId.ToString("D"), status.Job.JobId.ToString("D")) < 0)));
                previous = status;
            }
            Require(page.NextBeforeJobId is null || page.NextBeforeJobId == page.Items![^1].Job.JobId);
            var reply = new ToolReply("search-demonstration-history", null, SyntheticSearchTaskJob.CurrentProfile, document.RootElement.Clone());
            return Fits(reply) ? reply : new("error", null, SyntheticSearchTaskJob.CurrentProfile, ErrorCode: "SEARCH_TASK_PAGE_TOO_LARGE");
        }, token);
    }
}
