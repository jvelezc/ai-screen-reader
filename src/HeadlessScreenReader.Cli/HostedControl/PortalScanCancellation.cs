using HeadlessScreenReader.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>A stop request for an admitted run; neither native cleanup nor input outcome is inferred.</summary>
internal sealed record PortalScanCancellationResponse(Guid OrganizationId, Guid ScanId, Guid JobId, long Generation,
    Guid? Producer, string Status, string InputAssessment, DateTimeOffset? RequestedAt)
{
    public string Scope => "AdmittedRunCancellation";
    public string NativeCleanup => "NotAssessed";
    public string GoalVerification => "NotAssessed";
    public bool IsCompletionReceipt => false;
    public bool CanReplay => false;

    internal void Validate(Guid organization, Guid scan)
    {
        if (OrganizationId == Guid.Empty || OrganizationId != organization || ScanId == Guid.Empty || ScanId != scan
            || JobId == Guid.Empty || Generation is < 0 or > 3 || Producer == Guid.Empty
            || (Generation == 0) != (Producer is null)
            || InputAssessment != (Generation == 0 ? "NoAttemptStarted" : "NotAssessed")
            || Status is not ("Requested" or "AlreadyCompleted")
            || Status == "Requested" && (RequestedAt is null || RequestedAt <= DateTimeOffset.UnixEpoch)
            || Status == "AlreadyCompleted" && (RequestedAt is not null || Generation == 0))
            throw new PortalFault(503, "CONTROL_CANCELLATION_RESPONSE_UNCONFIRMED");
    }
}

internal interface IPortalScanCancellation
{
    Task<PortalScanCancellationResponse> RequestAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token);
}

/// <summary>Original-user authority and actual attempt identity surround an idempotent stop request.</summary>
internal sealed class PortalScanCancellation(IPortalProjectClient project) : IPortalScanCancellation
{
    public async Task<PortalScanCancellationResponse> RequestAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        if (organization == Guid.Empty || scan == Guid.Empty) throw new PortalFault(400, "CONTROL_CANCELLATION_SCOPE_INVALID");
        await ScanAsync(actor, organization, scan, token).ConfigureAwait(false);
        var response = await project.RequestScanCancellationAsync(actor, organization, scan, token).ConfigureAwait(false);
        response.Validate(organization, scan);
        var record = await ScanAsync(actor, organization, scan, token).ConfigureAwait(false);
        var job = await project.JobAsync(actor, organization, scan, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_JOB_NOT_FOUND");
        var expected = response.Status == "AlreadyCompleted" ? "completed" : "blocked";
        if (job.OrganizationId != organization || job.ScanId != scan || job.JobId != response.JobId
            || job.Generation != response.Generation || job.State != expected
            || record.State != (expected == "completed" ? "observed" : "blocked")
            || expected == "blocked" && job.ErrorCode != "WORKER_CANCELLED_BY_CALLER")
            throw new PortalFault(503, "CONTROL_CANCELLATION_RESPONSE_UNCONFIRMED");
        if (response.Generation > 0)
        {
            var descriptor = new FixtureJob(organization, scan, job.JobId, job.Generation)
            { RecordObservation = job.RecordObservation, TraversalRoute = job.TraversalRoute };
            var actual = await project.AttemptWorkerAsync(actor, descriptor, token).ConfigureAwait(false);
            if (actual is null || actual != response.Producer)
                throw new PortalFault(503, "CONTROL_CANCELLATION_PRODUCER_UNCONFIRMED");
        }
        // The mutation may have committed even if access disappears before its response.
        // Do not release scope metadata after the last provider read without current RLS.
        var final = await ScanAsync(actor, organization, scan, token).ConfigureAwait(false);
        if (final.State != record.State) throw new PortalFault(409, "CONTROL_CANCELLATION_STATE_CHANGED");
        return response;
    }

    private async Task<PortalScanResponse> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        var value = await project.ScanAsync(actor, organization, scan, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_SCAN_NOT_FOUND");
        if (value.OrganizationId != organization || value.Id != scan)
            throw new PortalFault(503, "CONTROL_PROVIDER_RESPONSE_UNCONFIRMED");
        return value;
    }
}

internal static class PortalScanCancellationEndpoints
{
    public static void MapScanCancellation(this WebApplication app)
    {
        app.MapPost("/api/workspaces/{organization:guid}/scans/{scan:guid}/cancellation", async (HttpContext context,
            IPortalScanCancellation service, Guid organization, Guid scan, CancellationToken token) =>
        {
            await HostedControlServer.BodyAsync<PortalEmptyRequest>(context, token).ConfigureAwait(false);
            var result = await service.RequestAsync(HostedControlServer.Actor(context), organization, scan, token).ConfigureAwait(false);
            return TypedResults.Json(result, PortalJson.Options, statusCode: result.Status == "Requested" ? 202 : 200);
        }).WithName("RequestScanCancellation").WithSummary("Request caller-owned run cancellation; preserve uncertain input and originals")
            .WithDescription("Idempotent for this immutable scan. Revokes execution and publication authority. Requested does not establish physical stop, native cleanup, zero input or goal success. AlreadyCompleted leaves the completed receipt unchanged.")
            .Produces<PortalScanCancellationResponse>(202).Produces<PortalScanCancellationResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(413).ProducesProblem(503);
    }
}
