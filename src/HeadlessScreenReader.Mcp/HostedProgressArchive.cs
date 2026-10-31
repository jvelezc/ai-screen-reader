using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    private static readonly JsonSerializerOptions ProgressJson = new(HostedReaderTransport.Json)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    private sealed record HostedProgressGraph(HostedScanProgress Status, RetainedProgressGraph? Graph);

    public Task<ToolReply> ProgressGraphAsync(string scanId, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_ID_REQUIRED"));
        return LockedAsync(async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Get, Collection + $"/{id:D}/progress/graph").ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var value = document.RootElement.Deserialize<HostedProgressGraph>(ProgressJson) ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
            ValidateProgressStatus(value.Status, id);
            Require((value.Status.Progress is null) == (value.Graph is null));
            if (value.Graph is { } graph)
            {
                try { graph.Validate(); }
                catch (BrowserOperationException) { Require(false); }
                Require(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(graph.Progress, ProgressJson),
                    JsonSerializer.SerializeToElement(value.Status.Progress, ProgressJson)));
            }
            var reply = new ToolReply("scan-progress-graph", null, ScreenReaderSession.Profile, value);
            return Fits(reply) ? reply : ToolReply.Error("HOSTED_SCAN_PROGRESS_TOO_LARGE");
        }, token);
    }

    public Task<ToolReply> CheckpointAsync(string scanId, string prefixSha256, int sequence, string checkpointSha256,
        int offset = 0, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_ID_REQUIRED"));
        if (prefixSha256 is null || checkpointSha256 is null || !Digest(prefixSha256) || !Digest(checkpointSha256))
            return Task.FromResult(ToolReply.Error("PROGRESS_IDENTITY_INVALID"));
        if (sequence is < 1 or > 9 || offset is < 0 or > 1048576) return Task.FromResult(ToolReply.Error("PROGRESS_PAGE_INVALID"));
        return LockedAsync(async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Post, Collection + $"/{id:D}/progress/checkpoint",
                new { prefixSha256, sequence, offset }).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var page = document.RootElement.Deserialize<RetainedCheckpointPage>(ProgressJson) ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
            ValidateProgressIdentity(page.Progress, id);
            Require(page.Progress.PrefixSha256 == prefixSha256 && page.Sequence == sequence && sequence <= page.Progress.Records
                && page.CheckpointSha256 == checkpointSha256 && Digest(page.SourceSha256)
                && (sequence != page.Progress.Records || page.SourceSha256 == prefixSha256)
                && page.FileBytes is > 0 and <= 1048576 && page.Offset == offset && offset <= page.FileBytes
                && (offset == page.FileBytes || offset % RetainedCheckpointPage.PageBytes == 0)
                && page.Data is not null && page.Data.Length == Math.Min(RetainedCheckpointPage.PageBytes, page.FileBytes - offset)
                && Digest(page.PageSha256) && Convert.ToHexStringLower(SHA256.HashData(page.Data)) == page.PageSha256);
            var next = offset + page.Data!.Length;
            Require(page.NextOffset == (next < page.FileBytes ? next : null));
            if (offset == 0 && next == page.FileBytes) Require(page.PageSha256 == checkpointSha256);
            var reply = new ToolReply("scan-checkpoint", null, ScreenReaderSession.Profile, page);
            return Fits(reply) ? reply : ToolReply.Error("HOSTED_SCAN_PROGRESS_TOO_LARGE");
        }, token);
    }

    private void ValidateProgressIdentity(RetainedCommandProgress progress, Guid scan)
    {
        Require(progress is not null);
        try { progress!.Validate(); }
        catch (BrowserOperationException) { Require(false); }
        Require(progress!.Job.OrganizationId == _options.Organization && progress.Job.ScanId == scan);
    }

    private void ValidateProgressStatus(HostedScanProgress value, Guid scan)
    {
        Require(value is not null); ValidateScan(value!.Scan);
        Require(value.Scan.Id == scan && value.JobId != Guid.Empty && value.Generation is >= 0 and <= 3 && value.WorkerId != Guid.Empty
            && value.NextAction is { Length: > 0 and <= 1024 } && !value.NextAction.Any(char.IsControl));
        Require(value.Generation != 0 || value.Scan.State is "queued" or "blocked");
        if (value.Progress is { } progress)
        {
            ValidateProgressIdentity(progress, scan);
            Require(value.ProgressState == "Available" && value.Scan.State != "queued" && progress.Job.JobId == value.JobId
                && progress.Job.Generation == value.Generation && progress.WorkerId == value.WorkerId);
        }
        else Require(value.ProgressState == "NotApplicable" && value.WorkerId is null
            || value.ProgressState == "NotStarted" && value.Generation == 0 && value.WorkerId is null
            || value.ProgressState == "NotRetained" && value.Scan.State != "queued" && value.Generation > 0 && value.WorkerId is not null);
    }
}
