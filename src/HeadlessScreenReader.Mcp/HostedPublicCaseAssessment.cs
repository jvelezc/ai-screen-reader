using System.ComponentModel;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    public Task<ToolReply> PublicCaseAssessmentAsync(string scanId, string requestId, PublicCaseAssessmentRequest input,
        CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var scan) || scan == Guid.Empty || !Guid.TryParse(requestId, out var request)
            || request == Guid.Empty || input is null || !input.HasSupportedInput())
            return Task.FromResult(new ToolReply("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_CASE_REQUEST_INVALID"));

        return LockedAsync(async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Post,
                PublicCollection + $"/{scan:D}/tasks/sets/{request:D}/case-assessment", input).ConfigureAwait(false);
            var raw = document.RootElement;
            try
            {
                var value = raw.Deserialize<PublicCaseAssessment>(HostedReaderTransport.Json);
                Require(value is not null && value.MatchesRequest(_options.Organization, scan, request, input)
                    && ReviewTruth(raw, "schema", JsonValueKind.String, PublicCaseAssessment.CurrentSchema)
                    && ReviewTruth(raw, "outcome", JsonValueKind.String, value.Outcome)
                    && JsonElement.DeepEquals(raw, JsonSerializer.SerializeToElement(value, HostedReaderTransport.Json)));
            }
            catch (Exception error) when (error is InvalidOperationException or JsonException)
            { throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }
            var reply = new ToolReply("public-case-assessment", null, PublicTaskJob.Profile, raw.Clone());
            return Fits(reply) ? reply : new("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_CASE_PAGE_TOO_LARGE");
        }, token);
    }
}

[McpServerToolType]
public sealed class HostedPublicCaseTools(HostedScanClient client)
{
    [McpServerTool(Name = "website_case_assessment", ReadOnly = true, OpenWorld = false),
        Description("Screen one exact saved task using the Ninth Circuit Title III prospective-relief profile and counsel's categorical selections. Use current task-set IDs and the selected verified task report digest, or null when no current original is verified. The core verifies scoped originals again. Counsel findings are not verified facts. This computes a review, saves no selections, and starts no website actions. It does not decide whether to file.")]
    public async Task<CallToolResult> Assess(
        string scanId, string requestId, Guid jobId, string? reportSha256, string profile,
        string coverage, string websiteConnection, string claimantAccess, string standing,
        CancellationToken cancellationToken = default) =>
        ReaderTools.Result(await client.PublicCaseAssessmentAsync(scanId, requestId,
            new(profile, jobId, reportSha256, coverage, websiteConnection, claimantAccess, standing), cancellationToken));
}
