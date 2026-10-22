using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli.HostedControl;

internal interface IPortalObservationReader
{
    Task<PortalObservationResponse?> ReadAsync(PortalJobResult job, CancellationToken token);
}

/// <summary>Only the exact authorized job reference and validated original bytes can become a report.</summary>
internal sealed class PortalObservationReader(string root, IObservationArchive? archive = null) : IPortalObservationReader
{
    public async Task<PortalObservationResponse?> ReadAsync(PortalJobResult job, CancellationToken token)
    {
        var descriptor = new FixtureJob(job.OrganizationId, job.ScanId, job.JobId, job.Generation);
        try { descriptor.Validate(); }
        catch (BrowserOperationException) { throw new PortalFault(503, "CONTROL_EVIDENCE_IDENTITY_UNCONFIRMED"); }
        if (job.State != "completed" || job.Reference != descriptor.Reference || job.Digest is null || job.Digest.Length != 64
            || job.Digest.Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new PortalFault(503, "CONTROL_EVIDENCE_IDENTITY_UNCONFIRMED");
        var path = Path.Combine(Path.GetFullPath(root), descriptor.Reference.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (archive is not null)
            {
                var remote = await archive.ReadAsync(descriptor, job.Digest, token).ConfigureAwait(false);
                if (remote is not null) return Project(remote, descriptor, job.Digest, "S3");
            }
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length is <= 0 or > FixtureObservation.MaximumBytes)
                throw new PortalFault(503, "CONTROL_EVIDENCE_UNCONFIRMED");
            var bytes = new byte[checked((int)file.Length)];
            await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (file.ReadByte() != -1)
                throw new PortalFault(503, "CONTROL_EVIDENCE_UNCONFIRMED");
            return Project(bytes, descriptor, job.Digest, archive is null ? "Local" : "LocalFallback");
        }
        catch (ObservationArchiveException exception) when (exception.Failure == ObservationArchiveFailure.Invalid)
        { throw new PortalFault(503, "CONTROL_EVIDENCE_UNCONFIRMED"); }
        catch (ObservationArchiveException) { throw new PortalFault(503, "CONTROL_EVIDENCE_REMOTE_UNAVAILABLE"); }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (PortalFault) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or BrowserOperationException)
        { throw new PortalFault(503, "CONTROL_EVIDENCE_UNCONFIRMED"); }
    }

    private static PortalObservationResponse Project(byte[] bytes, FixtureJob descriptor, string digest, string storage)
    {
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != digest)
            throw new PortalFault(503, "CONTROL_EVIDENCE_UNCONFIRMED");
        var observation = ObservationStore.Validate(bytes, descriptor);
        var nodes = observation.Snapshot.Nodes.Select(node => new PortalNodeResponse(node.Id, node.ParentId,
            node.Role, node.Name, node.IsFocused, node.IsDisabled, node.HeadingLevel, node.Checked, node.Expanded,
            node.Selected, node.Required?.Value, node.Required?.Source.ToString(), node.Description)).ToArray();
        return new(observation.Snapshot.DocumentId, digest, observation.ReaderProfile,
            observation.DispatchedInputs, observation.GoalVerified, nodes, observation.Snapshot.Limitations.ToArray(), storage);
    }
}

internal interface IPortalScanService
{
    Task<PortalReportResponse> ReportAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token);
}

/// <summary>Authorization precedes all artifact access; execution and evidence availability stay separate.</summary>
internal sealed class PortalScanService(IPortalProjectClient project, IPortalObservationReader observations) : IPortalScanService
{
    public async Task<PortalReportResponse> ReportAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        var record = await project.ScanAsync(actor, organization, scan, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_SCAN_NOT_FOUND");
        if (record.OrganizationId != organization || record.Id != scan)
            throw new PortalFault(503, "CONTROL_PROVIDER_RESPONSE_UNCONFIRMED");
        var job = await project.JobAsync(actor, organization, scan, token).ConfigureAwait(false);
        if (job is null) return new(record, "Unavailable", "CONTROL_JOB_NOT_FOUND", null);
        if (job.OrganizationId != organization || job.ScanId != scan)
            throw new PortalFault(503, "CONTROL_PROVIDER_RESPONSE_UNCONFIRMED");
        var expectedState = job.State switch
        {
            "queued" => "queued",
            "leased" => "leased",
            "completed" => "observed",
            "blocked" => "blocked",
            _ => throw new PortalFault(503, "CONTROL_STATE_UNCONFIRMED")
        };
        if (record.State != expectedState)
        {
            // Separate REST reads can straddle the worker's atomic completion transaction.
            record = await project.ScanAsync(actor, organization, scan, token).ConfigureAwait(false)
                ?? throw new PortalFault(404, "CONTROL_SCAN_NOT_FOUND");
            if (record.OrganizationId != organization || record.Id != scan || record.State != expectedState)
                throw new PortalFault(503, "CONTROL_STATE_UNCONFIRMED");
        }
        if (job.State == "blocked") return new(record, "NotCollected", job.ErrorCode, null);
        if (job.State != "completed") return new(record, "Pending", job.ErrorCode, null);
        try
        {
            var observation = await observations.ReadAsync(job, token).ConfigureAwait(false);
            return observation is null ? new(record, "Unavailable", "CONTROL_EVIDENCE_UNAVAILABLE", null)
                : new(record, "Available", null, observation);
        }
        catch (PortalFault fault)
        {
            return new(record, fault.Code == "CONTROL_EVIDENCE_REMOTE_UNAVAILABLE" ? "Unavailable" : "Invalid", fault.Code, null);
        }
    }
}
