using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Only the pinned source review and a caller-generated idempotency key; no URLs, selectors or authored input.</summary>
internal sealed record PortalCreatePublicTaskRequest(Guid RequestId, string ParentOriginalSha256);
internal sealed record PortalTaskAttemptSummary(int Number, Guid Id, string Method, string Outcome, string? ErrorCode,
    string? FinalUrl, int Events, int AttemptedInputs, int CompletedInputs, bool TaskTargetMatched, int PolicyInterruptions,
    string Video, bool RecordingAvailable, int DecodedFrames);
internal sealed record PortalTaskResult(PublicTaskStatus Status, PublicTaskGoal? Goal, PublicTaskAssessment Assessment,
    PortalTaskAttemptSummary[] Attempts, PublicTaskFile[] Files, string Browser, string Playwright, string Platform,
    string Viewport, string Scope, string VideoAlignment, bool LegalCaseDetermined, string AssistiveTechnologyComparison,
    DateTimeOffset VerifiedAt)
{
    public PublicTaskIssueTable? Diagnostics { get; init; }
}
internal sealed record PortalTaskEventPage(PublicTaskStatus Status, string ReportSha256, int Attempt, int Offset,
    int Total, PublicTaskEvent[] Events, int? NextOffset);
internal sealed record PortalTaskGraphPage(PublicTaskStatus Status, string ReportSha256, int Attempt, int Offset,
    PublicTaskGraphNode[] Nodes, PublicTaskGraphEdge[] Edges, int TotalNodes, int TotalEdges, int? NextOffset);
internal sealed record PortalTaskOriginalPage(PublicTaskStatus Status, string ReportSha256, PublicTaskFile File, int Offset,
    byte[] Data, string PageSha256, int? NextOffset);

internal interface IPortalPublicTaskProject
{
    Task<PublicTaskStatus?> ReadPublicTaskAsync(PortalIdentity actor, Guid organization, Guid parent, Guid? job, Guid? request, CancellationToken token);
    Task<PublicTaskStatus> CreatePublicTaskAsync(PortalIdentity actor, Guid organization, Guid parent, PortalCreatePublicTaskRequest request, CancellationToken token);
    Task<PublicTaskStatus?> CancelPublicTaskAsync(PortalIdentity actor, Guid organization, Guid parent, Guid job, CancellationToken token);
}

internal sealed partial class SupabasePortalClient : IPortalPublicTaskProject
{
    public Task<PublicTaskStatus?> ReadPublicTaskAsync(PortalIdentity actor, Guid organization, Guid parent, Guid? job, Guid? request, CancellationToken token) =>
        PublicTaskRpcAsync(actor, "read_public_task", new { p_organization_id = organization, p_parent_scan_id = parent, p_job_id = job, p_request_id = request }, token);
    public async Task<PublicTaskStatus> CreatePublicTaskAsync(PortalIdentity actor, Guid organization, Guid parent, PortalCreatePublicTaskRequest value, CancellationToken token) =>
        await PublicTaskRpcAsync(actor, "create_public_task", new
        {
            p_organization_id = organization,
            p_parent_scan_id = parent,
            p_request_id = value.RequestId,
            p_parent_original_sha256 = value.ParentOriginalSha256
        }, token).ConfigureAwait(false)
            ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RESPONSE_INVALID");
    public Task<PublicTaskStatus?> CancelPublicTaskAsync(PortalIdentity actor, Guid organization, Guid parent, Guid job, CancellationToken token) =>
        PublicTaskRpcAsync(actor, "cancel_public_task", new { p_organization_id = organization, p_parent_scan_id = parent, p_job_id = job }, token);
    private async Task<PublicTaskStatus?> PublicTaskRpcAsync(PortalIdentity actor, string function, object value, CancellationToken token)
    {
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/" + function, actor.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token, publicAdmission: true).ConfigureAwait(false);
        return json.RootElement.ValueKind == JsonValueKind.Null ? null : json.RootElement.Deserialize<PublicTaskStatus>(ReaderWorkerWire.Json)
            ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RESPONSE_INVALID");
    }
}

/// <summary>Every original/result read is bracketed by a current session, membership and immutable job check.</summary>
internal sealed class PortalPublicTasks(IPortalPublicTaskProject? client, PublicTaskEvidenceStore? evidence, Func<Task<bool>>? ready)
{
    private IPortalPublicTaskProject Client => client ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_UNAVAILABLE");
    internal async Task<PublicTaskStatus> StartAsync(PortalIdentity actor, Guid organization, Guid parent, PortalCreatePublicTaskRequest request, CancellationToken token)
    {
        if (request.RequestId == Guid.Empty || !PublicTaskRules.Digest(request.ParentOriginalSha256)) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var existing = await Client.ReadPublicTaskAsync(actor, organization, parent, null, request.RequestId, token).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Validate(organization, parent);
            if (existing.Job.RequestId != request.RequestId || existing.CreatedBy != actor.UserId || existing.Job.ParentOriginalSha256 != request.ParentOriginalSha256)
                throw new PortalFault(409, "CONTROL_REQUEST_CONFLICT");
            return existing;
        }
        if (ready is null || !await ready().ConfigureAwait(false)) throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RUNTIME_UNAVAILABLE");
        var result = await Client.CreatePublicTaskAsync(actor, organization, parent, request, token).ConfigureAwait(false);
        result.Validate(organization, parent);
        if (result.Job.RequestId != request.RequestId || result.CreatedBy != actor.UserId || result.Job.ParentOriginalSha256 != request.ParentOriginalSha256)
            throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RESPONSE_INVALID");
        return await StatusAsync(actor, organization, parent, result.Job.JobId, token).ConfigureAwait(false) ?? throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RESPONSE_INVALID");
    }
    internal async Task<PublicTaskStatus?> StatusAsync(PortalIdentity actor, Guid organization, Guid parent, Guid? job, CancellationToken token)
    {
        var result = await Client.ReadPublicTaskAsync(actor, organization, parent, job, null, token).ConfigureAwait(false);
        result?.Validate(organization, parent, job); return result;
    }
    internal async Task<PublicTaskStatus> CancelAsync(PortalIdentity actor, Guid organization, Guid parent, Guid job, CancellationToken token)
    {
        var result = await Client.CancelPublicTaskAsync(actor, organization, parent, job, token).ConfigureAwait(false) ?? throw new PortalFault(404, "CONTROL_PUBLIC_TASK_NOT_FOUND");
        result.Validate(organization, parent, job); return result;
    }
    private async Task<(PublicTaskStatus Status, PublicTaskReport Report)> ReportAsync(PortalIdentity actor, Guid organization, Guid parent, Guid job, CancellationToken token)
    {
        var status = await StatusAsync(actor, organization, parent, job, token).ConfigureAwait(false) ?? throw new PortalFault(404, "CONTROL_PUBLIC_TASK_NOT_FOUND");
        if (status.State != "completed" || status.Publication is not { } publication || evidence is null) throw new PortalFault(409, "CONTROL_PUBLIC_TASK_NOT_READY");
        PublicTaskReport? report = null; Exception? failure = null;
        try
        {
            var bytes = await evidence.ReadAsync(status.Job, publication.ReportSha256, publication.ReportBytes, token).ConfigureAwait(false) ?? throw new IOException();
            using var raw = JsonDocument.Parse(bytes);
            if (raw.RootElement.GetProperty("LegalCaseDetermined").ValueKind != JsonValueKind.False
                || raw.RootElement.GetProperty("VideoAlignment").GetString() != "EventSeekingNotVerified"
                || raw.RootElement.GetProperty("Scope").GetString() != "PublicServiceNavigationOnly"
                || raw.RootElement.GetProperty("AssistiveTechnologyComparison").GetString() != "NotPerformed") throw new IOException();
            report = JsonSerializer.Deserialize<PublicTaskReport>(bytes, ReaderWorkerWire.Json) ?? throw new IOException();
            report.Validate(new(PublicTaskStart.CurrentSchema, status.Job, status.ProducerId!.Value, publication.ExecutionId));
        }
        catch (Exception error) { failure = error; }
        await RecheckAsync(actor, status, token).ConfigureAwait(false);
        if (failure is not null) throw new PortalFault(503, "CONTROL_PUBLIC_TASK_ORIGINAL_UNAVAILABLE");
        return (status, report!);
    }
    private async Task RecheckAsync(PortalIdentity actor, PublicTaskStatus expected, CancellationToken token)
    {
        var fresh = await StatusAsync(actor, expected.Job.OrganizationId, expected.Job.ParentScanId, expected.Job.JobId, token).ConfigureAwait(false);
        if (fresh is null || fresh.State != "completed" || fresh.Job != expected.Job || fresh.ProducerId != expected.ProducerId
            || fresh.ReportSha256 != expected.ReportSha256 || fresh.Publication?.ExecutionId != expected.Publication?.ExecutionId)
            throw new PortalFault(409, "CONTROL_PUBLIC_TASK_CHANGED");
    }
    internal async Task<PortalTaskResult> ResultAsync(PortalIdentity actor, Guid organization, Guid parent, Guid job, CancellationToken token)
    {
        var (status, report) = await ReportAsync(actor, organization, parent, job, token).ConfigureAwait(false);
        Exception? failure = null;
        try { foreach (var file in report.Files) _ = await evidence!.ReadAsync(report.Start.Job, file.Sha256, file.Bytes, token).ConfigureAwait(false) ?? throw new IOException(); }
        catch (Exception error) { failure = error; }
        await RecheckAsync(actor, status, token).ConfigureAwait(false);
        if (failure is not null) throw new PortalFault(503, "CONTROL_PUBLIC_TASK_ORIGINAL_UNAVAILABLE");
        var assessment = PublicTaskAssessment.Evaluate(report, true, true);
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(assessment), JsonSerializer.SerializeToElement(status.Publication!.Assessment)))
            throw new PortalFault(503, "CONTROL_PUBLIC_TASK_RESPONSE_INVALID");
        return new(status, report.Goal, assessment, report.Attempts.Select(a => new PortalTaskAttemptSummary(a.Number, a.Id, a.Method,
            a.Outcome, a.ErrorCode, a.FinalUrl, a.Events.Length, a.Events.Count(e => e.InputAttempted), a.Events.Count(e => e.InputCompleted),
            a.TaskTargetMatched, a.PolicyInterruptions, a.Video, report.Files.Any(f => f.Path == a.Video && f.MediaType == "video/webm"), a.DecodedVideoFrames)).ToArray(),
            report.Files.ToArray(), report.BrowserVersion, report.PlaywrightVersion, report.Platform, report.Viewport,
            report.Scope, report.VideoAlignment, false, report.AssistiveTechnologyComparison, DateTimeOffset.UtcNow)
        { Diagnostics = PublicTaskIssueTable.From(report) };
    }
    internal async Task<PortalTaskEventPage> EventsAsync(PortalIdentity actor, Guid organization, Guid parent, Guid job, int attempt, int offset, string digest, CancellationToken token)
    {
        if (attempt is < 1 or > 3 || offset < 0 || !PublicTaskRules.Digest(digest)) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var (status, report) = await ReportAsync(actor, organization, parent, job, token).ConfigureAwait(false);
        if (status.ReportSha256 != digest) throw new PortalFault(409, "CONTROL_PUBLIC_TASK_CHANGED");
        var run = report.Attempts.SingleOrDefault(a => a.Number == attempt) ?? throw new PortalFault(404, "CONTROL_PUBLIC_TASK_NOT_FOUND");
        if (offset > run.Events.Length) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var events = run.Events.Skip(offset).Take(24).ToArray();
        return new(status, digest, attempt, offset, run.Events.Length, events, offset + events.Length < run.Events.Length ? offset + events.Length : null);
    }
    internal async Task<PortalTaskOriginalPage> OriginalAsync(PortalIdentity actor, Guid organization, Guid parent, Guid job, string name, int offset, string digest, CancellationToken token)
    {
        if (!PublicTaskRules.FileName(name) || offset < 0 || !PublicTaskRules.Digest(digest)) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var (status, report) = await ReportAsync(actor, organization, parent, job, token).ConfigureAwait(false);
        if (status.ReportSha256 != digest) throw new PortalFault(409, "CONTROL_PUBLIC_TASK_CHANGED");
        var file = name == "report.json" ? new PublicTaskFile(name, digest, status.Publication!.ReportBytes, "application/json")
            : report.Files.SingleOrDefault(f => f.Path == name) ?? throw new PortalFault(404, "CONTROL_PUBLIC_TASK_NOT_FOUND");
        if (offset > file.Bytes || offset != file.Bytes && offset % 65536 != 0) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        byte[]? data = null; Exception? failure = null;
        try { data = await evidence!.PageAsync(status.Job, file, offset, token).ConfigureAwait(false); } catch (Exception error) { failure = error; }
        await RecheckAsync(actor, status, token).ConfigureAwait(false);
        if (failure is not null) throw new PortalFault(503, "CONTROL_PUBLIC_TASK_ORIGINAL_UNAVAILABLE");
        return new(status, digest, file, offset, data!, PublicTaskRules.Hash(data!), offset + data!.Length < file.Bytes ? offset + data.Length : null);
    }
    internal async Task<PortalTaskGraphPage> GraphAsync(PortalIdentity actor, Guid organization, Guid parent, Guid job, int attempt, int offset, string digest, CancellationToken token)
    {
        if (attempt is < 1 or > 3 || offset < 0 || !PublicTaskRules.Digest(digest)) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var (status, report) = await ReportAsync(actor, organization, parent, job, token).ConfigureAwait(false);
        if (status.ReportSha256 != digest) throw new PortalFault(409, "CONTROL_PUBLIC_TASK_CHANGED");
        var run = report.Attempts.SingleOrDefault(a => a.Number == attempt) ?? throw new PortalFault(404, "CONTROL_PUBLIC_TASK_NOT_FOUND");
        var graph = PublicTaskGraph.Build(run); var total = Math.Max(graph.Nodes.Length, graph.Edges.Length);
        if (offset > total) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        return new(status, digest, attempt, offset, graph.Nodes.Skip(offset).Take(24).ToArray(), graph.Edges.Skip(offset).Take(24).ToArray(),
            graph.Nodes.Length, graph.Edges.Length, offset + 24 < total ? offset + 24 : null);
    }
}

internal static class PortalPublicTaskEndpoints
{
    internal static void Map(WebApplication app)
    {
        const string root = "/api/workspaces/{organization:guid}/website-scans/{scan:guid}/tasks";
        app.MapPost(root, async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan, CancellationToken token) =>
        {
            var value = await HostedControlServer.BodyAsync<PortalCreatePublicTaskRequest>(context, token).ConfigureAwait(false);
            var result = await service.StartAsync(HostedControlServer.Actor(context), organization, scan, value, token).ConfigureAwait(false);
            return TypedResults.Accepted($"/api/workspaces/{organization:D}/website-scans/{scan:D}/tasks/{result.Job.JobId:D}", result);
        }).WithName("AdmitPublicTask");
        app.MapGet(root, async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan, CancellationToken token) =>
            TypedResults.Json(JsonSerializer.SerializeToElement(await service.StatusAsync(HostedControlServer.Actor(context), organization, scan, null, token).ConfigureAwait(false), PortalJson.Options), PortalJson.Options)).WithName("ReadLatestPublicTask");
        app.MapGet(root + "/{job:guid}", async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan, Guid job, CancellationToken token) =>
            TypedResults.Json(JsonSerializer.SerializeToElement(await service.StatusAsync(HostedControlServer.Actor(context), organization, scan, job, token).ConfigureAwait(false), PortalJson.Options), PortalJson.Options)).WithName("ReadPublicTaskStatus");
        app.MapPost(root + "/{job:guid}/cancel", async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan, Guid job, CancellationToken token) =>
            TypedResults.Json(await service.CancelAsync(HostedControlServer.Actor(context), organization, scan, job, token).ConfigureAwait(false), PortalJson.Options)).WithName("CancelPublicTask");
        app.MapGet(root + "/{job:guid}/result", async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan, Guid job, CancellationToken token) =>
            TypedResults.Json(await service.ResultAsync(HostedControlServer.Actor(context), organization, scan, job, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadPublicTaskResult");
        app.MapGet(root + "/{job:guid}/events/{attempt:int}/{offset:int}/{digest}", async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan,
            Guid job, int attempt, int offset, string digest, CancellationToken token) => TypedResults.Json(await service.EventsAsync(HostedControlServer.Actor(context), organization, scan, job, attempt, offset, digest, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadPublicTaskEvents");
        app.MapGet(root + "/{job:guid}/graph/{attempt:int}/{offset:int}/{digest}", async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan,
            Guid job, int attempt, int offset, string digest, CancellationToken token) => TypedResults.Json(await service.GraphAsync(HostedControlServer.Actor(context), organization, scan, job, attempt, offset, digest, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadPublicTaskGraph");
        app.MapGet(root + "/{job:guid}/originals/{name}/{offset:int}/{digest}", async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan,
            Guid job, string name, int offset, string digest, CancellationToken token) => TypedResults.Json(await service.OriginalAsync(HostedControlServer.Actor(context), organization, scan, job, name, offset, digest, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadPublicTaskOriginal");
    }
}
