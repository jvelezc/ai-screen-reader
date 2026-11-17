using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    public Task<ToolReply> PublicReviewAssessmentAsync(string scanId, string requestId, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var parent) || parent == Guid.Empty || !Guid.TryParse(requestId, out var request) || request == Guid.Empty)
            return Task.FromResult(new ToolReply("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_REVIEW_REQUEST_INVALID"));
        return LockedAsync(async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Get,
                PublicCollection + $"/{parent:D}/tasks/sets/{request:D}/assessment").ConfigureAwait(false);
            var raw = document.RootElement;
            try
            {
                var value = raw.Deserialize<PublicReviewAssessment>(HostedReaderTransport.Json);
                if (value?.TaskSet is null) throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
                value.Validate(_options.Organization, parent, value.TaskSet.CreatedBy, request);
                Require(ReviewTruth(raw, "scope", JsonValueKind.String, "CapturedPageServiceLinks")
                    && ReviewTruth(raw, "isAssessment", JsonValueKind.True)
                    && ReviewTruth(raw, "websiteInputStartedByAssessment", JsonValueKind.False)
                    && ReviewTruth(raw, "fullSiteCoverageEstablished", JsonValueKind.False)
                    && ReviewTruth(raw, "legalCaseDetermined", JsonValueKind.False)
                    && ReviewTruth(raw, "assistiveTechnologyComparison", JsonValueKind.String, "NotEstablishedByThisAssessment")
                    && ReviewTruth(raw, "outcome", JsonValueKind.String, value.Outcome)
                    && raw.TryGetProperty("verifiedTaskCount", out var count) && count.ValueKind == JsonValueKind.Number
                    && count.TryGetInt32(out var actual) && actual == value.VerifiedTaskCount
                    && raw.TryGetProperty("barrierJobIds", out var barriers) && barriers.ValueKind == JsonValueKind.Array
                    && barriers.Deserialize<ImmutableArray<Guid>>(HostedReaderTransport.Json).SequenceEqual(value.BarrierJobIds));
                var set = raw.GetProperty("taskSet");
                Require(ReviewTruth(set, "scope", JsonValueKind.String, "CapturedPageServiceLinks")
                    && ReviewTruth(set, "taskResultsRequireOriginalRead", JsonValueKind.True)
                    && ReviewTruth(set, "isAssessment", JsonValueKind.False));
            }
            catch (Exception error) when (error is BrowserOperationException or ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException)
            { throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }
            var reply = new ToolReply("public-review-assessment", null, PublicTaskJob.Profile, raw.Clone());
            return Fits(reply) ? reply : new("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_REVIEW_PAGE_TOO_LARGE");
        }, token);
    }

    private static bool ReviewTruth(JsonElement element, string name, JsonValueKind kind, string? text = null) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == kind
        && (text is null || value.GetString() == text);
}

[McpServerToolType]
public sealed class HostedPublicReviewTools(HostedScanClient client)
{
    [McpServerTool(Name = "website_review_assessment", ReadOnly = true, OpenWorld = false),
        Description("Read the evidence assessment of this exact saved scanId/task-set requestId. Core independently verifies each available task report and supporting original; partial conditions from different tasks never form one barrier. Includes exact barrier job IDs and pending/stopped/unavailable/not-read members. No new admission, website input, automatic retry or polling. Covers only this captured page's declared public service links; does not determine whole-site accessibility, external assistive-technology equivalence, injury, standing or a legal case. Open each exact task's result, reader, graph and original recordings for its supporting evidence.")]
    public async Task<CallToolResult> Read(string scanId, string requestId, CancellationToken cancellationToken = default) =>
        ReaderTools.Result(await client.PublicReviewAssessmentAsync(scanId, requestId, cancellationToken));
}
