using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed record PortalCreateSearchTaskRequest(Guid RequestId, string SourceId);
internal sealed record PortalSearchFile(int Id, string Path, long Bytes, string Sha256, string MediaType);
internal sealed record PortalSearchTrial(SyntheticSearchTaskTrial Trial, PortalSearchFile Video);
internal sealed record PortalSearchResult(SyntheticSearchTaskStatus Status, string IndexSha256,
    PortalSearchTrial[] KeyboardTrials, string PointerRunId, PortalSearchFile PointerVideo,
    string Observation, string ProducerSha256, string ProducerVersion, string Runtime, string Platform,
    string Viewport, int OriginalFiles, string Scope, string WebsiteEvidenceAssessment,
    string VideoAlignment, string ExternalAssistiveTechnologyComparison, bool LegalCaseDetermined);
internal sealed record PortalSearchFilePage(SyntheticSearchTaskStatus Status, string IndexSha256,
    int Offset, int Total, PortalSearchFile[] Files, int? NextOffset);
internal sealed record PortalSearchOriginalPage(SyntheticSearchTaskStatus Status, string IndexSha256,
    PortalSearchFile File, int Offset, byte[] Data, string PageSha256, int? NextOffset);
internal sealed record PortalSearchPublicationAttempt(int Number, Guid ProducerId, Guid PublisherId, Guid ExecutionId,
    string IndexSha256, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string State, string? ErrorCode,
    bool WebsiteInputRetried);
internal sealed record PortalSearchDiagnostics(string Schema, Guid JobId, PortalSearchPublicationAttempt[] PublicationAttempts);

internal interface IPortalSearchTaskProject
{
    Task<PortalSearchHistory> ReadSearchHistoryAsync(PortalIdentity actor, Guid organization, string sourceId, Guid? beforeJob, CancellationToken token) =>
        throw new PortalFault(503, "CONTROL_SEARCH_HISTORY_UNAVAILABLE");
    Task<SyntheticSearchTaskStatus?> ReadSearchTaskAsync(PortalIdentity actor, Guid organization, Guid? job, Guid? request, CancellationToken token);
    Task<SyntheticSearchTaskStatus> CreateSearchTaskAsync(PortalIdentity actor, Guid organization, PortalCreateSearchTaskRequest request, CancellationToken token);
    Task<SyntheticSearchTaskStatus?> CancelSearchTaskAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token);
    Task<PortalSearchDiagnostics?> ReadSearchDiagnosticsAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token);
}

internal sealed partial class SupabasePortalClient : IPortalSearchTaskProject
{
    public Task<SyntheticSearchTaskStatus?> ReadSearchTaskAsync(PortalIdentity actor, Guid organization, Guid? job, Guid? request, CancellationToken token) =>
        SearchTaskRpcAsync<SyntheticSearchTaskStatus>(actor, "read_search_task", new { p_organization_id = organization, p_job_id = job, p_request_id = request }, token);
    public async Task<SyntheticSearchTaskStatus> CreateSearchTaskAsync(PortalIdentity actor, Guid organization, PortalCreateSearchTaskRequest value, CancellationToken token) =>
        await SearchTaskRpcAsync<SyntheticSearchTaskStatus>(actor, "create_search_task", new
        { p_organization_id = organization, p_request_id = value.RequestId, p_source_id = value.SourceId }, token).ConfigureAwait(false)
        ?? throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID");
    public Task<SyntheticSearchTaskStatus?> CancelSearchTaskAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token) =>
        SearchTaskRpcAsync<SyntheticSearchTaskStatus>(actor, "cancel_search_task", new { p_organization_id = organization, p_job_id = job }, token);
    public Task<PortalSearchDiagnostics?> ReadSearchDiagnosticsAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token) =>
        SearchTaskRpcAsync<PortalSearchDiagnostics>(actor, "read_search_task_diagnostics", new { p_organization_id = organization, p_job_id = job }, token);

    private async Task<T?> SearchTaskRpcAsync<T>(PortalIdentity actor, string function, object value, CancellationToken token) where T : class
    {
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/" + function, actor.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        JsonDocument json;
        try { json = await SendAsync(request, false, token, publicAdmission: true).ConfigureAwait(false); }
        catch (PortalFault fault) when (fault.Code == "CONTROL_PUBLIC_WEBSITE_QUEUE_FULL")
        { throw new PortalFault(429, "CONTROL_SEARCH_TASK_QUEUE_FULL"); }
        using (json)
        {
            if (json.RootElement.ValueKind == JsonValueKind.Null) return null;
            try
            {
                var result = json.RootElement.Deserialize<T>(ReaderWorkerWire.Json);
                // Getter-only scope fields must match the actual upstream bytes, not just a reconstructed object.
                if (result is null || !JsonElement.DeepEquals(json.RootElement, JsonSerializer.SerializeToElement(result, ReaderWorkerWire.Json)))
                    throw new JsonException();
                if (result is SyntheticSearchTaskStatus status) status.Validate(status.Job.OrganizationId);
                return result;
            }
            catch (JsonException) { throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID"); }
            catch (BrowserOperationException) { throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID"); }
        }
    }
}

/// <summary>Independent demonstrations, with current-caller checks before and after private original retrieval.</summary>
internal sealed partial class PortalSearchTasks(IPortalSearchTaskProject? client, SyntheticSearchTaskArchive? archive, Func<Task<bool>>? ready)
{
    internal const int FilePageSize = 24;
    internal const int OriginalPageSize = 64 * 1024;
    private IPortalSearchTaskProject Client => client ?? throw new PortalFault(503, "CONTROL_SEARCH_TASK_UNAVAILABLE");

    internal async Task<SyntheticSearchTaskStatus> StartAsync(PortalIdentity actor, Guid organization,
        PortalCreateSearchTaskRequest request, CancellationToken token)
    {
        if (organization == Guid.Empty || request.RequestId == Guid.Empty || request.SourceId is not ("search-working" or "search-keyboard-blocked"))
            throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var existing = await Client.ReadSearchTaskAsync(actor, organization, null, request.RequestId, token).ConfigureAwait(false);
        if (existing is not null)
        {
            Validate(existing, organization);
            if (existing.RequestId != request.RequestId || existing.CreatedBy != actor.UserId || existing.Job.Source.Id != request.SourceId)
                throw new PortalFault(409, "CONTROL_REQUEST_CONFLICT");
            return existing;
        }
        if (ready is null || !await ready().ConfigureAwait(false)) throw new PortalFault(503, "CONTROL_SEARCH_TASK_RUNTIME_UNAVAILABLE");
        var status = await Client.CreateSearchTaskAsync(actor, organization, request, token).ConfigureAwait(false);
        Validate(status, organization);
        if (status.RequestId != request.RequestId || status.CreatedBy != actor.UserId || status.Job.Source.Id != request.SourceId)
            throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID");
        var current = await StatusAsync(actor, organization, status.Job.JobId, token).ConfigureAwait(false)
            ?? throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID");
        if (current.Job != status.Job || current.RequestId != status.RequestId || current.CreatedBy != status.CreatedBy)
            throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID");
        return current;
    }

    internal async Task<SyntheticSearchTaskStatus?> StatusAsync(PortalIdentity actor, Guid organization, Guid? job, CancellationToken token)
    {
        var status = await Client.ReadSearchTaskAsync(actor, organization, job, null, token).ConfigureAwait(false);
        if (status is not null) Validate(status, organization, job);
        return status;
    }
    internal async Task<SyntheticSearchTaskStatus> CancelAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token)
    {
        var status = await Client.CancelSearchTaskAsync(actor, organization, job, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_SEARCH_TASK_NOT_FOUND");
        Validate(status, organization, job); return status;
    }

    private static void Validate(SyntheticSearchTaskStatus status, Guid organization, Guid? job = null)
    {
        try { status.Validate(organization, job); }
        catch (BrowserOperationException) { throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID"); }
    }
    private async Task RecheckAsync(PortalIdentity actor, SyntheticSearchTaskStatus expected, CancellationToken token)
    {
        var current = await StatusAsync(actor, expected.Job.OrganizationId, expected.Job.JobId, token).ConfigureAwait(false);
        if (current is null || current.Job != expected.Job || current.RequestId != expected.RequestId || current.CreatedBy != expected.CreatedBy
            || current.State != expected.State || current.ProducerId != expected.ProducerId || current.Publication != expected.Publication)
            throw new PortalFault(409, "CONTROL_SEARCH_TASK_CHANGED");
    }
    private async Task<(SyntheticSearchTaskStatus Status, SearchTaskStoredOriginals Originals)> OriginalsAsync(
        PortalIdentity actor, Guid organization, Guid job, string? digest, CancellationToken token)
    {
        var status = await StatusAsync(actor, organization, job, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_SEARCH_TASK_NOT_FOUND");
        if (status.State != "completed" || status.Publication is null || status.ProducerId is null)
            throw new PortalFault(409, "CONTROL_SEARCH_TASK_NOT_READY");
        if (digest is not null && digest != status.Publication.IndexSha256) throw new PortalFault(409, "CONTROL_SEARCH_TASK_CHANGED");
        SearchTaskStoredOriginals? originals = null; Exception? failure = null;
        try
        {
            originals = await (archive ?? throw new IOException()).ReadAsync(status.Job, status.ProducerId.Value,
                status.Publication, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) { failure = error; }
        await RecheckAsync(actor, status, token).ConfigureAwait(false);
        if (failure is not null || originals is null) throw new PortalFault(503, "CONTROL_SEARCH_TASK_ORIGINAL_UNAVAILABLE");
        return (status, originals);
    }

    private static PortalSearchFile[] Files(SearchTaskStoredOriginals originals) => originals.Files.OrderBy(item => item.Key, StringComparer.Ordinal)
        .Select((item, number) => new PortalSearchFile(number, item.Key, item.Value.Length, TraversalArchive.Hash(item.Value),
            Path.GetExtension(item.Key) switch { ".webm" => "video/webm", ".json" => "application/json", ".jpg" => "image/jpeg", ".html" => "text/html", _ => "text/plain" })).ToArray();
    internal async Task<PortalSearchResult> ResultAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token)
    {
        var (status, originals) = await OriginalsAsync(actor, organization, job, null, token).ConfigureAwait(false);
        var files = Files(originals); var manifest = originals.Manifest;
        return new(status, status.Publication!.IndexSha256,
            manifest.KeyboardTrials.Select(trial => new PortalSearchTrial(trial, files.Single(file => file.Path == $"route-{trial.Trial}-media/original.webm"))).ToArray(),
            manifest.PointerRunId, files.Single(file => file.Path == "pointer-media/original.webm"), manifest.Observation,
            manifest.ProducerSha256, manifest.ProducerVersion, manifest.Runtime, manifest.Platform, manifest.Viewport,
            files.Length, manifest.Scope, manifest.WebsiteEvidenceAssessment, manifest.VideoAlignment, manifest.ExternalAssistiveTechnologyComparison, false);
    }
    internal async Task<PortalSearchFilePage> FilesAsync(PortalIdentity actor, Guid organization, Guid job, int offset, string digest, CancellationToken token)
    {
        if (!TraversalArchive.IsHash(digest) || offset < 0 || offset % FilePageSize != 0) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var (status, originals) = await OriginalsAsync(actor, organization, job, digest, token).ConfigureAwait(false);
        var files = Files(originals);
        if (offset >= files.Length) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        return new(status, digest, offset, files.Length, files.Skip(offset).Take(FilePageSize).ToArray(),
            offset + FilePageSize < files.Length ? offset + FilePageSize : null);
    }
    internal async Task<PortalSearchOriginalPage> OriginalAsync(PortalIdentity actor, Guid organization, Guid job, int fileId,
        int offset, string digest, CancellationToken token)
    {
        if (!TraversalArchive.IsHash(digest) || fileId < 0 || offset < 0 || offset > 26 * 1024 * 1024) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var (status, originals) = await OriginalsAsync(actor, organization, job, digest, token).ConfigureAwait(false);
        var files = Files(originals);
        if (fileId >= files.Length) throw new PortalFault(404, "CONTROL_SEARCH_TASK_FILE_NOT_FOUND");
        var file = files[fileId]; var original = originals.Files[file.Path];
        if (offset > original.Length || offset % OriginalPageSize != 0 && offset != original.Length) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var bytes = original.AsSpan(offset, Math.Min(OriginalPageSize, original.Length - offset)).ToArray();
        return new(status, digest, file, offset, bytes, TraversalArchive.Hash(bytes),
            offset + bytes.Length < original.Length ? offset + bytes.Length : null);
    }
    internal async Task<PortalSearchDiagnostics> DiagnosticsAsync(PortalIdentity actor, Guid organization, Guid job, CancellationToken token)
    {
        var status = await StatusAsync(actor, organization, job, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_SEARCH_TASK_NOT_FOUND");
        var diagnostics = await Client.ReadSearchDiagnosticsAsync(actor, organization, job, token).ConfigureAwait(false)
            ?? throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID");
        await RecheckAsync(actor, status, token).ConfigureAwait(false);
        if (diagnostics.Schema != "hsr-search-task-diagnostics-v1" || diagnostics.JobId != job || diagnostics.PublicationAttempts is not { Length: <= 3 })
            throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID");
        for (var number = 0; number < diagnostics.PublicationAttempts.Length; number++)
        {
            var attempt = diagnostics.PublicationAttempts[number];
            if (attempt is null || attempt.Number != number + 1 || attempt.ProducerId == Guid.Empty || attempt.PublisherId == Guid.Empty
                || attempt.ExecutionId == Guid.Empty || !TraversalArchive.IsHash(attempt.IndexSha256) || attempt.StartedAt == default
                || attempt.WebsiteInputRetried || attempt.State is not ("Publishing" or "Published" or "Blocked" or "Cancelled")
                || attempt.State == "Publishing" && (attempt.FinishedAt is not null || attempt.ErrorCode is not null)
                || attempt.State != "Publishing" && (attempt.FinishedAt is null || attempt.FinishedAt < attempt.StartedAt)
                || attempt.State == "Published" && attempt.ErrorCode is not null
                || attempt.State is "Blocked" or "Cancelled" && attempt.ErrorCode is null
                || attempt.ErrorCode is not (null or "SEARCH_TASK_PUBLICATION_UNCONFIRMED" or "SEARCH_TASK_PUBLICATION_INVALID"
                    or "SEARCH_TASK_AUTHORITY_LOST" or "SEARCH_TASK_CANCELLED" or "SEARCH_TASK_PUBLICATION_LEASE_EXPIRED")
                || attempt.ProducerId != status.ProducerId
                || status.Publication is { } published && (attempt.ExecutionId != published.ExecutionId || attempt.IndexSha256 != published.IndexSha256))
                throw new PortalFault(503, "CONTROL_SEARCH_TASK_RESPONSE_INVALID");
        }
        return diagnostics;
    }
}

internal static class PortalSearchTaskEndpoints
{
    internal static void Map(WebApplication app)
    {
        const string root = "/api/workspaces/{organization:guid}/search-demonstrations";
        app.MapGet(root + "/history/{sourceId}", async (HttpContext context, PortalSearchTasks service, Guid organization, string sourceId, CancellationToken token) =>
            TypedResults.Json(await service.HistoryAsync(HostedControlServer.Actor(context), organization, sourceId, null, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadSearchTaskHistory");
        app.MapGet(root + "/history/{sourceId}/{beforeJob:guid}", async (HttpContext context, PortalSearchTasks service, Guid organization, string sourceId, Guid beforeJob, CancellationToken token) =>
            TypedResults.Json(await service.HistoryAsync(HostedControlServer.Actor(context), organization, sourceId, beforeJob, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadSearchTaskHistoryBefore");
        app.MapPost(root, async (HttpContext context, PortalSearchTasks service, Guid organization, CancellationToken token) =>
        {
            var value = await HostedControlServer.BodyAsync<PortalCreateSearchTaskRequest>(context, token).ConfigureAwait(false);
            var result = await service.StartAsync(HostedControlServer.Actor(context), organization, value, token).ConfigureAwait(false);
            return TypedResults.Accepted($"/api/workspaces/{organization:D}/search-demonstrations/{result.Job.JobId:D}", result);
        }).WithName("AdmitSearchTask");
        app.MapGet(root, async (HttpContext context, PortalSearchTasks service, Guid organization, CancellationToken token) =>
            TypedResults.Json(JsonSerializer.SerializeToElement(await service.StatusAsync(HostedControlServer.Actor(context), organization, null, token).ConfigureAwait(false), PortalJson.Options), PortalJson.Options)).WithName("ReadLatestSearchTask");
        app.MapGet(root + "/{job:guid}", async (HttpContext context, PortalSearchTasks service, Guid organization, Guid job, CancellationToken token) =>
            TypedResults.Json(JsonSerializer.SerializeToElement(await service.StatusAsync(HostedControlServer.Actor(context), organization, job, token).ConfigureAwait(false), PortalJson.Options), PortalJson.Options)).WithName("ReadSearchTaskStatus");
        app.MapPost(root + "/{job:guid}/cancel", async (HttpContext context, PortalSearchTasks service, Guid organization, Guid job, CancellationToken token) =>
            TypedResults.Json(await service.CancelAsync(HostedControlServer.Actor(context), organization, job, token).ConfigureAwait(false), PortalJson.Options)).WithName("CancelSearchTask");
        app.MapGet(root + "/{job:guid}/result", async (HttpContext context, PortalSearchTasks service, Guid organization, Guid job, CancellationToken token) =>
            TypedResults.Json(await service.ResultAsync(HostedControlServer.Actor(context), organization, job, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadSearchTaskResult");
        app.MapGet(root + "/{job:guid}/files/{offset:int}/{digest}", async (HttpContext context, PortalSearchTasks service, Guid organization, Guid job, int offset, string digest, CancellationToken token) =>
            TypedResults.Json(await service.FilesAsync(HostedControlServer.Actor(context), organization, job, offset, digest, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadSearchTaskFiles");
        app.MapGet(root + "/{job:guid}/originals/{fileId:int}/{offset:int}/{digest}", async (HttpContext context, PortalSearchTasks service, Guid organization, Guid job, int fileId, int offset, string digest, CancellationToken token) =>
            TypedResults.Json(await service.OriginalAsync(HostedControlServer.Actor(context), organization, job, fileId, offset, digest, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadSearchTaskOriginal");
        app.MapGet(root + "/{job:guid}/diagnostics", async (HttpContext context, PortalSearchTasks service, Guid organization, Guid job, CancellationToken token) =>
            TypedResults.Json(await service.DiagnosticsAsync(HostedControlServer.Actor(context), organization, job, token).ConfigureAwait(false), PortalJson.Options)).WithName("ReadSearchTaskDiagnostics");
    }
}
