using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Bounded screening of a verified original. Truncation is explicit; no task execution or legal conclusion is inferred.</summary>
internal sealed record PortalWebsiteScreeningResponse(PortalPublicWebsiteStatusResponse Scan, string OriginalSha256,
    int CaptureIndex, int Captures, DateTimeOffset ReceivedAt, WebsitePageScreening Screening,
    int TotalIssues, int TotalSuggestedTasks, bool IssuesTruncated, bool TasksTruncated);

internal sealed partial class PortalPublicWebsiteService
{
    internal static readonly JsonSerializerOptions ScreeningJson = new(PortalJson.Options)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };

    public async Task<PortalWebsiteScreeningResponse> ScreeningAsync(PortalIdentity actor, Guid organization, Guid scan,
        string digest, CancellationToken token)
    {
        if (!PortalPublicWebsiteContract.Digest(digest)) throw new PortalFault(400, "CONTROL_PUBLIC_WEBSITE_DIGEST_REQUIRED");
        var value = await OriginalContextAsync(actor, organization, scan, digest, token).ConfigureAwait(false);
        WebsitePageScreening screening;
        try { screening = WebsiteScreening.Inspect(value.Observation.Captures[1]); }
        catch (ArgumentException) { throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_INVALID"); }
        var issues = Math.Min(40, screening.Issues.Length);
        var tasks = Math.Min(20, screening.SuggestedTasks.Length);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var response = new PortalWebsiteScreeningResponse(value.Status, digest, 1, 2, value.Observation.ReceivedAt,
                screening with { Issues = screening.Issues.Take(issues).ToImmutableArray(), SuggestedTasks = screening.SuggestedTasks.Take(tasks).ToImmutableArray() },
                screening.Issues.Length, screening.SuggestedTasks.Length, issues < screening.Issues.Length, tasks < screening.SuggestedTasks.Length);
            if (JsonSerializer.SerializeToUtf8Bytes(response, ScreeningJson).Length <= PortalPublicWebsiteContract.MaximumReplyBytes)
                return response;
            if (issues > 0) issues--;
            else if (tasks > 0) tasks--;
            else throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_PAGE_TOO_LARGE");
        }
    }
}
