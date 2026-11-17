using System.ComponentModel;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    public Task<ToolReply> PublicTaskSetAsync(string operation, string scanId, string requestId,
        string? parentOriginalSha256 = null, int maximumTasks = 4, CancellationToken token = default)
    {
        if (operation is not ("start" or "status") || !Guid.TryParse(scanId, out var parent) || parent == Guid.Empty
            || !Guid.TryParse(requestId, out var request) || request == Guid.Empty
            || operation == "start" && (!PublicTaskRules.Digest(parentOriginalSha256) || maximumTasks is < 1 or > PublicTaskSet.MaximumMembers))
            return Task.FromResult(new ToolReply("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_TASK_SET_REQUEST_INVALID"));
        return LockedAsync(async () =>
        {
            var path = PublicCollection + $"/{parent:D}/tasks/sets" + (operation == "status" ? $"/{request:D}" : "");
            using var document = await _transport.SendAsync(operation == "start" ? HttpMethod.Post : HttpMethod.Get, path,
                operation == "start" ? new { requestId = request, parentOriginalSha256, maximumTasks } : null,
                operation == "start" ? 202 : 200).ConfigureAwait(false);
            var raw = document.RootElement;
            if (raw.ValueKind == JsonValueKind.Null) Require(operation == "status");
            else
            {
                PublicTaskSet value;
                try
                {
                    value = raw.Deserialize<PublicTaskSet>(HostedReaderTransport.Json) ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
                    value.Validate(_options.Organization, parent, value.CreatedBy, request);
                }
                catch (Exception error) when (error is BrowserOperationException or ArgumentException or JsonException or InvalidOperationException)
                { throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }
                Require(raw.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String && scope.GetString() == "CapturedPageServiceLinks"
                    && raw.TryGetProperty("taskResultsRequireOriginalRead", out var originals) && originals.ValueKind == JsonValueKind.True
                    && raw.TryGetProperty("isAssessment", out var assessment) && assessment.ValueKind == JsonValueKind.False);
                if (operation == "start") Require(value.ParentOriginalSha256 == parentOriginalSha256 && value.MaximumTasks == maximumTasks);
            }
            var reply = new ToolReply("public-task-set-" + operation, null, PublicTaskJob.Profile, raw.Clone());
            return Fits(reply) ? reply : new("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_TASK_SET_PAGE_TOO_LARGE");
        }, token, operation == "start" ? request : null);
    }
}

[McpServerToolType]
public sealed class HostedPublicTaskSetTools(HostedScanClient client)
{
    [McpServerTool(Name = "website_task_set_start", ReadOnly = false, Destructive = false, OpenWorld = true),
        Description("Atomically queue applicable Pricing, Catalog, Contact and Help service-link tasks from the exact saved review, within maximumTasks=1..4. Each child has independent keyboard/mouse experiments and verified originals. Retain the same requestId, parentOriginalSha256 and budget to reconcile a lost response; never automatically retry or poll. Empty, unsupported, unobserved and budget-excluded tasks remain unassessed. Metadata is not a barrier, conformance or legal assessment. No sign-in, form entry or transactions. Public MCP/plugin publication remains deferred.")]
    public async Task<CallToolResult> Start(string scanId, string requestId, string parentOriginalSha256,
        int maximumTasks = 4, CancellationToken cancellationToken = default) => ReaderTools.Result(
            await client.PublicTaskSetAsync("start", scanId, requestId, parentOriginalSha256, maximumTasks, cancellationToken));

    [McpServerTool(Name = "website_task_set_status", ReadOnly = true, OpenWorld = false),
        Description("Read this original caller's exact task set by saved scanId and requestId. Null means absent. No new admission, input retry or automatic polling. Metadata omits evidence assessments: read each member's exact website_task_result, website_task_reader, trace and original recording separately.")]
    public async Task<CallToolResult> Status(string scanId, string requestId, CancellationToken cancellationToken = default) =>
        ReaderTools.Result(await client.PublicTaskSetAsync("status", scanId, requestId, token: cancellationToken));
}
