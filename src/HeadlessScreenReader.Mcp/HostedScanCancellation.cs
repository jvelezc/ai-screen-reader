using System.Text.Json.Serialization;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedScanClient
{
    /// <summary>Requests cancellation of an existing run; disconnect alone never cancels a run.</summary>
    public Task<ToolReply> CancelAsync(string scanId, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_ID_REQUIRED"));
        return LockedAsync(async () =>
        {
            var response = await _transport.SendResponseAsync(HttpMethod.Post, Collection + $"/{id:D}/cancellation", new { }, 202, 200).ConfigureAwait(false);
            using var document = response.Document;
            token.ThrowIfCancellationRequested();
            var value = Parse<HostedScanCancellation>(document);
            Require(value.OrganizationId == _options.Organization && value.ScanId == id && value.JobId != Guid.Empty
                && value.Generation is >= 0 and <= 3 && value.Producer != Guid.Empty
                && (value.Generation == 0) == (value.Producer is null)
                && value.InputAssessment == (value.Generation == 0 ? "NoAttemptStarted" : "NotAssessed")
                && value.Scope == "AdmittedRunCancellation" && value.NativeCleanup == "NotAssessed"
                && value.GoalVerification == "NotAssessed" && !value.IsCompletionReceipt && !value.CanReplay
                && (response.StatusCode == 202 && value.Status == "Requested" && value.RequestedAt > DateTimeOffset.UnixEpoch
                    || response.StatusCode == 200 && value.Status == "AlreadyCompleted" && value.RequestedAt is null && value.Generation > 0));
            return new("scan-cancellation", null, ScreenReaderSession.Profile, value);
        }, token);
    }

    private sealed record HostedScanCancellation(Guid OrganizationId, Guid ScanId, Guid JobId,
        [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Generation,
        Guid? Producer, string Status, string InputAssessment, DateTimeOffset? RequestedAt,
        string Scope, string NativeCleanup, string GoalVerification, bool IsCompletionReceipt, bool CanReplay);
}
