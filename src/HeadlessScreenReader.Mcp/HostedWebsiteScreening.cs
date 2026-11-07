using System.Collections.Immutable;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

internal sealed record HostedScreeningIssue(string Id, string Rule, string NodeId, string Role, string ExposedName,
    bool Disabled, string Classification, string TaskImpact);
internal sealed record HostedScreeningTask(string Id, WebsiteTaskPurpose Purpose, string NodeId, string Role,
    string ExposedName, bool Disabled, string State, bool ExecutionAuthorized);
internal sealed record HostedPageScreening(string Schema, string Policy, string DocumentId, long CaptureEpoch,
    int InspectedNodes, int InteractiveControls, ImmutableArray<HostedScreeningIssue> Issues,
    ImmutableArray<HostedScreeningTask> SuggestedTasks, ImmutableArray<string> Limitations,
    string Scope, string TaskExecution, string TaskBarrier, string EvidenceReadiness, bool LegalCaseDetermined);
internal sealed record HostedWebsiteScreening(HostedPublicWebsiteStatus Scan, string OriginalSha256,
    int CaptureIndex, int Captures, DateTimeOffset ReceivedAt, HostedPageScreening Screening,
    int TotalIssues, int TotalSuggestedTasks, bool IssuesTruncated, bool TasksTruncated);

public sealed partial class HostedScanClient
{
    private static readonly JsonSerializerOptions ScreeningJson = new(HostedReaderTransport.Json)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };

    public Task<ToolReply> PublicWebsiteScreeningAsync(string scanId, string originalSha256, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var scan) || scan == Guid.Empty) return Task.FromResult(PublicError("PUBLIC_WEBSITE_SCAN_ID_REQUIRED"));
        if (originalSha256 is null || !Digest(originalSha256)) return Task.FromResult(PublicError("PUBLIC_WEBSITE_DIGEST_REQUIRED"));
        return PublicLockedAsync(async () =>
        {
            using var json = await _transport.SendAsync(HttpMethod.Get, PublicCollection + $"/{scan:D}/screening/{originalSha256}").ConfigureAwait(false);
            var value = json.RootElement.Deserialize<HostedWebsiteScreening>(ScreeningJson)
                ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
            ValidatePublicStatus(value.Scan, scan);
            var report = value.Screening;
            Require(value.Scan.State == "completed" && value.OriginalSha256 == originalSha256
                && value.Scan.OriginalSha256 == originalSha256 && value.CaptureIndex == 1 && value.Captures == 2
                && value.ReceivedAt != default && report is not null);
            Require(report!.Schema == WebsitePageScreening.CurrentSchema && report.Policy == WebsitePageScreening.CurrentPolicy
                && report.Scope == "CapturedPageOnly" && report.TaskExecution == "NotStarted" && report.TaskBarrier == "NotAssessed"
                && report.EvidenceReadiness == "InsufficientTaskEvidence" && !report.LegalCaseDetermined
                && !string.IsNullOrWhiteSpace(report.DocumentId) && report.DocumentId.Length <= 8192 && report.CaptureEpoch >= 1
                && report.InspectedNodes is >= 1 and <= 2048 && report.InteractiveControls >= 0 && report.InteractiveControls <= report.InspectedNodes
                && !report.Issues.IsDefault && report.Issues.Length <= 40 && !report.SuggestedTasks.IsDefault && report.SuggestedTasks.Length <= 20
                && !report.Limitations.IsDefault && report.Limitations.Length <= 2052 && report.Limitations.All(l => l is not null && l.Length <= 8192)
                && value.TotalIssues >= report.Issues.Length && value.TotalIssues <= report.InteractiveControls
                && value.TotalSuggestedTasks >= report.SuggestedTasks.Length && value.TotalSuggestedTasks <= report.InteractiveControls
                && value.TotalIssues + value.TotalSuggestedTasks <= report.InteractiveControls
                && value.IssuesTruncated == (value.TotalIssues > report.Issues.Length)
                && value.TasksTruncated == (value.TotalSuggestedTasks > report.SuggestedTasks.Length));
            Require(report.Issues.All(i => i is not null && i.Id.Length is > 0 and <= 100 && i.NodeId.Length is > 0 and <= 8192
                && i.Role.Length is > 0 and <= 100 && i.ExposedName.Length <= 8192 && string.IsNullOrWhiteSpace(i.ExposedName)
                && i.Role is "button" or "link" or "textbox" or "searchbox" or "combobox" or "listbox" or "checkbox" or "radio" or "switch"
                    or "slider" or "spinbutton" or "tab" or "menuitem" or "menuitemcheckbox" or "menuitemradio" or "treeitem"
                && i.Rule == "ExposedInteractiveControlNameMissing" && i.Classification == "PotentialIssue" && i.TaskImpact == "NotAssessed")
                && report.Issues.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() == report.Issues.Length
                && report.Issues.Select(i => i.NodeId).Distinct(StringComparer.Ordinal).Count() == report.Issues.Length);
            Require(report.SuggestedTasks.All(t => t is not null && t.Id.Length is > 0 and <= 100 && t.NodeId.Length is > 0 and <= 8192
                && t.Role is "link" or "button" && !string.IsNullOrWhiteSpace(t.ExposedName) && t.ExposedName.Length <= 8192
                && Enum.IsDefined(t.Purpose) && t.State == "NotTested" && !t.ExecutionAuthorized)
                && report.SuggestedTasks.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == report.SuggestedTasks.Length
                && report.SuggestedTasks.Select(t => t.NodeId).Distinct(StringComparer.Ordinal).Count() == report.SuggestedTasks.Length
                && !report.Issues.Select(i => i.NodeId).Intersect(report.SuggestedTasks.Select(t => t.NodeId), StringComparer.Ordinal).Any());
            var issues = report.Issues.Length; var tasks = report.SuggestedTasks.Length;
            while (true)
            {
                var bounded = value with
                {
                    Screening = report with { Issues = report.Issues.Take(issues).ToImmutableArray(), SuggestedTasks = report.SuggestedTasks.Take(tasks).ToImmutableArray() },
                    IssuesTruncated = issues < value.TotalIssues,
                    TasksTruncated = tasks < value.TotalSuggestedTasks
                };
                var reply = new ToolReply("website-screening", null, value.Scan.ReaderProfile, bounded);
                if (Fits(reply)) return reply;
                if (issues > 0) issues--; else if (tasks > 0) tasks--; else return PublicError("PUBLIC_WEBSITE_PAGE_TOO_LARGE");
            }
        }, token);
    }
}
