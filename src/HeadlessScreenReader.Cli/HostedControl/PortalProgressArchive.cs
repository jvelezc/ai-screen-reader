using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>A graph and checkpoint manifest for one authorized retained prefix; missing prefixes remain explicit.</summary>
internal sealed record PortalProgressGraphResponse(PortalProgressResponse Status, RetainedProgressGraph? Graph);
/// <summary>Read one canonical checkpoint at a fixed 16 KiB offset using the retained prefix digest, never caller-selected producer identity.</summary>
internal sealed record PortalCheckpointRequest(string PrefixSha256, int Sequence, int Offset = 0);

internal static class PortalProgressArchiveJson
{
    internal static readonly JsonSerializerOptions Options = new(PortalJson.Options)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
}

internal sealed partial class PortalProgressService
{
    public Task<PortalProgressGraphResponse> GraphAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) =>
        ReadRetainedAsync(actor, organization, scan, (status, snapshot) =>
        {
            var graph = snapshot is null ? null : RetainedProgressProjection.Graph(snapshot, status.Progress!.Job, status.WorkerId!.Value);
            var result = new PortalProgressGraphResponse(status, graph);
            if (JsonSerializer.SerializeToUtf8Bytes(result, PortalProgressArchiveJson.Options).Length > 65536)
                throw new PortalFault(409, "CONTROL_PROGRESS_GRAPH_TOO_LARGE");
            return result;
        }, token);

    public Task<RetainedCheckpointPage> CheckpointAsync(PortalIdentity actor, Guid organization, Guid scan,
        PortalCheckpointRequest request, CancellationToken token)
    {
        if (request.PrefixSha256 is not { Length: 64 } || !request.PrefixSha256.All(char.IsAsciiHexDigitLower)
            || request.Sequence is < 1 or > 9 || request.Offset is < 0 or > 1048576)
            throw new PortalFault(400, "CONTROL_PROGRESS_PAGE_INVALID");
        return ReadRetainedAsync(actor, organization, scan, (status, snapshot) =>
        {
            if (snapshot is null) throw new PortalFault(404, "CONTROL_PROGRESS_NOT_RETAINED");
            if (request.PrefixSha256 != status.Progress!.PrefixSha256) throw new PortalFault(409, "CONTROL_PROGRESS_PREFIX_CHANGED");
            if (request.Sequence > snapshot.Originals.Length) throw new PortalFault(400, "CONTROL_PROGRESS_PAGE_INVALID");
            var length = FixtureProgressProtocol.Encode(snapshot.Originals[request.Sequence - 1].Checkpoint).Length;
            if (request.Offset > length || request.Offset != length && request.Offset % RetainedCheckpointPage.PageBytes != 0)
                throw new PortalFault(400, "CONTROL_PROGRESS_PAGE_INVALID");
            return RetainedProgressProjection.Checkpoint(snapshot, status.Progress.Job, status.WorkerId!.Value,
                request.PrefixSha256, request.Sequence, request.Offset);
        }, token);
    }

    private async Task<T> ReadRetainedAsync<T>(PortalIdentity actor, Guid organization, Guid scan,
        Func<PortalProgressResponse, CommandProgressSnapshot?, T> projectResult, CancellationToken token)
    {
        if (organization == Guid.Empty || scan == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        token.ThrowIfCancellationRequested();
        var before = await ScopeAsync(actor, organization, scan, token).ConfigureAwait(false);
        T? result = default; PortalFault? failure = null;
        try
        {
            CommandProgressSnapshot? snapshot = null;
            if (before.Worker is { } worker)
                snapshot = await (snapshotReader ?? throw new PortalFault(503, "CONTROL_PROGRESS_UNAVAILABLE"))
                    .ReadSnapshotAsync(Descriptor(before.Job), worker, token).ConfigureAwait(false);
            var progress = snapshot is null ? null : CommandProgressProjection.Project(snapshot.Summary, Descriptor(before.Job), before.Worker!.Value);
            var status = new PortalProgressResponse(before.Scan, before.Job.JobId, before.Job.Generation, before.Worker,
                before.Job.TraversalRoute == 0 ? "NotApplicable" : before.Worker is null ? "NotStarted" : progress is null ? "NotRetained" : "Available", progress);
            result = projectResult(status, snapshot);
        }
        catch (PortalFault error) { failure = error; }
        catch (BrowserOperationException) { failure = new(409, "CONTROL_PROGRESS_INVALID"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure = new(503, "CONTROL_PROGRESS_UNAVAILABLE"); }
        var after = await ScopeAsync(actor, organization, scan, token).ConfigureAwait(false);
        if (Descriptor(before.Job) != Descriptor(after.Job) || before.Worker != after.Worker)
            throw new PortalFault(409, "CONTROL_PROGRESS_ATTEMPT_CHANGED");
        token.ThrowIfCancellationRequested();
        if (failure is not null) throw failure;
        return result!;
    }
}

internal static class PortalProgressArchiveEndpoints
{
    public static void MapProgressArchive(this WebApplication app)
    {
        app.MapGet("/api/workspaces/{organization:guid}/scans/{scan:guid}/progress/graph", async (HttpContext context,
            PortalProgressService progress, Guid organization, Guid scan, CancellationToken token) =>
            TypedResults.Json(await progress.GraphAsync(HostedControlServer.Actor(context), organization, scan, token).ConfigureAwait(false), PortalProgressArchiveJson.Options))
            .WithName("ReadRetainedProgressGraph").WithSummary("Read the original attempt's retained diagnostic graph and checkpoint manifest")
            .WithDescription("Unresolved intent edges remain explicit. Canonical checkpoint export hashes differ from source journal seals. No native ownership, replay, media or overall goal authority.")
            .Produces<PortalProgressGraphResponse>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        app.MapPost("/api/workspaces/{organization:guid}/scans/{scan:guid}/progress/checkpoint", async (HttpContext context,
            PortalProgressService progress, Guid organization, Guid scan, CancellationToken token) =>
        {
            var request = await HostedControlServer.BodyAsync<PortalCheckpointRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Json(await progress.CheckpointAsync(HostedControlServer.Actor(context), organization, scan, request, token).ConfigureAwait(false), PortalProgressArchiveJson.Options);
        }).WithName("ReadRetainedCheckpoint").WithSummary("Read a bounded canonical full-checkpoint byte page")
            .WithDescription("Requires the graph's exact prefix digest. 16 KiB aligned offsets or exact EOF; compare page and whole export hashes. Original caller and actual producer rechecked on every page; changed prefix requires a fresh graph read, never input replay.")
            .Accepts<PortalCheckpointRequest>("application/json").Produces<RetainedCheckpointPage>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
    }
}
