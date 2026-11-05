using System.Collections.Immutable;
using System.Security.Cryptography;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    private string PublicCollection => $"/api/workspaces/{_options.Organization:D}/website-scans";
    private static ToolReply PublicError(string code) => new("error", null, PublicReaderSource.CurrentProfile, ErrorCode: code);
    private async Task<ToolReply> PublicLockedAsync(Func<Task<ToolReply>> operation, CancellationToken token, Guid? request = null) =>
        (await LockedAsync(operation, token, request).ConfigureAwait(false)) with { Profile = PublicReaderSource.CurrentProfile };

    public Task<ToolReply> StartPublicWebsiteAsync(string requestId, string url, CancellationToken token = default)
    {
        if (!Guid.TryParse(requestId, out var request) || request == Guid.Empty) return Task.FromResult(PublicError("PUBLIC_WEBSITE_REQUEST_ID_REQUIRED"));
        PublicReaderSource source;
        try { source = PublicReaderSource.FromUrl(url); }
        catch (Exception error) when (error is BrowserOperationException or ArgumentException or UriFormatException)
        { return Task.FromResult(PublicError("PUBLIC_WEBSITE_TARGET_INVALID")); }
        return PublicLockedAsync(async () =>
        {
            using var json = await _transport.SendAsync(HttpMethod.Post, PublicCollection, new { requestId = request, url = source.TargetUrl }, 202).ConfigureAwait(false);
            var value = Parse<HostedPublicWebsiteAdmission>(json);
            Require(value.OrganizationId == _options.Organization && value.ScanId != Guid.Empty && value.JobId != Guid.Empty
                && value.RequestId == request && PublicSource(value.ReaderProfile, value.TargetUrl, value.AllowedHost) == source);
            return new("website-scan-admitted", null, source.Profile, new
            {
                Admission = value,
                Execution = "NotEstablishedByAdmission",
                GoalVerification = "NotAssessed",
                EvidenceSufficiency = "NotAssessed",
                AutomaticRetry = false,
                WorkerContinuesAfterDisconnect = true,
                NextAction = "Read website_scan_status for this scan. Explicit reconciliation must reuse the same request UUID and source."
            });
        }, token, request);
    }

    public Task<ToolReply> ListPublicWebsitesAsync(CancellationToken token = default) => PublicLockedAsync(async () =>
    {
        using var json = await _transport.SendAsync(HttpMethod.Get, PublicCollection).ConfigureAwait(false);
        var value = Parse<HostedPublicWebsiteList>(json);
        Require(!value.Items.IsDefault && value.Items.Length <= 20 && (!value.HasMore || value.Items.Length > 0));
        foreach (var row in value.Items)
        {
            Require(row is not null && row.OrganizationId == _options.Organization && row.Id != Guid.Empty
                && row.RequestId != Guid.Empty && row.CreatedBy != Guid.Empty && row.CreatedAt != default
                && row.State is "queued" or "leased" or "observed" or "blocked" or "cancelled");
            _ = PublicSource(row!.ReaderProfile, row.TargetUrl, row.AllowedHost);
        }
        Require(value.Items.Select(row => row.Id).Distinct().Count() == value.Items.Length);
        for (var index = 1; index < value.Items.Length; index++)
            Require(value.Items[index].CreatedAt < value.Items[index - 1].CreatedAt
                || value.Items[index].CreatedAt == value.Items[index - 1].CreatedAt
                && string.CompareOrdinal(value.Items[index].Id.ToString("D"), value.Items[index - 1].Id.ToString("D")) < 0);
        for (var count = value.Items.Length; count >= 0; count--)
        {
            var page = value with { Items = value.Items.Take(count).ToImmutableArray(), HasMore = value.HasMore || count < value.Items.Length };
            var reply = new ToolReply("website-scan-list", null, PublicReaderSource.CurrentProfile, page);
            if (Fits(reply)) return reply;
        }
        return PublicError("PUBLIC_WEBSITE_PAGE_TOO_LARGE");
    }, token);

    public Task<ToolReply> PublicWebsiteStatusAsync(string scanId, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var scan) || scan == Guid.Empty) return Task.FromResult(PublicError("PUBLIC_WEBSITE_SCAN_ID_REQUIRED"));
        return PublicLockedAsync(async () =>
        {
            using var json = await _transport.SendAsync(HttpMethod.Get, PublicCollection + $"/{scan:D}").ConfigureAwait(false);
            var value = Parse<HostedPublicWebsiteStatus>(json); ValidatePublicStatus(value, scan);
            return new("website-scan-status", null, value.ReaderProfile, new
            {
                Scan = value,
                Scope = "PageObservationsOnly",
                GoalVerification = "NotAssessed",
                EvidenceSufficiency = "NotAssessed",
                RequiresFencedRecovery = value.WorkerPermission?.Status is "Expired" or "Missing",
                AutomaticRetry = false
            });
        }, token);
    }

    public Task<ToolReply> PublicWebsiteNodesAsync(string scanId, int capture = 1, int offset = 0, int limit = 80,
        string? originalSha256 = null, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var scan) || scan == Guid.Empty) return Task.FromResult(PublicError("PUBLIC_WEBSITE_SCAN_ID_REQUIRED"));
        if (capture is < 0 or > 1 || offset is < 0 or > 2048 || limit is < 1 or > 100
            || offset > 0 && originalSha256 is null || originalSha256 is not null && !Digest(originalSha256))
            return Task.FromResult(PublicError("PUBLIC_WEBSITE_PAGE_INVALID"));
        return PublicLockedAsync(async () =>
        {
            var path = PublicCollection + $"/{scan:D}/observations/{capture}/{offset}/{limit}"
                + (originalSha256 is null ? "" : "/" + originalSha256);
            using var json = await _transport.SendAsync(HttpMethod.Get, path).ConfigureAwait(false);
            var value = Parse<HostedPublicWebsiteNodes>(json); ValidatePublicStatus(value.Scan, scan);
            Require(value.Scan.State == "completed" && value.OriginalSha256 == value.Scan.OriginalSha256
                && (originalSha256 is null || value.OriginalSha256 == originalSha256)
                && value.CaptureIndex == capture && value.Captures == 2 && value.ReceivedAt != default
                && value.CaptureEpoch >= 1 && !string.IsNullOrWhiteSpace(value.DocumentId)
                && PublicSource(value.Scan.ReaderProfile, value.Scan.TargetUrl, value.Scan.AllowedHost).AllowsDocument(value.DocumentUrl)
                && value.TotalNodes is >= 1 and <= 2048 && value.Offset == offset && offset <= value.TotalNodes
                && !value.Nodes.IsDefault && value.Nodes.Length <= limit && value.Nodes.Length <= value.TotalNodes - offset
                && (offset == value.TotalNodes || value.Nodes.Length > 0) && !value.Limitations.IsDefault
                && value.Nodes.All(node => node is not null && !string.IsNullOrWhiteSpace(node.Id) && !string.IsNullOrWhiteSpace(node.Role))
                && value.Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() == value.Nodes.Length
                && value.Scope == "PageObservationsOnly" && value.DispatchedInputs == 0 && value.GoalVerification == "NotAssessed"
                && value.NextOffset == (offset + value.Nodes.Length < value.TotalNodes ? offset + value.Nodes.Length : null));
            for (var count = value.Nodes.Length; count >= 0; count--)
            {
                if (count == 0 && offset < value.TotalNodes) break;
                var page = value with { Nodes = value.Nodes.Take(count).ToImmutableArray(), NextOffset = offset + count < value.TotalNodes ? offset + count : null };
                var reply = new ToolReply("website-observation", null, value.Scan.ReaderProfile, page);
                if (Fits(reply)) return reply;
            }
            return PublicError("PUBLIC_WEBSITE_PAGE_TOO_LARGE");
        }, token);
    }

    public Task<ToolReply> PublicWebsiteOriginalAsync(string scanId, string originalSha256, int offset = 0, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var scan) || scan == Guid.Empty) return Task.FromResult(PublicError("PUBLIC_WEBSITE_SCAN_ID_REQUIRED"));
        if (originalSha256 is null || !Digest(originalSha256) || offset is < 0 or > PublicWebsiteObservation.MaximumSupportedBytes)
            return Task.FromResult(PublicError("PUBLIC_WEBSITE_PAGE_INVALID"));
        return PublicLockedAsync(async () =>
        {
            using var json = await _transport.SendAsync(HttpMethod.Get, PublicCollection + $"/{scan:D}/original/{offset}/{originalSha256}").ConfigureAwait(false);
            var value = Parse<HostedPublicWebsiteOriginal>(json); ValidatePublicStatus(value.Scan, scan);
            Require(value.Scan.State == "completed" && value.Scan.OriginalSha256 == originalSha256 && value.OriginalSha256 == originalSha256
                && value.TotalBytes is >= 1 and <= PublicWebsiteObservation.MaximumSupportedBytes && value.Offset == offset && offset <= value.TotalBytes
                && (offset == value.TotalBytes || offset % 65536 == 0) && value.Data is not null
                && value.Data.Length == Math.Min(65536, value.TotalBytes - offset)
                && value.PageSha256 == Convert.ToHexStringLower(SHA256.HashData(value.Data))
                && value.NextOffset == (offset + value.Data.Length < value.TotalBytes ? offset + value.Data.Length : null));
            var reply = new ToolReply("website-original", null, value.Scan.ReaderProfile, value);
            return Fits(reply) ? reply : PublicError("PUBLIC_WEBSITE_PAGE_TOO_LARGE");
        }, token);
    }

    private static PublicReaderSource PublicSource(string profile, string url, string host)
    {
        try
        {
            var value = PublicReaderSource.FromUrl(url);
            Require(value.Profile == profile && value.TargetUrl == url && value.AllowedHost == host); return value;
        }
        catch (Exception error) when (error is BrowserOperationException or ArgumentException or UriFormatException)
        { throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }
    }

    private void ValidatePublicStatus(HostedPublicWebsiteStatus value, Guid scan)
    {
        Require(value is not null && value.OrganizationId == _options.Organization && value.Id == scan && scan != Guid.Empty
            && value.RequestId != Guid.Empty && value.CreatedBy != Guid.Empty && value.CreatedAt != default && value.JobId != Guid.Empty
            && value.Generation is >= 0 and <= 3 && value.State is "queued" or "leased" or "completed" or "blocked" or "cancelled"
            && (value.State != "queued" || value.Generation == 0) && (value.State is not ("leased" or "completed") || value.Generation > 0)
            && SafeCode(value.ErrorCode) && (value.ErrorCode is null || value.ErrorCode.StartsWith("PUBLIC_WEBSITE_", StringComparison.Ordinal)
                && value.ErrorCode.All(c => c is >= 'A' and <= 'Z' or '_')));
        _ = PublicSource(value!.ReaderProfile, value.TargetUrl, value.AllowedHost);
        if (value.State == "completed") Require(value.ProducerId is { } producer && producer != Guid.Empty
            && value.OriginalSha256 is not null && Digest(value.OriginalSha256) && value.ErrorCode is null);
        else Require(value.ProducerId is null && value.OriginalSha256 is null);
        if (value.State == "leased")
        {
            Require(value.WorkerPermission is not null);
            try { value.WorkerPermission!.Validate(_options.Organization, scan); }
            catch (BrowserOperationException) { throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }
            Require(value.WorkerPermission.JobId == value.JobId && value.WorkerPermission.Generation == value.Generation);
        }
        else Require(value.WorkerPermission is null);
    }
}
