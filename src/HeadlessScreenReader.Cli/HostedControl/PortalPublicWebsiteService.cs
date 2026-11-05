using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed class PortalPublicWebsiteService(IPortalProjectClient project, IPortalPublicWebsiteProjectClient? publicProject,
    Func<Task<bool>>? processorReady = null) : IPortalPublicWebsites
{
    private IPortalPublicWebsiteProjectClient Client => publicProject ?? throw new PortalFault(503, "CONTROL_PUBLIC_WEBSITE_UNAVAILABLE");

    public Task<PortalPublicWebsiteCreatedResponse> AdmitAsync(PortalIdentity actor, Guid organization,
        PortalCreatePublicWebsiteRequest request, CancellationToken token)
    {
        if (request.RequestId == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        PublicReaderSource source;
        try { source = PublicReaderSource.FromUrl(request.Url); }
        catch (Exception error) when (error is ArgumentException or BrowserOperationException or UriFormatException)
        { throw new PortalFault(400, "CONTROL_PUBLIC_WEBSITE_TARGET_INVALID"); }
        return CurrentAsync(actor, organization, async () =>
        {
            var existing = await Client.PublicWebsiteRequestAsync(actor, organization, request.RequestId, token).ConfigureAwait(false);
            if (existing is not null)
            {
                existing.Validate(organization);
                if (existing.RequestId != request.RequestId || existing.CreatedBy != actor.UserId || existing.Source != source)
                    throw new PortalFault(409, "CONTROL_REQUEST_CONFLICT");
                var status = await StatusCoreAsync(actor, organization, existing.Id, token).ConfigureAwait(false);
                CheckAdmission(status, actor, request.RequestId, source);
                return Created(status);
            }
            if (processorReady is null || !await processorReady().ConfigureAwait(false))
                throw new PortalFault(503, "CONTROL_PUBLIC_WEBSITE_PROCESSOR_UNAVAILABLE");
            token.ThrowIfCancellationRequested();
            var admitted = await Client.CreatePublicWebsiteAsync(actor, organization, request.RequestId, source, token).ConfigureAwait(false);
            if (admitted.OrganizationId != organization || admitted.ScanId == Guid.Empty || admitted.JobId == Guid.Empty
                || admitted.RequestId != request.RequestId || admitted.TargetUrl != source.TargetUrl
                || admitted.AllowedHost != source.AllowedHost || admitted.ReaderProfile != source.Profile)
                throw PortalPublicWebsiteContract.Invalid();
            var current = await StatusCoreAsync(actor, organization, admitted.ScanId, token).ConfigureAwait(false);
            CheckAdmission(current, actor, request.RequestId, source);
            if (current.JobId != admitted.JobId) throw PortalPublicWebsiteContract.Invalid();
            return Created(current);
        }, token);
    }

    public Task<PortalPublicWebsiteListResponse> ListAsync(PortalIdentity actor, Guid organization, CancellationToken token) =>
        CurrentAsync(actor, organization, async () =>
        {
            var rows = await Client.PublicWebsitesAsync(actor, organization, token).ConfigureAwait(false);
            if (rows.Length > PortalPublicWebsiteContract.ListSize + 1) throw PortalPublicWebsiteContract.Invalid();
            foreach (var row in rows) row.Validate(organization);
            for (var index = 1; index < rows.Length; index++)
                if (rows[index].CreatedAt > rows[index - 1].CreatedAt || rows[index].CreatedAt == rows[index - 1].CreatedAt
                    && string.CompareOrdinal(rows[index].Id.ToString("D"), rows[index - 1].Id.ToString("D")) >= 0)
                    throw PortalPublicWebsiteContract.Invalid();
            for (var count = Math.Min(rows.Length, PortalPublicWebsiteContract.ListSize); count >= 0; count--)
            {
                var response = new PortalPublicWebsiteListResponse(rows.Take(count).ToArray(), rows.Length > count);
                if (JsonSerializer.SerializeToUtf8Bytes(response, PortalJson.Options).Length <= PortalPublicWebsiteContract.MaximumReplyBytes)
                    return response;
            }
            throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_PAGE_TOO_LARGE");
        }, token);

    public Task<PortalPublicWebsiteStatusResponse> StatusAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) =>
        CurrentAsync(actor, organization, () => StatusCoreAsync(actor, organization, scan, token), token);

    public async Task<PortalPublicWebsiteNodePageResponse> NodesAsync(PortalIdentity actor, Guid organization, Guid scan,
        int capture, int offset, int limit, string? digest, CancellationToken token)
    {
        if (capture is < 0 or > 1 || offset is < 0 or > ReaderWorkerRequest.MaximumNodes || limit is < 1 or > 100
            || offset > 0 && digest is null || digest is not null && !PortalPublicWebsiteContract.Digest(digest))
            throw new PortalFault(400, "CONTROL_PUBLIC_WEBSITE_PAGE_INVALID");
        var value = await OriginalContextAsync(actor, organization, scan, digest, token).ConfigureAwait(false);
        var snapshot = value.Observation.Captures[capture];
        if (offset > snapshot.Nodes.Length) throw new PortalFault(400, "CONTROL_PUBLIC_WEBSITE_PAGE_INVALID");
        var count = Math.Min(limit, snapshot.Nodes.Length - offset);
        while (true)
        {
            var nodes = snapshot.Nodes.Skip(offset).Take(count).Select(node => new PortalNodeResponse(node.Id, node.ParentId,
                node.Role, node.Name, node.IsFocused, node.IsDisabled, node.HeadingLevel, node.Checked, node.Expanded,
                node.Selected, node.Required?.Value, node.Required?.Source.ToString(), node.Description)).ToArray();
            var response = new PortalPublicWebsiteNodePageResponse(value.Status, value.Status.OriginalSha256!, value.Observation.ReceivedAt,
                capture, value.Observation.Captures.Length, snapshot.DocumentId, snapshot.DocumentUrl!, snapshot.CaptureEpoch,
                snapshot.Nodes.Length, offset, nodes, offset + count < snapshot.Nodes.Length ? offset + count : null,
                "PageObservationsOnly", value.Observation.DispatchedInputs, "NotAssessed", snapshot.Limitations.ToArray());
            if (JsonSerializer.SerializeToUtf8Bytes(response, PortalJson.Options).Length <= PortalPublicWebsiteContract.MaximumReplyBytes)
            {
                token.ThrowIfCancellationRequested();
                return response;
            }
            if (count <= 1) throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_PAGE_TOO_LARGE");
            count--;
        }
    }

    public async Task<PortalPublicWebsiteOriginalPageResponse> OriginalAsync(PortalIdentity actor, Guid organization, Guid scan,
        int offset, string digest, CancellationToken token)
    {
        if (!PortalPublicWebsiteContract.Digest(digest) || offset is < 0 or > PublicWebsiteObservation.MaximumSupportedBytes)
            throw new PortalFault(400, "CONTROL_PUBLIC_WEBSITE_PAGE_INVALID");
        var value = await OriginalContextAsync(actor, organization, scan, digest, token).ConfigureAwait(false);
        if (offset > value.Bytes.Length || offset != value.Bytes.Length && offset % PortalPublicWebsiteContract.OriginalPageBytes != 0)
            throw new PortalFault(400, "CONTROL_PUBLIC_WEBSITE_PAGE_INVALID");
        var data = value.Bytes.AsSpan(offset, Math.Min(PortalPublicWebsiteContract.OriginalPageBytes, value.Bytes.Length - offset)).ToArray();
        var next = offset + data.Length;
        token.ThrowIfCancellationRequested();
        return new(value.Status, value.Status.OriginalSha256!, value.Bytes.Length, offset, data, TraversalArchive.Hash(data),
            next < value.Bytes.Length ? next : null);
    }

    private sealed record AuthorizedOriginal(PortalPublicWebsiteStatusResponse Status, byte[] Bytes, PublicWebsiteObservation Observation);

    private Task<AuthorizedOriginal> OriginalContextAsync(PortalIdentity actor, Guid organization, Guid scan, string? digest, CancellationToken token) =>
        CurrentAsync(actor, organization, async () =>
        {
            var before = await StatusCoreAsync(actor, organization, scan, token).ConfigureAwait(false);
            byte[]? bytes = null; PublicWebsiteObservation? observation = null; PortalFault? failure = null;
            try
            {
                if (before.State != "completed") throw new PortalFault(404, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_NOT_AVAILABLE");
                if (digest is not null && before.OriginalSha256 != digest) throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_CHANGED");
                var stored = await Client.PublicWebsiteOriginalAsync(actor, before, token).ConfigureAwait(false)
                    ?? throw new PortalFault(404, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_NOT_AVAILABLE");
                if (stored.OrganizationId != organization || stored.ScanId != scan || stored.JobId != before.JobId
                    || stored.Generation != before.Generation || stored.ProducerId != before.ProducerId || stored.Sha256 != before.OriginalSha256
                    || stored.Text.Length > PublicWebsiteObservation.MaximumSupportedBytes)
                    throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_CHANGED");
                bytes = Encoding.UTF8.GetBytes(stored.Text);
                observation = PublicWebsiteObservationCodec.Decode(bytes, stored.Sha256, before.Job, stored.ProducerId);
            }
            catch (PortalFault fault) { failure = fault; }
            catch (Exception error) when (error is InvalidDataException or JsonException or BrowserOperationException or ArgumentException)
            { failure = new(409, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_INVALID"); }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            { failure = new(503, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_UNAVAILABLE"); }
            var after = await StatusCoreAsync(actor, organization, scan, token).ConfigureAwait(false);
            if ((before with { WorkerPermission = null }) != (after with { WorkerPermission = null }))
                throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_CHANGED");
            token.ThrowIfCancellationRequested();
            if (failure is not null) throw failure;
            return new AuthorizedOriginal(after, bytes!, observation!);
        }, token);

    private async Task<PortalPublicWebsiteStatusResponse> StatusCoreAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        if (scan == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var value = await Client.PublicWebsiteStatusAsync(actor, organization, scan, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_PUBLIC_WEBSITE_NOT_FOUND");
        value.Validate(organization, scan); return value;
    }

    private async Task<T> CurrentAsync<T>(PortalIdentity actor, Guid organization, Func<Task<T>> operation, CancellationToken token)
    {
        if (organization == Guid.Empty || actor.UserId == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        await MemberAsync(actor, organization, token).ConfigureAwait(false);
        T? result = default; PortalFault? failure = null;
        try { token.ThrowIfCancellationRequested(); result = await operation().ConfigureAwait(false); }
        catch (PortalFault fault) { failure = fault; }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        { failure = new(503, "CONTROL_PUBLIC_WEBSITE_OPERATION_UNCONFIRMED"); }
        // Current denial wins over a held successful result or mapped read error.
        await MemberAsync(actor, organization, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (failure is not null) throw failure;
        return result!;
    }

    private async Task MemberAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    {
        var workspace = await project.WorkspaceAsync(actor, organization, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_WORKSPACE_NOT_FOUND");
        if (workspace.Id != organization) throw PortalPublicWebsiteContract.Invalid();
    }
    private static void CheckAdmission(PortalPublicWebsiteStatusResponse value, PortalIdentity actor, Guid request, PublicReaderSource source)
    {
        if (value.RequestId != request || value.CreatedBy != actor.UserId
            || PortalPublicWebsiteContract.Source(value.TargetUrl, value.AllowedHost, value.ReaderProfile) != source)
            throw new PortalFault(409, "CONTROL_REQUEST_CONFLICT");
    }
    private static PortalPublicWebsiteCreatedResponse Created(PortalPublicWebsiteStatusResponse value) =>
        new(value.OrganizationId, value.Id, value.JobId, value.RequestId, value.TargetUrl, value.AllowedHost, value.ReaderProfile);
}
