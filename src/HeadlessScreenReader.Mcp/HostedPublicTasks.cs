using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    public Task<ToolReply> PublicTaskAsync(string operation, string scanId, string? jobId = null, string? requestId = null,
        string? digest = null, int attempt = 1, int offset = 0, string? file = null, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var parent) || parent == Guid.Empty || offset < 0
            || operation is not ("start" or "latest" or "status" or "cancel" or "result" or "events" or "graph" or "original"))
            return Task.FromResult(new ToolReply("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_TASK_REQUEST_INVALID"));
        Guid job = default, request = default;
        if (operation is not ("start" or "latest") && (!Guid.TryParse(jobId, out job) || job == Guid.Empty)
            || operation == "start" && (!Guid.TryParse(requestId, out request) || request == Guid.Empty || !Digest(digest ?? ""))
            || operation is "events" or "graph" or "original" && !Digest(digest ?? "")
            || operation is "events" or "graph" && (attempt is < 1 or > 3 || offset > 100)
            || operation == "original" && (!PublicTaskRules.FileName(file) || offset > PublicTaskReport.MaximumArchiveBytes))
            return Task.FromResult(new ToolReply("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_TASK_REQUEST_INVALID"));
        return LockedAsync(async () =>
        {
            var path = PublicCollection + $"/{parent:D}/tasks";
            if (job != Guid.Empty) path += $"/{job:D}";
            path += operation switch
            {
                "cancel" => "/cancel",
                "result" => "/result/diagnostics",
                "events" or "graph" => $"/{operation}/{attempt}/{offset}/{digest}",
                "original" => $"/originals/{file}/{offset}/{digest}",
                _ => ""
            };
            var method = operation is "start" or "cancel" ? HttpMethod.Post : HttpMethod.Get;
            object? body = operation == "start" ? new { requestId = request, parentOriginalSha256 = digest } : null;
            using var document = await _transport.SendAsync(method, path, body, operation == "start" ? 202 : 200).ConfigureAwait(false);
            var raw = document.RootElement;
            var wrapped = operation is "result" or "events" or "graph" or "original";
            var state = wrapped ? raw.GetProperty("status") : raw;
            if (state.ValueKind == JsonValueKind.Null) Require(operation is "latest" or "status");
            else
            {
                var status = state.Deserialize<PublicTaskStatus>(HostedReaderTransport.Json) ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
                status.Validate(_options.Organization, parent, job == Guid.Empty ? null : job);
                if (operation == "start") Require(status.Job.RequestId == request && status.Job.ParentOriginalSha256 == digest);
                if (wrapped) Require(status.State == "completed");
                if (operation is "events" or "graph" or "original") Require(status.ReportSha256 == digest && raw.GetProperty("reportSha256").GetString() == digest
                    && raw.GetProperty("offset").GetInt32() == offset);
                if (operation == "result") Require(!raw.GetProperty("legalCaseDetermined").GetBoolean()
                    && raw.GetProperty("scope").GetString() == "PublicServiceNavigationOnly"
                    && raw.GetProperty("videoAlignment").GetString() == "EventSeekingNotVerified"
                    && raw.GetProperty("assistiveTechnologyComparison").GetString() == "NotPerformed"
                    && JsonElement.DeepEquals(raw.GetProperty("assessment"), JsonSerializer.SerializeToElement(status.Publication!.Assessment, HostedReaderTransport.Json)));
                if (operation is "events" or "graph") Require(raw.GetProperty("attempt").GetInt32() == attempt);
                if (operation == "original")
                {
                    var original = raw.GetProperty("file").Deserialize<PublicTaskFile>(HostedReaderTransport.Json)!;
                    var bytes = raw.GetProperty("data").GetBytesFromBase64();
                    Require(original.Path == file && Digest(original.Sha256) && original.Bytes > 0 && bytes.Length <= 65536
                        && offset <= original.Bytes && bytes.Length == Math.Min(65536, original.Bytes - offset)
                        && Convert.ToHexStringLower(SHA256.HashData(bytes)) == raw.GetProperty("pageSha256").GetString());
                }
            }
            var reply = new ToolReply("public-task-" + operation, null, PublicTaskJob.Profile, raw.Clone());
            return Fits(reply) ? reply : new("error", null, PublicTaskJob.Profile, ErrorCode: "PUBLIC_TASK_PAGE_TOO_LARGE");
        }, token, operation == "start" ? request : null);
    }
}
