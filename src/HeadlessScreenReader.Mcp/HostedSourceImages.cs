using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    private sealed record HostedSourceImages(HostedScan Scan, Guid JobId, long Generation, Guid? WorkerId,
        string SourceState, RetainedSourceImages? Sources, string NextAction);

    public Task<ToolReply> SourceManifestAsync(string scanId, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_ID_REQUIRED"));
        return LockedAsync(async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Get, Collection + $"/{id:D}/source-images").ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var value = Parse<HostedSourceImages>(document);
            var scan = value.Scan ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); ValidateScan(scan);
            Require(scan.Id == id && value.JobId != Guid.Empty && value.Generation is >= 0 and <= 3 && value.WorkerId != Guid.Empty
                && value.SourceState is "NotApplicable" or "NotStarted" or "NotRetained" or "Empty" or "Available"
                && (value.SourceState is "Available" or "Empty") == (value.Sources is not null)
                && (value.SourceState is "NotApplicable" or "NotStarted" ? value.WorkerId is null : value.WorkerId is not null && value.Generation > 0)
                && (value.SourceState != "NotStarted" || value.Generation == 0));
            if (value.Sources is { } sources)
            {
                try { sources.Validate(); } catch (BrowserOperationException) { Require(false); }
                Require(sources.Job.OrganizationId == _options.Organization && sources.Job.ScanId == id
                    && sources.Job.JobId == value.JobId && sources.Job.Generation == value.Generation && sources.WorkerId == value.WorkerId
                    && (value.SourceState == "Empty") == (sources.Files.Length == 0));
            }
            var reply = new ToolReply("scan-source-manifest", null, ScreenReaderSession.Profile, value);
            return Fits(reply) ? reply : ToolReply.Error("HOSTED_SCAN_SOURCES_TOO_LARGE");
        }, token);
    }

    public Task<ToolReply> SourceImageAsync(string scanId, string prefixSha256, int sourceSequence, string imageSha256,
        int offset = 0, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_ID_REQUIRED"));
        if (prefixSha256 is not { Length: 64 } || !Digest(prefixSha256) || imageSha256 is not { Length: 64 } || !Digest(imageSha256)
            || sourceSequence is < 1 or > RetainedSourceImages.MaximumFrames) return Task.FromResult(ToolReply.Error("SOURCE_IMAGE_IDENTITY_INVALID"));
        if (offset is < 0 or > RetainedSourceImageFile.MaximumBytes) return Task.FromResult(ToolReply.Error("SOURCE_IMAGE_PAGE_INVALID"));
        return LockedAsync(async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Post, Collection + $"/{id:D}/source-images/bytes",
                new { prefixSha256, sourceSequence, imageSha256, offset }).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); var page = Parse<RetainedSourceImagePage>(document);
            var source = page.Source ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
            try { source.Validate(); } catch (BrowserOperationException) { Require(false); }
            var image = source.Image;
            Require(source.Job.OrganizationId == _options.Organization && source.Job.ScanId == id
                && source.PrefixSha256 == prefixSha256 && image.SourceSequence == sourceSequence && image.Sha256 == imageSha256
                && page.Offset == offset && offset <= image.Bytes
                && (offset == image.Bytes || offset % RetainedSourceImagePage.PageBytes == 0)
                && page.Data is not null && page.Data.Length == Math.Min(RetainedSourceImagePage.PageBytes, image.Bytes - offset)
                && page.PageSha256 is { Length: 64 } && Digest(page.PageSha256)
                && Convert.ToHexStringLower(SHA256.HashData(page.Data)) == page.PageSha256);
            var next = offset + page.Data!.Length;
            Require(page.NextOffset == (next < image.Bytes ? next : null));
            if (offset == 0 && next == image.Bytes) Require(page.PageSha256 == imageSha256);
            var reply = new ToolReply("scan-source-image", null, ScreenReaderSession.Profile, page);
            return Fits(reply) ? reply : ToolReply.Error("HOSTED_SCAN_SOURCES_TOO_LARGE");
        }, token);
    }
}
