using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static class PortalPublicTaskFrames
{
    internal static async Task<JsonHttpResult<JsonElement>> ReadAsync(HttpContext context, PortalPublicTasks service,
        PortalOptions options, Guid organization, Guid scan, Guid job, CancellationToken token)
    {
        var actor = HostedControlServer.Actor(context);
        var frame = await ReadFrameAsync(service, new(options.EvidenceRoot), actor, organization, scan, job, token).ConfigureAwait(false);
        return TypedResults.Json(JsonSerializer.SerializeToElement(frame, PortalJson.Options), PortalJson.Options);
    }

    internal static async Task<PublicTaskLiveFrame?> ReadFrameAsync(PortalPublicTasks service, PublicTaskLiveFrameStore store,
        PortalIdentity actor, Guid organization, Guid scan, Guid job, CancellationToken token)
    {
        var before = await ReadStatusAsync().ConfigureAwait(false);
        var attempt = ActiveAttempt(before);
        if (attempt == 0) return null;
        var frame = await store.ReadAsync(before.Job, attempt, DateTimeOffset.UtcNow, token).ConfigureAwait(false);
        if (frame is null || frame.CapturedAt < before.CreatedAt) return null;

        // Recheck current provider/session authority and the active attempt after opening the local frame.
        var after = await ReadStatusAsync().ConfigureAwait(false);
        if (after.Job != before.Job || ActiveAttempt(after) != attempt) return null;
        var current = await store.ReadAsync(after.Job, attempt, DateTimeOffset.UtcNow, token).ConfigureAwait(false);
        return current is not null && current.CapturedAt >= after.CreatedAt && current.ExecutionId == frame.ExecutionId ? current : null;

        async Task<PublicTaskStatus> ReadStatusAsync()
        {
            var status = await service.StatusAsync(actor, organization, scan, job, token).ConfigureAwait(false);
            if (status is null || status.CreatedBy != actor.UserId) throw new PortalFault(404, "CONTROL_PUBLIC_TASK_NOT_FOUND");
            return status;
        }
    }

    private static int ActiveAttempt(PublicTaskStatus status) => status.State != "leased" ? 0 : status.Phase switch
    {
        "KeyboardAttempt1" => 1,
        "KeyboardAttempt2" => 2,
        "PointerComparison" => 3,
        _ => 0
    };
}
