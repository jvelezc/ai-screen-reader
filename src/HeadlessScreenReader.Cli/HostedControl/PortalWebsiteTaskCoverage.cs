using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed record PortalWebsiteTaskCoverageResponse(PortalPublicWebsiteStatusResponse Scan, string OriginalSha256,
    int CaptureIndex, int Captures, DateTimeOffset ReceivedAt, PublicTaskCoverage Coverage, PublicTaskStatus[] LatestTasks)
{
    public bool TaskResultsRequireOriginalRead => true;
}

internal sealed partial class SupabasePortalClient
{
    public async Task<PublicTaskStatus[]> PublicTaskCoverageAsync(PortalIdentity actor, Guid organization, Guid parent, string digest, CancellationToken token)
    {
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/read_public_task_coverage", actor.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        { p_organization_id = organization, p_parent_scan_id = parent, p_parent_original_sha256 = digest }), Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        return json.RootElement.Deserialize<PublicTaskStatus[]>(ReaderWorkerWire.Json)
            ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RESPONSE_INVALID");
    }
}

internal sealed partial class PortalPublicWebsiteService
{
    public async Task<PortalWebsiteTaskCoverageResponse> CoverageAsync(PortalIdentity actor, Guid organization, Guid scan,
        string digest, CancellationToken token)
    {
        if (!PortalPublicWebsiteContract.Digest(digest)) throw new PortalFault(400, "CONTROL_PUBLIC_WEBSITE_DIGEST_REQUIRED");
        return await CurrentAsync(actor, organization, async () =>
        {
            var value = await OriginalContextAsync(actor, organization, scan, digest, token).ConfigureAwait(false);
            PublicTaskCoverage coverage;
            try { coverage = WebsiteTaskCoverage.Inspect(value.Observation.Captures[1]); }
            catch (ArgumentException) { throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_INVALID"); }
            var tasks = await (project as IPortalPublicTaskProject ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_COVERAGE_UNAVAILABLE"))
                .PublicTaskCoverageAsync(actor, organization, scan, digest, token).ConfigureAwait(false);
            if (tasks is null || tasks.Length > 5 || tasks.Select(t => t.Job.GoalKind).Distinct(StringComparer.Ordinal).Count() != tasks.Length
                || tasks.Select(t => t.Job.JobId).Distinct().Count() != tasks.Length || tasks.Select(t => t.Job.RequestId).Distinct().Count() != tasks.Length)
                throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RESPONSE_INVALID");
            foreach (var task in tasks)
            {
                task.Validate(organization, scan);
                if (task.Job.ParentOriginalSha256 != digest || task.Job.Source != value.Status.Job.Source)
                    throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RESPONSE_INVALID");
            }
            return BoundCoverage(new(value.Status, digest, 1, 2, value.Observation.ReceivedAt, coverage, tasks));
        }, token).ConfigureAwait(false);
    }

    internal static PortalWebsiteTaskCoverageResponse BoundCoverage(PortalWebsiteTaskCoverageResponse value)
    {
        while (JsonSerializer.SerializeToUtf8Bytes(value, PortalJson.Options).Length > PortalPublicWebsiteContract.MaximumReplyBytes)
        {
            var groups = value.Coverage.Groups;
            var index = -1;
            for (var i = groups.Length - 1; i >= 0; i--) if (groups[i].Examples.Length > 0) { index = i; break; }
            if (index >= 0) value = value with
            { Coverage = value.Coverage with { Groups = groups.SetItem(index, groups[index] with { Examples = groups[index].Examples.RemoveAt(groups[index].Examples.Length - 1) }) } };
            else if (value.Coverage.Limitations.Length > 0) value = value with
            { Coverage = value.Coverage with { Limitations = value.Coverage.Limitations.RemoveAt(value.Coverage.Limitations.Length - 1) } };
            else throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_PAGE_TOO_LARGE");
        }
        return value;
    }
}
