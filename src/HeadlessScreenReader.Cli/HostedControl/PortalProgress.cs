using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Local diagnostic availability is separate from execution, complete originals and goal verification.</summary>
internal sealed record PortalProgressResponse(PortalScanResponse Scan, Guid JobId, long Generation, Guid? WorkerId,
    string ProgressState, RetainedCommandProgress? Progress)
{
    public string NextAction => ProgressState switch
    {
        "NotApplicable" => "This scan has no authored keyboard progress. Read its scan report.",
        "NotStarted" => "No worker attempt has started. Read progress explicitly later using this same scan ID.",
        "NotRetained" => "No local progress prefix is retained for this attempt. Keep the original attempt; do not replay uncertain input.",
        _ => "This prefix is diagnostic only. Read the scan report and complete originals for completion; do not replay uncertain input."
    };
}

internal interface IPortalProgressReader
{
    Task<RetainedCommandProgress?> ReadAsync(FixtureJob job, Guid worker, CancellationToken token);
}

internal interface IPortalProgressSnapshotReader
{
    Task<CommandProgressSnapshot?> ReadSnapshotAsync(FixtureJob job, Guid worker, CancellationToken token);
}

internal sealed class PortalProgressReader(string evidence) : IPortalProgressReader, IPortalProgressSnapshotReader
{
    public async Task<RetainedCommandProgress?> ReadAsync(FixtureJob job, Guid worker, CancellationToken token)
    {
        var snapshot = await ReadSnapshotAsync(job, worker, token).ConfigureAwait(false);
        try { return snapshot is null ? null : CommandProgressProjection.Project(snapshot.Summary, job, worker); }
        catch (BrowserOperationException) { throw new PortalFault(409, "CONTROL_PROGRESS_INVALID"); }
    }

    public Task<CommandProgressSnapshot?> ReadSnapshotAsync(FixtureJob job, Guid worker, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!DirectoryPresent(evidence)) throw new PortalFault(503, "CONTROL_PROGRESS_UNAVAILABLE");
        var runtime = Path.Combine(evidence, ".runtime");
        var root = Path.Combine(runtime, "command-progress");
        var scope = Path.Combine(root, $"{job.OrganizationId:N}-{job.JobId:N}-{job.Generation}");
        if (!DirectoryPresent(runtime) || !DirectoryPresent(root) || !DirectoryPresent(scope)) return Task.FromResult<CommandProgressSnapshot?>(null);
        try
        {
            var result = CommandProgressJournal.ReadSnapshot(root, job.OrganizationId, job.JobId, job.Generation, token);
            _ = CommandProgressProjection.Project(result.Summary, job, worker);
            token.ThrowIfCancellationRequested(); return Task.FromResult<CommandProgressSnapshot?>(result);
        }
        catch (BrowserOperationException) { throw new PortalFault(409, "CONTROL_PROGRESS_INVALID"); }
    }
    private static bool DirectoryPresent(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint) || !attributes.HasFlag(FileAttributes.Directory))
                throw new PortalFault(409, "CONTROL_PROGRESS_INVALID");
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return false; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new PortalFault(503, "CONTROL_PROGRESS_UNAVAILABLE"); }
    }
}

internal sealed partial class PortalProgressService(IPortalProjectClient project, IPortalProgressReader reader,
    IPortalProgressSnapshotReader? snapshotReader = null, IPortalRecordedOutputReader? recordedOutputReader = null)
{
    private async Task<(PortalScanResponse Scan, PortalJobResult Job, Guid? Worker)> ScopeAsync(PortalIdentity actor,
        Guid organization, Guid scan, CancellationToken token, bool includePassiveRecording = false)
    {
        var workspace = await project.WorkspaceAsync(actor, organization, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_WORKSPACE_NOT_FOUND");
        var record = await project.ScanAsync(actor, organization, scan, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_SCAN_NOT_FOUND");
        var job = await project.JobAsync(actor, organization, scan, token).ConfigureAwait(false)
            ?? throw new PortalFault(503, "CONTROL_JOB_NOT_FOUND");
        if (workspace.Id != organization || record.OrganizationId != organization || record.Id != scan
            || job.OrganizationId != organization || job.ScanId != scan || job.JobId == Guid.Empty || job.Generation is < 0 or > 3
            || job.State is not ("queued" or "leased" or "blocked" or "completed")
            || record.State is not ("queued" or "leased" or "blocked" or "observed")
            || job.TraversalRoute is < 0 or > 4 || job.TraversalRoute > 0 && !job.RecordObservation
            || job.Generation == 0 && job.State is not ("queued" or "blocked"))
            throw new PortalFault(503, "CONTROL_PROGRESS_IDENTITY_UNCONFIRMED");
        var expectedState = job.State == "completed" ? "observed" : job.State;
        if (record.State != expectedState)
        {
            record = await project.ScanAsync(actor, organization, scan, token).ConfigureAwait(false)
                ?? throw new PortalFault(404, "CONTROL_SCAN_NOT_FOUND");
            if (record.OrganizationId != organization || record.Id != scan || record.State != expectedState)
                throw new PortalFault(503, "CONTROL_STATE_UNCONFIRMED");
        }
        Guid? worker = null;
        if ((job.TraversalRoute > 0 || includePassiveRecording && job.RecordObservation) && job.Generation > 0)
        {
            worker = await project.AttemptWorkerAsync(actor, Descriptor(job), token).ConfigureAwait(false);
            if (worker is null || worker == Guid.Empty) throw new PortalFault(503, "CONTROL_PROGRESS_IDENTITY_UNCONFIRMED");
            if (job.WorkerArchive is { } archive && archive.WorkerId != worker)
                throw new PortalFault(503, "CONTROL_PROGRESS_IDENTITY_UNCONFIRMED");
        }
        return (record, job, worker);
    }
    private static FixtureJob Descriptor(PortalJobResult job) => new(job.OrganizationId, job.ScanId, job.JobId, job.Generation)
    { RecordObservation = job.RecordObservation, TraversalRoute = job.TraversalRoute };

    public async Task<PortalProgressResponse> ReadAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        if (organization == Guid.Empty || scan == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var before = await ScopeAsync(actor, organization, scan, token).ConfigureAwait(false);
        RetainedCommandProgress? progress = null; PortalFault? failure = null;
        if (before.Worker is { } worker)
        {
            try { progress = await reader.ReadAsync(Descriptor(before.Job), worker, token).ConfigureAwait(false); }
            catch (PortalFault error) { failure = error; }
        }
        var after = await ScopeAsync(actor, organization, scan, token).ConfigureAwait(false);
        if (Descriptor(before.Job) != Descriptor(after.Job) || before.Worker != after.Worker)
            throw new PortalFault(409, "CONTROL_PROGRESS_ATTEMPT_CHANGED");
        token.ThrowIfCancellationRequested();
        if (failure is not null) throw failure;
        if (progress is not null)
        {
            try { progress.Validate(); }
            catch (BrowserOperationException) { throw new PortalFault(409, "CONTROL_PROGRESS_INVALID"); }
            if (progress.Job != Descriptor(after.Job) || progress.WorkerId != after.Worker)
                throw new PortalFault(409, "CONTROL_PROGRESS_IDENTITY_UNCONFIRMED");
        }
        return new(after.Scan, after.Job.JobId, after.Job.Generation, after.Worker, after.Job.TraversalRoute == 0 ? "NotApplicable" : after.Worker is null ? "NotStarted"
            : progress is null ? "NotRetained" : "Available", progress);
    }
}

internal static class PortalProgressEndpoints
{
    public static void MapRecordedProgress(this WebApplication app) =>
        app.MapGet("/api/workspaces/{organization:guid}/scans/{scan:guid}/progress", async (HttpContext context,
            PortalProgressService progress, Guid organization, Guid scan, CancellationToken token) =>
            TypedResults.Ok(await progress.ReadAsync(HostedControlServer.Actor(context), organization, scan, token).ConfigureAwait(false)))
        .WithName("ReadRecordedCommandProgress").WithSummary("Read retained command diagnostics for the current admitted attempt")
        .WithDescription("Original caller/workspace authority and actual producer checked before and after local reads. Never a completion receipt or replay authority.")
        .Produces<PortalProgressResponse>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
}
