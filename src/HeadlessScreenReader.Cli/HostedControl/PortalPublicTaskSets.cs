using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>One explicit atomic set, pinned to a saved page and a one-to-four service-link task budget.</summary>
internal sealed record PortalCreatePublicTaskSetRequest(Guid RequestId, string ParentOriginalSha256, int MaximumTasks);

internal sealed partial class SupabasePortalClient
{
    public Task<PublicTaskSet?> ReadPublicTaskSetAsync(PortalIdentity actor, Guid organization, Guid parent,
        Guid request, CancellationToken token) => PublicTaskSetRpcAsync(actor, "read_public_task_set",
            new { p_organization_id = organization, p_parent_scan_id = parent, p_request_id = request }, token);

    public async Task<PublicTaskSet> CreatePublicTaskSetAsync(PortalIdentity actor, Guid organization, Guid parent,
        PortalCreatePublicTaskSetRequest request, ImmutableArray<string> kinds, CancellationToken token) =>
        await PublicTaskSetRpcAsync(actor, "create_public_task_set", new
        {
            p_organization_id = organization,
            p_parent_scan_id = parent,
            p_request_id = request.RequestId,
            p_parent_original_sha256 = request.ParentOriginalSha256,
            p_maximum_tasks = request.MaximumTasks,
            p_goal_kinds = kinds
        }, token).ConfigureAwait(false) ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID");

    private async Task<PublicTaskSet?> PublicTaskSetRpcAsync(PortalIdentity actor, string function, object value, CancellationToken token)
    {
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/" + function, actor.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token, publicAdmission: true).ConfigureAwait(false);
        var root = json.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return null;
        try
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Scope", out var scope)
                || scope.GetString() != "CapturedPageServiceLinks"
                || !root.TryGetProperty("IsAssessment", out var assessment) || assessment.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("TaskResultsRequireOriginalRead", out var originals) || originals.ValueKind != JsonValueKind.True)
                throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID");
            return root.Deserialize<PublicTaskSet>(ReaderWorkerWire.Json)
                ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID");
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException)
        { throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID"); }
    }
}

internal sealed partial class PortalPublicTasks
{
    internal async Task<PublicTaskSet?> SetStatusAsync(PortalIdentity actor, Guid organization, Guid parent,
        Guid request, CancellationToken token)
    {
        if (request == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var result = await Client.ReadPublicTaskSetAsync(actor, organization, parent, request, token).ConfigureAwait(false);
        return result is null ? null : CheckSet(result, actor, organization, parent, request);
    }

    internal async Task<PublicTaskSet> StartSetAsync(PortalIdentity actor, Guid organization, Guid parent,
        PortalCreatePublicTaskSetRequest request, Func<CancellationToken, Task<PortalWebsiteTaskCoverageResponse>> inspect,
        CancellationToken token)
    {
        if (request.RequestId == Guid.Empty || !PublicTaskRules.Digest(request.ParentOriginalSha256)
            || request.MaximumTasks is < 1 or > PublicTaskSet.MaximumMembers)
            throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var existing = await SetStatusAsync(actor, organization, parent, request.RequestId, token).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ParentOriginalSha256 != request.ParentOriginalSha256 || existing.MaximumTasks != request.MaximumTasks)
                throw new PortalFault(409, "CONTROL_REQUEST_CONFLICT");
            return existing;
        }
        if (ready is null || !await ready().ConfigureAwait(false)) throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RUNTIME_UNAVAILABLE");
        var observed = await inspect(token).ConfigureAwait(false);
        if (observed is null || observed.Scan is null) throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_SOURCE_INVALID");
        observed.Scan.Validate(organization, parent);
        if (observed.Scan.State != "completed" || observed.Scan.OriginalSha256 != request.ParentOriginalSha256
            || observed.OriginalSha256 != request.ParentOriginalSha256 || observed.CaptureIndex != 1
            || observed.Captures != 2 || observed.ReceivedAt == default)
            throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_SOURCE_INVALID");
        ImmutableArray<string> kinds;
        try { kinds = WebsiteTaskSetPlanning.Select(observed.Coverage, request.MaximumTasks); }
        catch (ArgumentException) { throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_SOURCE_INVALID"); }
        token.ThrowIfCancellationRequested();
        var result = CheckSet(await Client.CreatePublicTaskSetAsync(actor, organization, parent, request, kinds, token).ConfigureAwait(false),
            actor, organization, parent, request.RequestId);
        if (result.ParentOriginalSha256 != request.ParentOriginalSha256 || result.MaximumTasks != request.MaximumTasks
            || result.Source != observed.Scan.Job.Source || !result.GoalKinds.SequenceEqual(kinds, StringComparer.Ordinal))
            throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID");
        var current = await SetStatusAsync(actor, organization, parent, request.RequestId, token).ConfigureAwait(false)
            ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID");
        if (current.SetId != result.SetId || current.CreatedAt != result.CreatedAt || current.Source != result.Source
            || current.ParentOriginalSha256 != result.ParentOriginalSha256 || current.MaximumTasks != result.MaximumTasks
            || !current.GoalKinds.SequenceEqual(result.GoalKinds, StringComparer.Ordinal)
            || !current.Tasks.Select(task => task.Job).SequenceEqual(result.Tasks.Select(task => task.Job)))
            throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID");
        return current;
    }

    private static PublicTaskSet CheckSet(PublicTaskSet value, PortalIdentity actor, Guid organization, Guid parent, Guid request)
    {
        if (value is null) throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID");
        try { value.Validate(organization, parent, actor.UserId, request); }
        catch (Exception failure) when (failure is BrowserOperationException or ArgumentException)
        { throw new PortalFault(503, "CONTROL_PUBLIC_TASK_SET_RESPONSE_INVALID"); }
        if (JsonSerializer.SerializeToUtf8Bytes(value, PortalJson.Options).Length > PortalPublicWebsiteContract.MaximumReplyBytes)
            throw new PortalFault(409, "CONTROL_PUBLIC_TASK_SET_PAGE_TOO_LARGE");
        return value;
    }
}

internal static class PortalPublicTaskSetEndpoints
{
    internal static void Map(WebApplication app)
    {
        const string root = "/api/workspaces/{organization:guid}/website-scans/{scan:guid}/tasks/sets";
        app.MapPost(root, async (HttpContext context, PortalPublicTasks service, IPortalPublicWebsites websites,
            Guid organization, Guid scan, CancellationToken token) =>
        {
            var request = await HostedControlServer.BodyAsync<PortalCreatePublicTaskSetRequest>(context, token).ConfigureAwait(false);
            var actor = HostedControlServer.Actor(context);
            var result = await service.StartSetAsync(actor, organization, scan, request,
                current => websites.CoverageAsync(actor, organization, scan, request.ParentOriginalSha256, current), token).ConfigureAwait(false);
            context.Response.Headers.Location = $"/api/workspaces/{organization:D}/website-scans/{scan:D}/tasks/sets/{result.RequestId:D}";
            return TypedResults.Json(result, PortalJson.Options, statusCode: StatusCodes.Status202Accepted);
        }).WithName("AdmitPublicTaskSet").WithSummary("Atomically queue applicable supported service-link tasks within a declared budget")
            .WithDescription("Reconcile the same request and original digest; no automatic replay. Each child retains independent keyboard/mouse trials and verified originals. Unsupported tasks and other pages remain unassessed. Metadata is not an aggregate barrier, accessibility or legal decision.")
            .Produces<PublicTaskSet>(202).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409).ProducesProblem(429).ProducesProblem(503);
        app.MapGet(root + "/{request:guid}", async (HttpContext context, PortalPublicTasks service, Guid organization,
            Guid scan, Guid request, CancellationToken token) => TypedResults.Json(JsonSerializer.SerializeToElement(
                await service.SetStatusAsync(HostedControlServer.Actor(context), organization, scan, request, token).ConfigureAwait(false),
                PortalJson.Options), PortalJson.Options))
            .WithName("ReadPublicTaskSet").WithSummary("Read the same caller's original set and its current independent task states")
            .WithDescription("Null means no set for this request. This read creates no jobs and never retries website input. Read every task's verified result and matching originals separately.")
            .Produces<PublicTaskSet>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(409).ProducesProblem(503);
    }
}
