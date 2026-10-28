using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

/// <summary>Consumes durable core jobs; disconnect never cancels or claims ownership of their execution.</summary>
public sealed class HostedScanClient : IAsyncDisposable
{
    private readonly HostedReaderOptions _options;
    private readonly HostedReaderTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private string Collection => $"/api/workspaces/{_options.Organization:D}/scans";

    public HostedScanClient(HostedReaderOptions options, HttpMessageHandler? transport = null)
    { _options = options; _transport = new(options, transport); }

    public Task<ToolReply> StartFixtureAsync(string requestId, CancellationToken token = default, bool recordObservation = false)
    {
        if (!Guid.TryParse(requestId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_REQUEST_ID_REQUIRED"));
        return LockedAsync(async () =>
        {
            object request = recordObservation ? new { requestId = id, recordObservation = true } : new { requestId = id };
            using var document = await _transport.SendAsync(HttpMethod.Post, Collection, request, 201).ConfigureAwait(false);
            var receipt = Parse<HostedScanAdmission>(document);
            Require(receipt.OrganizationId == _options.Organization && receipt.ScanId != Guid.Empty && receipt.JobId != Guid.Empty);
            return new("scan-admitted", null, ScreenReaderSession.Profile, new
            {
                RequestId = id,
                receipt.OrganizationId,
                receipt.ScanId,
                receipt.JobId,
                Execution = "NotEstablishedByAdmission",
                Evidence = "NotEstablishedByAdmission",
                GoalVerification = "NotAssessed",
                RecordingRequested = recordObservation ? "PassiveObservation" : "NotRequested",
                WorkerContinuesAfterDisconnect = true,
                NextAction = "Read scan_report using scanId. An admission receipt does not establish execution or available evidence."
            });
        }, token, id);
    }

    public Task<ToolReply> ListAsync(CancellationToken token = default) => LockedAsync(async () =>
    {
        using var document = await _transport.SendAsync(HttpMethod.Get, Collection).ConfigureAwait(false);
        var result = Parse<HostedScanList>(document);
        Require(!result.Items.IsDefault && result.Items.Length <= 50 && (!result.HasMore || result.Items.Length == 50)
            && result.Items.All(item => item is not null)
            && result.Items.Select(item => item.Id).Distinct().Count() == result.Items.Length);
        foreach (var scan in result.Items) ValidateScan(scan);
        return new("scan-list", null, ScreenReaderSession.Profile, result);
    }, token);

    public Task<ToolReply> ReportAsync(string scanId, int offset = 0, int limit = 80,
        string? reportId = null, CancellationToken token = default)
    {
        if (!Guid.TryParse(scanId, out var id) || id == Guid.Empty) return Task.FromResult(ToolReply.Error("SCAN_ID_REQUIRED"));
        if (offset is < 0 or > 2048 || limit is < 1 or > 100) return Task.FromResult(ToolReply.Error("PAGE_BOUNDS_INVALID"));
        if (reportId is not null && !Digest(reportId)) return Task.FromResult(ToolReply.Error("REPORT_ID_INVALID"));
        if (offset > 0 && reportId is null) return Task.FromResult(ToolReply.Error("REPORT_ID_REQUIRED"));
        return LockedAsync(async () =>
        {
            // Every page rereads the authorized report; no cache can outlive upstream revocation.
            using var document = await _transport.SendAsync(HttpMethod.Get, Collection + $"/{id:D}").ConfigureAwait(false);
            var report = Parse<HostedScanReport>(document); ValidateScan(report.Scan);
            Require(report.Scan.Id == id && SafeCode(report.ErrorCode));
            if (report.WorkerArchive is { } producer)
            {
                Require(report.Scan.State == "observed" && producer.Job is not null
                    && producer.Job.OrganizationId == report.Scan.OrganizationId && producer.Job.ScanId == id);
                try { producer.Validate(producer.Job!); }
                catch (BrowserOperationException) { Require(false); }
            }
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document.RootElement.GetRawText())));
            if (reportId is not null && reportId != fingerprint) return ToolReply.Error("HOSTED_SCAN_REPORT_CHANGED");
            if (report.EvidenceState == "Available")
            {
                Require(report.Scan.State == "observed" && report.ErrorCode is null && report.Observation is not null);
                var observation = report.Observation!;
                Require(!string.IsNullOrWhiteSpace(observation.DocumentId) && Digest(observation.Digest)
                    && observation.ReaderProfile == ScreenReaderSession.Profile && !observation.Nodes.IsDefault && observation.Nodes.Length <= 2048
                    && !observation.Limitations.IsDefault && observation.EvidenceStorage is "S3" or "Local" or "LocalFallback"
                    && observation.Nodes.All(node => node is not null && !string.IsNullOrWhiteSpace(node.Id))
                    && observation.Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() == observation.Nodes.Length);
                if (observation.DispatchedInputs != 0 || observation.GoalVerified)
                    return new("error", null, ScreenReaderSession.Profile, new
                    { ScanId = id, observation.DispatchedInputs, observation.GoalVerified }, "HOSTED_SCAN_READ_ONLY_VIOLATION");
                if (offset > observation.Nodes.Length) return ToolReply.Error("PAGE_BOUNDS_INVALID");
                ToolReply Page(int count) => new("scan-report", null, ScreenReaderSession.Profile, new
                {
                    report.Scan,
                    report.EvidenceState,
                    report.ErrorCode,
                    report.WorkerArchive,
                    ReportId = fingerprint,
                    ReportIdMeaning = "SHA256 of core response for immutable pagination; separate from original observation digest",
                    EvidenceAuthority = "Core API validates original job-bound evidence",
                    Observation = observation with { Nodes = observation.Nodes.Skip(offset).Take(count).ToImmutableArray() },
                    Offset = offset,
                    TotalNodes = observation.Nodes.Length,
                    ReturnedNodes = count,
                    HasMore = offset + count < observation.Nodes.Length,
                    NextOffset = offset + count
                });
                return FitPage(Page, Math.Min(limit, observation.Nodes.Length - offset));
            }
            Require(report.Observation is null && (report.EvidenceState switch
            {
                "Pending" => report.Scan.State is "queued" or "leased",
                "NotCollected" => report.Scan.State == "blocked",
                "Invalid" => report.Scan.State == "observed" && report.ErrorCode is not null,
                "Unavailable" => report.ErrorCode is not null,
                _ => false
            }));
            if (offset != 0) return ToolReply.Error("PAGE_BOUNDS_INVALID");
            var error = report.EvidenceState is "Invalid" or "Unavailable";
            return new(error ? "error" : "scan-report", null, ScreenReaderSession.Profile, new
            {
                report.Scan,
                report.EvidenceState,
                report.ErrorCode,
                report.WorkerArchive,
                ReportId = fingerprint,
                Observation = (object?)null,
                GoalVerification = "NotAssessed",
                NextAction = report.EvidenceState == "Pending" ? "Poll scan_report explicitly; do not create another request ID."
                    : "Inspect the evidence/engine state and support code; it is not an accessibility barrier or a verified objective."
            }, error ? "HOSTED_SCAN_EVIDENCE_" + report.EvidenceState.ToUpperInvariant() : null);
        }, token);
    }

    private void ValidateScan(HostedScan scan) => Require(scan.OrganizationId == _options.Organization && scan.Id != Guid.Empty
        && scan.Fixture == "synthetic-shop" && scan.ReaderProfile == ScreenReaderSession.Profile
        && scan.State is "queued" or "leased" or "observed" or "blocked");
    private static bool Digest(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);
    private static bool SafeCode(string? code) => code is null || code is { Length: > 0 and <= 128 } && code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    private static void Require(bool condition) { if (!condition) throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }
    private static T Parse<T>(JsonDocument document) => document.RootElement.Deserialize<T>(HostedReaderTransport.Json)
        ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
    private static bool Fits(ToolReply reply) => JsonSerializer.SerializeToUtf8Bytes(reply, ToolReply.JsonOptions).Length <= 131072;
    private static ToolReply FitPage(Func<int, ToolReply> page, int maximum)
    {
        var complete = page(maximum); if (Fits(complete)) return complete;
        var lower = 1; var upper = maximum - 1; ToolReply? fitting = null;
        while (lower <= upper)
        {
            var count = lower + (upper - lower) / 2; var candidate = page(count);
            if (Fits(candidate)) { fitting = candidate; lower = count + 1; } else upper = count - 1;
        }
        return fitting ?? ToolReply.Error("HOSTED_SCAN_REPORT_ENTRY_TOO_LARGE");
    }

    private async Task<ToolReply> LockedAsync(Func<Task<ToolReply>> operation, CancellationToken token, Guid? requestId = null)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_disposed) return ToolReply.Error("WORKSPACE_CLOSED");
            token.ThrowIfCancellationRequested();
            return await operation().ConfigureAwait(false);
        }
        catch (HostedReaderFault fault)
        { return new("error", null, ScreenReaderSession.Profile, new { RequestId = requestId, Upstream = fault.Facts, AutomaticRetry = false, RetryRequestId = requestId }, fault.Code); }
        catch (Exception fault) when (fault is HttpRequestException or OperationCanceledException or JsonException
            or InvalidOperationException or NotSupportedException or IOException)
        { return new("error", null, ScreenReaderSession.Profile, new { RequestId = requestId, AutomaticRetry = false, RetryRequestId = requestId }, "HOSTED_SCAN_OPERATION_UNCONFIRMED"); }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { _disposed = true; _transport.Dispose(); } }
        finally { _gate.Release(); }
    }
}
