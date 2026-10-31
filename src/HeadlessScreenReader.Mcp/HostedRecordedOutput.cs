using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    private sealed record HostedRecordedOutput(HostedScan Scan, Guid JobId, long Generation, Guid? WorkerId,
        string OutputState, RetainedRecordingOutput? Output, string NextAction);

    public Task<ToolReply> OutputManifestAsync(string scanId, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_ID_REQUIRED"));
        return LockedAsync(async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Get, Collection + $"/{id:D}/recorded-output").ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var value = Parse<HostedRecordedOutput>(document);
            var scan = value.Scan ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); ValidateScan(scan);
            Require(scan.Id == id && value.JobId != Guid.Empty && value.Generation is >= 0 and <= 3
                && value.WorkerId != Guid.Empty && value.OutputState is "NotApplicable" or "NotStarted" or "NotRetained" or "Incomplete" or "CleanupUnconfirmed" or "Available"
                && (value.OutputState == "Available") == (value.Output is not null)
                && (value.OutputState is "NotApplicable" or "NotStarted" ? value.WorkerId is null : value.WorkerId is not null && value.Generation > 0)
                && (value.OutputState != "NotStarted" || value.Generation == 0));
            if (value.Output is { } output)
            {
                ValidateOutputIdentity(output, id);
                Require(output.Job.JobId == value.JobId && output.Job.Generation == value.Generation && output.WorkerId == value.WorkerId);
            }
            var reply = new ToolReply("scan-output-manifest", null, ScreenReaderSession.Profile, value);
            return Fits(reply) ? reply : ToolReply.Error("HOSTED_SCAN_OUTPUT_TOO_LARGE");
        }, token);
    }

    public Task<ToolReply> OutputPageAsync(string scanId, string bundleSha256, int offset = 0, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_ID_REQUIRED"));
        if (bundleSha256 is not { Length: 64 } || !Digest(bundleSha256)) return Task.FromResult(ToolReply.Error("OUTPUT_IDENTITY_INVALID"));
        if (offset is < 0 or > RetainedRecordingOutput.MaximumBytes) return Task.FromResult(ToolReply.Error("OUTPUT_PAGE_INVALID"));
        return LockedAsync(async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Post, Collection + $"/{id:D}/recorded-output/bytes", new { bundleSha256, offset }).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); var page = Parse<RetainedRecordingOutputPage>(document);
            var output = page.Output ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); ValidateOutputIdentity(output, id);
            Require(output.BundleSha256 == bundleSha256 && page.Offset == offset && offset <= output.Bytes
                && (offset == output.Bytes || offset % RetainedRecordingOutputPage.PageBytes == 0)
                && page.Data is not null && page.Data.Length == Math.Min(RetainedRecordingOutputPage.PageBytes, output.Bytes - offset)
                && page.PageSha256 is { Length: 64 } && Digest(page.PageSha256)
                && Convert.ToHexStringLower(SHA256.HashData(page.Data)) == page.PageSha256);
            var next = offset + page.Data!.Length;
            Require(page.NextOffset == (next < output.Bytes ? next : null));
            if (offset == 0 && next == output.Bytes) Require(page.PageSha256 == bundleSha256);
            var reply = new ToolReply("scan-output-file", null, ScreenReaderSession.Profile, page);
            return Fits(reply) ? reply : ToolReply.Error("HOSTED_SCAN_OUTPUT_TOO_LARGE");
        }, token);
    }

    private void ValidateOutputIdentity(RetainedRecordingOutput output, Guid scan)
    {
        try { output.Validate(); }
        catch (BrowserOperationException) { Require(false); }
        Require(output.Job.OrganizationId == _options.Organization && output.Job.ScanId == scan);
    }
}
