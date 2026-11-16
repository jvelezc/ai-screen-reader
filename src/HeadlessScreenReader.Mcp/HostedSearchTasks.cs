using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

internal sealed record HostedSearchFile(int Id, string Path, long Bytes, string Sha256, string MediaType);
internal sealed record HostedSearchTrialValue(int Trial, string Submission, string RunId, string Execution, string Reachability, int AttemptedKeys, int CompletedKeys);
internal sealed record HostedSearchTrial(HostedSearchTrialValue Trial, HostedSearchFile Video);
internal sealed record HostedSearchResult(SyntheticSearchTaskStatus Status, string IndexSha256, HostedSearchTrial[] KeyboardTrials,
    string PointerRunId, HostedSearchFile PointerVideo, string Observation, string ProducerSha256, string ProducerVersion,
    string Runtime, string Platform, string Viewport, int OriginalFiles, string Scope, string WebsiteEvidenceAssessment,
    string VideoAlignment, string ExternalAssistiveTechnologyComparison, bool LegalCaseDetermined);
internal sealed record HostedSearchFiles(SyntheticSearchTaskStatus Status, string IndexSha256, int Offset, int Total, HostedSearchFile[] Files, int? NextOffset);
internal sealed record HostedSearchOriginal(SyntheticSearchTaskStatus Status, string IndexSha256, HostedSearchFile File,
    int Offset, byte[] Data, string PageSha256, int? NextOffset);
internal sealed record HostedSearchPublicationAttempt(int Number, Guid ProducerId, Guid PublisherId, Guid ExecutionId,
    string IndexSha256, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string State, string? ErrorCode, bool WebsiteInputRetried);
internal sealed record HostedSearchDiagnostics(string Schema, Guid JobId, HostedSearchPublicationAttempt[] PublicationAttempts);

public sealed partial class HostedScanClient
{
    public Task<ToolReply> SearchTaskAsync(string operation, string? jobId = null, string? requestId = null,
        string? sourceId = null, string? digest = null, int offset = 0, int fileId = 0, CancellationToken token = default)
    {
        Guid job = default, request = default;
        if (operation is not ("start" or "latest" or "status" or "cancel" or "result" or "files" or "original" or "diagnostics")
            || operation is not ("start" or "latest") && (!Guid.TryParse(jobId, out job) || job == Guid.Empty)
            || operation == "start" && (!Guid.TryParse(requestId, out request) || request == Guid.Empty || sourceId is not ("search-working" or "search-keyboard-blocked"))
            || operation is "files" or "original" && (digest is null || !Digest(digest) || offset < 0)
            || operation == "files" && (offset > 1399 || offset % 24 != 0)
            || operation == "original" && (fileId is < 0 or > 1399 || offset > 26 * 1024 * 1024))
            return Task.FromResult(new ToolReply("error", null, SyntheticSearchTaskJob.CurrentProfile, ErrorCode: "SEARCH_TASK_REQUEST_INVALID"));
        return LockedAsync(async () =>
        {
            var path = $"/api/workspaces/{_options.Organization:D}/search-demonstrations";
            if (job != Guid.Empty) path += $"/{job:D}";
            path += operation switch
            {
                "cancel" => "/cancel",
                "result" => "/result",
                "diagnostics" => "/diagnostics",
                "files" => $"/files/{offset}/{digest}",
                "original" => $"/originals/{fileId}/{offset}/{digest}",
                _ => ""
            };
            var method = operation is "start" or "cancel" ? HttpMethod.Post : HttpMethod.Get;
            using var document = await _transport.SendAsync(method, path,
                operation == "start" ? new { requestId = request, sourceId } : null, operation == "start" ? 202 : 200).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var raw = document.RootElement;
            if (raw.ValueKind == JsonValueKind.Null) Require(operation is "latest" or "status");
            else if (operation == "diagnostics")
            {
                var diagnostics = SearchValue<HostedSearchDiagnostics>(raw);
                Require(diagnostics.Schema == "hsr-search-task-diagnostics-v1" && diagnostics.JobId == job && diagnostics.PublicationAttempts is { Length: <= 3 });
                for (var number = 0; number < diagnostics.PublicationAttempts.Length; number++)
                {
                    var attempt = diagnostics.PublicationAttempts[number];
                    Require(attempt is not null && attempt.Number == number + 1 && attempt.ProducerId != Guid.Empty
                        && attempt.PublisherId != Guid.Empty && attempt.ExecutionId != Guid.Empty && Digest(attempt.IndexSha256)
                        && !attempt.WebsiteInputRetried && attempt.StartedAt != default && attempt.State is "Publishing" or "Published" or "Blocked" or "Cancelled"
                        && (attempt.State == "Publishing" ? attempt.FinishedAt is null && attempt.ErrorCode is null : attempt.FinishedAt >= attempt.StartedAt)
                        && (attempt.State == "Published" ? attempt.ErrorCode is null : attempt.State is not ("Blocked" or "Cancelled") || attempt.ErrorCode is not null)
                        && attempt.ErrorCode is null or "SEARCH_TASK_PUBLICATION_UNCONFIRMED" or "SEARCH_TASK_PUBLICATION_INVALID"
                            or "SEARCH_TASK_AUTHORITY_LOST" or "SEARCH_TASK_CANCELLED" or "SEARCH_TASK_PUBLICATION_LEASE_EXPIRED");
                }
            }
            else
            {
                var status = SearchValue<SyntheticSearchTaskStatus>(operation is "start" or "latest" or "status" or "cancel" ? raw : raw.GetProperty("status"));
                try { status.Validate(_options.Organization, job == Guid.Empty ? null : job); }
                catch (BrowserOperationException) { Require(false); }
                if (operation == "start") Require(status.RequestId == request && status.Job.Source.Id == sourceId);
                if (operation is "result" or "files" or "original")
                    Require(status.State == "completed" && status.Publication is not null && raw.GetProperty("indexSha256").GetString() == status.Publication.IndexSha256);
                if (operation == "result")
                {
                    var result = SearchValue<HostedSearchResult>(raw);
                    Require(result.KeyboardTrials is { Length: 4 } && result.Observation == status.Publication!.Observation
                        && result.Scope == "ControlledEmbeddedSearchTaskExperiments" && result.WebsiteEvidenceAssessment == "NotAssessed"
                        && result.VideoAlignment == "UnverifiedWithoutCalibrationMarkers" && result.ExternalAssistiveTechnologyComparison == "NotPerformed"
                        && !result.LegalCaseDetermined && result.OriginalFiles == status.Publication.Files + 1
                        && Digest(result.ProducerSha256) && result.Viewport == "320x240"
                        && Guid.TryParseExact(result.PointerRunId, "N", out var pointer) && pointer != Guid.Empty);
                    ValidateSearchFile(result.PointerVideo); Require(result.PointerVideo.Path == "pointer-media/original.webm");
                    for (var number = 0; number < result.KeyboardTrials.Length; number++)
                    {
                        var trial = result.KeyboardTrials[number] ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
                        var expected = number < 2 ? "FieldEnter" : number == 2 ? "ButtonEnter" : "ButtonSpace";
                        Require(trial.Trial.Trial == number + 1 && trial.Trial.Submission == expected
                            && trial.Trial.Execution == "Completed" && trial.Trial.Reachability is "Reached" or "Unknown"
                            && trial.Trial.AttemptedKeys == (number < 2 ? 15 : 16) && trial.Trial.CompletedKeys == trial.Trial.AttemptedKeys
                            && Guid.TryParseExact(trial.Trial.RunId, "N", out var run) && run != Guid.Empty);
                        ValidateSearchFile(trial.Video); Require(trial.Video.Path == $"route-{number + 1}-media/original.webm");
                    }
                    Require(result.KeyboardTrials.Select(t => t.Trial.RunId).Append(result.PointerRunId).Distinct(StringComparer.Ordinal).Count() == 5);
                }
                if (operation == "files")
                {
                    var page = SearchValue<HostedSearchFiles>(raw);
                    Require(page.IndexSha256 == digest && page.Offset == offset && page.Total == status.Publication!.Files + 1
                        && page.Files is { Length: > 0 and <= 24 } && page.Files.Length == Math.Min(24, page.Total - offset)
                        && page.NextOffset == (offset + page.Files.Length < page.Total ? offset + page.Files.Length : null));
                    for (var number = 0; number < page.Files.Length; number++)
                    { ValidateSearchFile(page.Files[number]); Require(page.Files[number].Id == offset + number); }
                    Require(page.Files.Select(f => f.Path).SequenceEqual(page.Files.Select(f => f.Path).Order(StringComparer.Ordinal)));
                }
                if (operation == "original")
                {
                    var page = SearchValue<HostedSearchOriginal>(raw); ValidateSearchFile(page.File);
                    Require(page.IndexSha256 == digest && page.File.Id == fileId && page.Offset == offset && offset <= page.File.Bytes
                        && (offset % 65536 == 0 || offset == page.File.Bytes) && page.Data.Length == Math.Min(65536, page.File.Bytes - offset)
                        && Convert.ToHexStringLower(SHA256.HashData(page.Data)) == page.PageSha256
                        && page.NextOffset == (offset + page.Data.Length < page.File.Bytes ? offset + page.Data.Length : null));
                }
            }
            var reply = new ToolReply("search-demonstration-" + operation, null, SyntheticSearchTaskJob.CurrentProfile, raw.Clone());
            return Fits(reply) ? reply : new("error", null, SyntheticSearchTaskJob.CurrentProfile, ErrorCode: "SEARCH_TASK_PAGE_TOO_LARGE");
        }, token, operation == "start" ? request : null);
    }

    private static T SearchValue<T>(JsonElement raw) where T : class
    {
        var value = raw.Deserialize<T>(HostedReaderTransport.Json);
        Require(value is not null && JsonElement.DeepEquals(raw, JsonSerializer.SerializeToElement(value, HostedReaderTransport.Json)));
        return value!;
    }
    private static void ValidateSearchFile(HostedSearchFile file) => Require(file is not null && file.Id is >= 0 and <= 1399
        && file.Bytes is > 0 and <= 26 * 1024 * 1024 && Digest(file.Sha256) && file.Path is { Length: > 0 and <= 500 }
        && !file.Path.Any(char.IsControl) && !file.Path.Contains('\\') && !file.Path.StartsWith('/')
        && file.Path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."))
        && file.MediaType is "video/webm" or "application/json" or "image/jpeg" or "text/html" or "text/plain");
}
