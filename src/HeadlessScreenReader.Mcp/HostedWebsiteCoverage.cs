using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

internal sealed record HostedWebsiteTaskCoverage(HostedPublicWebsiteStatus Scan, string OriginalSha256,
    int CaptureIndex, int Captures, DateTimeOffset ReceivedAt, PublicTaskCoverage Coverage, PublicTaskStatus[] LatestTasks,
    bool TaskResultsRequireOriginalRead);

public sealed partial class HostedScanClient
{
    public Task<ToolReply> PublicWebsiteCoverageAsync(string scanId, string originalSha256, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var scan) || scan == Guid.Empty) return Task.FromResult(PublicError("PUBLIC_WEBSITE_SCAN_ID_REQUIRED"));
        if (originalSha256 is null || !Digest(originalSha256)) return Task.FromResult(PublicError("PUBLIC_WEBSITE_DIGEST_REQUIRED"));
        return PublicLockedAsync(async () =>
        {
            using var json = await _transport.SendAsync(HttpMethod.Get, PublicCollection + $"/{scan:D}/coverage/{originalSha256}").ConfigureAwait(false);
            var value = json.RootElement.Deserialize<HostedWebsiteTaskCoverage>(HostedReaderTransport.Json)
                ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
            ValidatePublicStatus(value.Scan, scan);
            Require(value.Scan.State == "completed" && value.Scan.OriginalSha256 == originalSha256 && value.OriginalSha256 == originalSha256
                && value.CaptureIndex == 1 && value.Captures == 2 && value.ReceivedAt != default && value.TaskResultsRequireOriginalRead);
            var coverage = value.Coverage;
            var raw = json.RootElement.GetProperty("coverage");
            Require(coverage is not null && coverage.Schema == PublicTaskCoverage.CurrentSchema && coverage.Policy == PublicTaskCoverage.CurrentPolicy
                && coverage.DocumentId is { Length: > 0 and <= 8192 } && !string.IsNullOrWhiteSpace(coverage.DocumentId) && coverage.CaptureEpoch >= 1
                && coverage.InspectedNodes is >= 0 and <= 2048 && coverage.InteractiveControls >= 0 && coverage.InteractiveControls <= coverage.InspectedNodes
                && coverage.UnnamedInteractiveControls >= 0 && coverage.UnnamedInteractiveControls <= coverage.InteractiveControls
                && coverage.UnclassifiedInteractiveControls >= 0 && coverage.UnclassifiedInteractiveControls <= coverage.InteractiveControls
                && !coverage.Groups.IsDefault && coverage.Groups.Length == 8 && !coverage.Limitations.IsDefault && coverage.Limitations.Length <= 12
                && coverage.Limitations.All(l => l is not null && l.Length <= 8192) && coverage.TotalLimitations >= coverage.Limitations.Length
                && raw.GetProperty("scope").GetString() == "CapturedPageOnly" && raw.GetProperty("taskExecution").GetString() == "NotStartedByCoverage"
                && !raw.GetProperty("fullSiteCoverageEstablished").GetBoolean() && !raw.GetProperty("legalCaseDetermined").GetBoolean()
                && raw.GetProperty("limitationsTruncated").GetBoolean() == coverage.LimitationsTruncated);
            Require(coverage!.Groups.Select(g => g.Kind).SequenceEqual(new[] { "Pricing", "Catalog", "Contact", "Help", "Search", "FormCompletion", "AccountAccess", "EnrollmentOrCheckout" }));
            var rawGroups = raw.GetProperty("groups").EnumerateArray().ToArray();
            for (var i = 0; i < coverage.Groups.Length; i++)
            {
                var group = coverage.Groups[i]; var original = rawGroups[i];
                Require(group.ServiceLinkProfileAvailable == PublicTaskGoal.SupportsKind(group.Kind)
                    && group.ObservedControls >= 0 && group.ObservedControls <= coverage.InspectedNodes && group.EnabledControls >= 0
                    && group.EnabledControls <= group.ObservedControls && group.ServiceLinkCandidates >= 0 && group.ServiceLinkCandidates <= group.EnabledControls
                    && (group.ServiceLinkProfileAvailable || group.ServiceLinkCandidates == 0) && !group.Examples.IsDefault
                    && group.Examples.Length <= 2 && group.Examples.Length <= group.ObservedControls
                    && original.GetProperty("state").GetString() == group.State && !original.GetProperty("executionAuthorized").GetBoolean()
                    && original.GetProperty("examplesTruncated").GetBoolean() == group.ExamplesTruncated
                    && group.Examples.All(e => e is not null && !string.IsNullOrWhiteSpace(e.NodeId) && e.NodeId.Length <= 8192
                        && e.Role is { Length: > 0 and <= 100 } && e.ExposedName is { Length: <= 8192 }));
            }
            var examples = coverage.Groups.SelectMany(g => g.Examples).ToArray();
            Require(coverage.Groups.Sum(g => g.ObservedControls) <= coverage.InspectedNodes
                && examples.Select(e => e.NodeId).Distinct(StringComparer.Ordinal).Count() == examples.Length
                && value.LatestTasks is not null && value.LatestTasks.Length <= 5);
            var tasks = value.LatestTasks!;
            Require(tasks.Select(t => t.Job.GoalKind).Distinct(StringComparer.Ordinal).Count() == tasks.Length
                && tasks.Select(t => t.Job.JobId).Distinct().Count() == tasks.Length && tasks.Select(t => t.Job.RequestId).Distinct().Count() == tasks.Length);
            foreach (var task in tasks)
            {
                task.Validate(_options.Organization, scan);
                Require(task.Job.ParentOriginalSha256 == originalSha256 && task.Job.Source.TargetUrl == value.Scan.TargetUrl
                    && task.Job.Source.AllowedHost == value.Scan.AllowedHost && task.Job.Source.Profile == value.Scan.ReaderProfile);
            }
            while (true)
            {
                var reply = new ToolReply("website-task-coverage", null, value.Scan.ReaderProfile, value);
                if (Fits(reply)) return reply;
                var groups = value.Coverage.Groups; var index = -1;
                for (var i = groups.Length - 1; i >= 0; i--) if (groups[i].Examples.Length > 0) { index = i; break; }
                if (index >= 0) value = value with
                {
                    Coverage = value.Coverage with
                    { Groups = groups.SetItem(index, groups[index] with { Examples = groups[index].Examples.RemoveAt(groups[index].Examples.Length - 1) }) }
                };
                else if (value.Coverage.Limitations.Length > 0) value = value with
                {
                    Coverage = value.Coverage with
                    { Limitations = value.Coverage.Limitations.RemoveAt(value.Coverage.Limitations.Length - 1) }
                };
                else return PublicError("PUBLIC_WEBSITE_PAGE_TOO_LARGE");
            }
        }, token);
    }
}
