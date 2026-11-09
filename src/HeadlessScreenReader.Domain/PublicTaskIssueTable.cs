using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HeadlessScreenReader.Domain;

/// <summary>Observed technical issue and a troubleshooting action; never a barrier or a claimed root-cause diagnosis.</summary>
public sealed record PublicTaskIssue(string CaseId, string Source, int Attempt, int Sequence, int RequestId,
    DateTimeOffset OccurredAt, string Method, string ResourceType, string? Origin, int? Status, string Code,
    string Observation, string NextAction, string EvidencePointer)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InputEventSequence { get; init; }
}

/// <summary>Bounded issue events derived only from the same original report. Missing/truncated traces are explicit.</summary>
public sealed record PublicTaskIssueTable(int KnownIssueEvents, int DroppedTraceEvents, int MissingTraces,
    int ExpectedIsolationProbeDenials, bool RowsTruncated, ImmutableArray<PublicTaskIssue> Issues)
{
    public static PublicTaskIssueTable From(PublicTaskReport report)
    {
        report.Validate(report.Start);
        var rows = new List<PublicTaskIssue>(); var dropped = 0; var missing = 0; var probes = 0;
        foreach (var attempt in report.Attempts)
        {
            dropped += attempt.OmittedFailures;
            if (attempt.Failures is { } failures) for (var i = 0; i < failures.Length; i++)
            {
                var f = failures[i];
                rows.Add(new("PSR-COLLECTOR-001", "Collector", attempt.Number, i + 1, 0, f.OccurredAt,
                    f.Stage.ToUpperInvariant(), "collector", PublicTaskNetworkTrace.OriginOf(report.Start.Job.Source.TargetUrl), null,
                    f.Code ?? "COLLECTOR_" + f.Category.ToUpperInvariant(), $"A {f.Category} failure occurred during {f.Stage}.",
                    f.Stage == "Recording" ? "Inspect the original failure's first-party source frames and capture-stop/browser-close/media-decoder records. Preserve input history; do not repeat input to repair recordings."
                    : "Inspect the original failure's first-party method/source line, related input event and network trace using the recorded collector fingerprint. Treat an unconfirmed cause as a hypothesis.",
                    $"/Attempts/{attempt.Number - 1}/Failures/{i}")
                { InputEventSequence = f.EventSequence });
            }
            Add(attempt.Network, "Browser", attempt.Number, $"/Attempts/{attempt.Number - 1}/Network/Events", 0);
        }
        if (report.Goal is null) Add(report.DiscoveryNetwork, "Discovery", 0, "/DiscoveryNetwork/Events", 0);
        Add(report.Egress, "Egress", 0, "/Egress/Events", report.EgressIsolationProbeThroughSequence ?? 0);
        return new(rows.Count, dropped, missing, probes, rows.Count > 32, rows.Take(32).ToImmutableArray());

        void Add(PublicTaskNetworkTrace? trace, string source, int attempt, string pointer, int expectedThrough)
        {
            if (trace is null) { missing++; return; }
            dropped += trace.DroppedEvents;
            for (var i = 0; i < trace.Events.Length; i++)
            {
                var e = trace.Events[i];
                if (e.Sequence <= expectedThrough)
                { if (e.Phase == "PolicyDenied") probes++; continue; }
                if (e.Code is null && e.Phase != "WebSocketRequested") continue;
                var issue = e.Code is null ? e with { Code = "WEBSOCKET_OUTCOME_UNCONFIRMED" } : e;
                var (id, observation, action) = Explain(issue);
                rows.Add(new(id, source, attempt, e.Sequence, e.RequestId, trace.StartedAt.AddMilliseconds(e.ElapsedMilliseconds),
                    e.Method, e.ResourceType, e.Origin, e.Status, issue.Code!, observation, action, pointer + "/" + i));
            }
        }
    }

    private static (string Id, string Observed, string Action) Explain(PublicTaskNetworkEvent e) => e.Code switch
    {
        "HTTP_ERROR" => ("PSR-HTTP-001", $"The website server returned HTTP {e.Status}.",
            "Check the endpoint's anonymous-access requirements and server logs at this timestamp. An HTTP error is not a transport or accessibility finding."),
        "DNS_ADDRESS_DENIED" => ("PSR-EGRESS-001", "The independent proxy rejected an empty DNS answer or an answer containing private/reserved addresses.",
            "Inspect the destination's DNS answers, including mixed public/private records. Correct DNS or the approved environment; retain the private-address fence."),
        "DNS_NOT_FOUND" => ("PSR-EGRESS-002", "The destination did not resolve through the worker's DNS resolver.",
            "Check DNS propagation and resolver availability for this origin, then create a separate authorized retry."),
        "DNS_IPV4_REQUIRED" => ("PSR-EGRESS-003", "No IPv4 address was available to the current proxy.",
            "Verify whether the destination is IPv6-only and configure/test proxy IPv6 egress before enabling it."),
        "TUNNEL_BYTE_LIMIT" => ("PSR-EGRESS-004", "The independent TLS tunnel exceeded its byte budget.",
            "Identify the large resource using the browser trace. Measure an isolated larger budget before changing the declared limit."),
        "DOCUMENT_METHOD_DENIED" or "DOCUMENT_URL_DENIED" => ("PSR-POLICY-001", "The requested document navigation was outside this task's declared authority.",
            "Inspect the selected link and any redirect. Use an explicitly authorized task profile for a different host or a submitted form."),
        "DEPENDENCY_URL_DENIED" or "WEBSOCKET_URL_DENIED" or "DEPENDENCY_URL_UNSUPPORTED" => ("PSR-POLICY-002", "The dependency did not meet the public secure-URL policy.",
            "Inspect its scheme, port and destination DNS. Public HTTPS/WSS dependencies need no per-host allowlist in this profile."),
        "TARGET_HOST_NOT_APPROVED" => ("PSR-NET-001", "An exact-host proxy rejected a third-party destination.",
            "Check that the browser and independent proxy both use the admitted public-dependency profile; do not alter an older original."),
        "ERR_ABORTED" => ("PSR-NET-004", "The browser cancelled this request.",
            "Compare its timestamp with navigation and browser shutdown. Cancellation alone does not establish a website outage or barrier."),
        "WEBSOCKET_OUTCOME_UNCONFIRMED" => ("PSR-NET-005", "A secure WebSocket connection was requested; this trace does not establish its handshake or message exchange.",
            "Inspect the original connection trace and the destination's WebSocket service logs. A failed task cannot become a proven barrier while this dependency remains unconfirmed."),
        _ => ("PSR-NET-003", $"The {e.ResourceType} request reported {e.Code}.",
            "Join this origin and timestamp with the independent proxy trace and website server logs. Preserve the exact code and treat an unconfirmed cause as a hypothesis.")
    };
}
