using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

internal enum RecordedOutputStorageState { Missing, Incomplete, Sealed }
internal sealed record RecordedOutputRead(RecordedOutputStorageState State, RecordedOutputSnapshot? Snapshot = null);
internal interface IPortalRecordedOutputReader
{
    Task<RecordedOutputRead> ReadAsync(FixtureJob job, Guid worker, CancellationToken token);
}
internal sealed class PortalRecordedOutputReader(string evidence) : IPortalRecordedOutputReader
{
    public Task<RecordedOutputRead> ReadAsync(FixtureJob job, Guid worker, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!DirectoryPresent(evidence)) throw new PortalFault(503, "CONTROL_OUTPUT_UNAVAILABLE");
        var runtime = Path.Combine(evidence, ".runtime"); var root = Path.Combine(runtime, "recorded-output");
        var attempt = Path.Combine(root, $"{job.OrganizationId:N}-{job.JobId:N}-{job.Generation}");
        if (!DirectoryPresent(runtime) || !DirectoryPresent(root) || !DirectoryPresent(attempt))
            return Task.FromResult(new RecordedOutputRead(RecordedOutputStorageState.Missing));
        try { return Task.FromResult(new RecordedOutputRead(RecordedOutputStorageState.Sealed, RecordedOutputJournal.Read(root, job, worker, token))); }
        catch (BrowserOperationException error) when (error.Code == "WORKER_OUTPUT_NOT_SEALED")
        { return Task.FromResult(new RecordedOutputRead(RecordedOutputStorageState.Incomplete)); }
    }
    private static bool DirectoryPresent(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new PortalFault(409, "CONTROL_OUTPUT_INVALID");
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
}

/// <summary>Saved complete output is diagnostic retention, separate from published evidence and completed execution.</summary>
internal sealed record PortalRecordedOutputResponse(PortalScanResponse Scan, Guid JobId, long Generation, Guid? WorkerId,
    string OutputState, RetainedRecordingOutput? Output)
{
    public string NextAction => OutputState switch
    {
        "NotApplicable" => "This attempt did not request a recording.",
        "NotStarted" => "No worker attempt has started. Admission is not evidence collection.",
        "NotRetained" => "No saved output exists for this attempt. Missing older media cannot be reconstructed from diagnostics.",
        "Incomplete" => "A complete recording bundle was not sealed. Keep the retained diagnostics; do not repeat uncertain input.",
        "CleanupUnconfirmed" => "Complete output was retained, but its native cleanup acknowledgement is unconfirmed. It is not available for export or publication.",
        "Available" => "Read the bounded byte pages, compare the original attempt and producer on every page, verify page and whole-bundle hashes, then reauthorize at exact EOF before delivery. Retention is separate from completed execution and published evidence. Never replay input.",
        _ => "Recording availability is unconfirmed."
    };
}
internal sealed record PortalRecordedOutputRequest(string BundleSha256, int Offset = 0);

internal sealed partial class PortalProgressService
{
    private sealed record AuthorizedOutput(PortalScanResponse Scan, FixtureJob Job, Guid? Worker,
        RecordedOutputRead? Stored, RetainedRecordingOutput? Output);

    public async Task<PortalRecordedOutputResponse> OutputManifestAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        var value = await ReadOutputAsync(actor, organization, scan, token).ConfigureAwait(false);
        var state = !value.Job.RecordObservation ? "NotApplicable" : value.Worker is null ? "NotStarted"
            : value.Stored?.State == RecordedOutputStorageState.Missing ? "NotRetained"
            : value.Stored?.State == RecordedOutputStorageState.Incomplete ? "Incomplete"
            : value.Output is null ? "CleanupUnconfirmed" : "Available";
        return new(value.Scan, value.Job.JobId, value.Job.Generation, value.Worker, state, value.Output);
    }

    public async Task<RetainedRecordingOutputPage> OutputPageAsync(PortalIdentity actor, Guid organization, Guid scan,
        PortalRecordedOutputRequest request, CancellationToken token)
    {
        if (request.BundleSha256 is not { Length: 64 } || !request.BundleSha256.All(char.IsAsciiHexDigitLower)
            || request.Offset is < 0 or > RetainedRecordingOutput.MaximumBytes)
            throw new PortalFault(400, "CONTROL_OUTPUT_PAGE_INVALID");
        var value = await ReadOutputAsync(actor, organization, scan, token).ConfigureAwait(false);
        var output = value.Output ?? throw new PortalFault(404, "CONTROL_OUTPUT_NOT_AVAILABLE");
        if (request.BundleSha256 != output.BundleSha256) throw new PortalFault(409, "CONTROL_OUTPUT_CHANGED");
        if (request.Offset > output.Bytes || request.Offset != output.Bytes && request.Offset % RetainedRecordingOutputPage.PageBytes != 0)
            throw new PortalFault(400, "CONTROL_OUTPUT_PAGE_INVALID");
        var data = value.Stored!.Snapshot!.Bundle.AsSpan(request.Offset, Math.Min(RetainedRecordingOutputPage.PageBytes, output.Bytes - request.Offset)).ToArray();
        var next = request.Offset + data.Length;
        token.ThrowIfCancellationRequested();
        return new(output, request.Offset, data, TraversalArchive.Hash(data), next < output.Bytes ? next : null);
    }

    private async Task<AuthorizedOutput> ReadOutputAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        if (organization == Guid.Empty || scan == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        token.ThrowIfCancellationRequested();
        var before = await ScopeAsync(actor, organization, scan, token, includePassiveRecording: true).ConfigureAwait(false);
        RecordedOutputRead? stored = null; RetainedRecordingOutput? output = null; PortalFault? failure = null;
        try
        {
            if (before.Job.RecordObservation && before.Worker is { } worker)
            {
                stored = await (recordedOutputReader ?? throw new PortalFault(503, "CONTROL_OUTPUT_UNAVAILABLE"))
                    .ReadAsync(Descriptor(before.Job), worker, token).ConfigureAwait(false);
                if (stored is null || !Enum.IsDefined(stored.State) || (stored.State == RecordedOutputStorageState.Sealed) != (stored.Snapshot is not null))
                    throw new PortalFault(409, "CONTROL_OUTPUT_INVALID");
                if (stored.Snapshot is { } snapshot) output = RecordedOutputProjection.Manifest(snapshot, Descriptor(before.Job), worker);
            }
        }
        catch (PortalFault error) { failure = error; }
        catch (Exception error) when (error is BrowserOperationException or JsonException or InvalidDataException)
        { failure = new(409, "CONTROL_OUTPUT_INVALID"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { failure = new(503, "CONTROL_OUTPUT_UNAVAILABLE"); }
        var after = await ScopeAsync(actor, organization, scan, token, includePassiveRecording: true).ConfigureAwait(false);
        if (Descriptor(before.Job) != Descriptor(after.Job) || before.Worker != after.Worker)
            throw new PortalFault(409, "CONTROL_OUTPUT_ATTEMPT_CHANGED");
        token.ThrowIfCancellationRequested();
        if (failure is not null) throw failure;
        return new(after.Scan, Descriptor(after.Job), after.Worker, stored, output);
    }
}

internal static class PortalRecordedOutputEndpoints
{
    public static void MapRecordedOutput(this WebApplication app)
    {
        app.MapGet("/api/workspaces/{organization:guid}/scans/{scan:guid}/recorded-output", async (HttpContext context,
            PortalProgressService progress, Guid organization, Guid scan, CancellationToken token) =>
            TypedResults.Ok(await progress.OutputManifestAsync(HostedControlServer.Actor(context), organization, scan, token).ConfigureAwait(false)))
            .WithName("ReadRetainedRecordingOutput").WithSummary("Read the original producer's complete retained recording-output manifest")
            .WithDescription("Explicit unrecorded, unstarted, missing, incomplete and cleanup-unconfirmed states. Available bytes do not authorize replay or prove job completion, evidence publication or an accessibility barrier. No native ownership wrapper.")
            .Produces<PortalRecordedOutputResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        app.MapPost("/api/workspaces/{organization:guid}/scans/{scan:guid}/recorded-output/bytes", async (HttpContext context,
            PortalProgressService progress, Guid organization, Guid scan, CancellationToken token) =>
        {
            var request = await HostedControlServer.BodyAsync<PortalRecordedOutputRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Ok(await progress.OutputPageAsync(HostedControlServer.Actor(context), organization, scan, request, token).ConfigureAwait(false));
        }).WithName("ReadRetainedRecordingOutputBytes").WithSummary("Read a bounded page of the exact retained child-output ZIP")
            .WithDescription("Requires the manifest's bundle SHA-256. 64 KiB aligned offsets or exact EOF; verify every page and the complete bundle, then reauthorize at EOF before delivery. Original caller and producer rechecked per page, including storage failures. Never retry uncertain input.")
            .Accepts<PortalRecordedOutputRequest>("application/json").Produces<RetainedRecordingOutputPage>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
    }
}
