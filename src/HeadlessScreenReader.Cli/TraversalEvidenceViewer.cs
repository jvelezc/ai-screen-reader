using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>A self-contained local evidence viewer with no browser-control channel or external dependencies.</summary>
public static class TraversalEvidenceViewer
{
    public const int MaximumDocumentBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions DataOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private sealed record Item(string Id, string Kind, string Label, string Detail,
        ImmutableArray<string> EventIds, TraversalRecordingCue? Cue)
    {
        public string? Identity { get; init; }
    }

    public static string Render(TraversalReceipt receipt, int route, TraversalRecordingEvidence recording)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(route, 1);
        // Apply the same binding and declaration checks as the equivalent readable report.
        _ = TraversalReport.Render(receipt, route, recording);
        var graph = TraversalProjection.Build(receipt);
        var items = new List<Item>();
        TraversalRecordingCue? Cue(string id) => recording.Alignment?.Calibrated == true
            ? recording.Alignment.Cues.SingleOrDefault(c => c.EvidenceId == id) : null;
        foreach (var observation in receipt.Observations)
        {
            var node = graph.Nodes.Single(n => n.ObservationId == observation.Id);
            string Name(string? id)
            {
                var exposed = observation.Snapshot.Nodes.FirstOrDefault(n => n.Id == id);
                return id is null ? "none" : exposed is null ? "not retained" : exposed.Role + ": " + (exposed.Name.Length == 0 ? "(unnamed)" : exposed.Name);
            }
            items.Add(new(observation.Id, "Observation", $"{observation.Id}: {node.Label}",
                $"Mode {node.Mode}. Cursor {Name(node.CursorNodeId)}. Focus {Name(node.FocusedNodeId)}. "
                + $"Active descendant {Name(node.ActiveDescendantNodeId)}. This interval covers accessibility acquisition; it is not an atomic video frame.",
                [], Cue(observation.Id))
            {
                Identity = $"Document {node.DocumentId}. Cursor {node.CursorNodeId ?? "none"}. Focus {node.FocusedNodeId ?? "none"}. Active descendant {node.ActiveDescendantNodeId ?? "none"}."
            });
        }
        foreach (var attempt in receipt.Attempts)
        {
            var edge = graph.Edges.SingleOrDefault(e => e.AttemptId == attempt.Id);
            items.Add(new(attempt.Id, "Action", $"{attempt.Id}: {attempt.Instruction.Command}",
                $"Source {attempt.SourceObservationId}; actual result {attempt.ResultObservationId ?? "not reliably observed"}. "
                + $"Reader outcome {attempt.Step?.Outcome.ToString() ?? "Interrupted"}; goal verification {attempt.Step?.Verification.ToString() ?? "NotRequested"}. "
                + $"Browser keys: {(edge is null ? "see retained transcript; no reliable result state" : TraversalTraceDiagram.KeyLabel(edge))}. "
                + $"Completed calls {attempt.Step?.BrowserKeystrokes ?? 0}. "
                + (attempt.Step?.InputMayHaveOccurred == true ? "Input may have occurred despite an incomplete or uncertain result." : ""),
                [.. attempt.Events.Select(e => $"event-{e.Sequence}")], Cue(attempt.Id)));
        }
        foreach (var item in receipt.Timeline)
            items.Add(new($"event-{item.Sequence}", "Event", $"Event {item.Sequence}: {item.Kind}",
                $"Command {item.CommandSequence}: {item.Command}. {item.Detail}", [$"event-{item.Sequence}"], Cue($"event-{item.Sequence}")));
        var body = new StringBuilder();
        body.Append("<header><h1>").Append(H(receipt.Plan.Name)).Append("</h1><p class=outcome>")
            .Append(H($"Destination {receipt.Reachability} · Barrier {receipt.Barrier} · {receipt.StopReason}"))
            .Append("</p><p>Requested destination: ").Append(H(receipt.Plan.Destination.Role + ": " + receipt.Plan.Destination.Name))
            .Append("</p><p class=execution>").Append(H($"Execution {receipt.Execution} · Policy {receipt.Policy}. Engine/policy code: {receipt.ErrorCode ?? "None reported"}."))
            .Append("</p><p class=completeness>").Append(H($"Attempted keys: {receipt.AttemptedKeystrokes}; completed keyboard API calls: {receipt.CompletedKeystrokes}. "
                + $"Unexecuted route instructions: {receipt.UnexecutedInstructions}. Timeline truncated: {receipt.TimelineTruncated}."))
            .Append("</p><nav aria-label=Reports><a href=\"route-").Append(route).Append(".md\">Readable report</a><a href=\"route-")
            .Append(route).Append(".json\">Structured evidence and hashes</a><a href=\"route-").Append(route).Append(".svg\">Standalone trace</a></nav></header>")
            .Append("<p class=scope>Controlled synthetic fixture. Selecting evidence only seeks this recording. Input attempts, completed calls, observed state and verified goals remain separate.</p>")
            .Append("<main id=main><div class=workspace><section aria-labelledby=trace-title><h2 id=trace-title>Observed route</h2><p>Select a state or action to inspect its evidence. <a href=#selection-title>Go to selected evidence</a>.</p><ol class=trace>");
        foreach (var node in graph.Nodes)
        {
            var incoming = graph.Edges.LastOrDefault(e => e.ResultObservationId == node.ObservationId);
            var state = incoming?.Verification == VerificationStatus.Satisfied ? "verified" : incoming?.Outcome == ReaderOutcome.Blocked ? "barrier" : "observed";
            var stateText = state == "verified" ? "Objective verified here" : state == "barrier" ? "Disabled control observed" : "Observed state";
            body.Append("<li class=state><button type=button class=\"").Append(state).Append("\" data-evidence=\"").Append(H(node.ObservationId))
                .Append("\"><strong>").Append(H(node.ObservationId + ": " + node.Label)).Append("</strong><span>").Append(H(stateText)).Append("</span></button></li>");
            foreach (var attempt in receipt.Attempts.Where(a => a.SourceObservationId == node.ObservationId))
            {
                var edge = graph.Edges.SingleOrDefault(e => e.AttemptId == attempt.Id);
                body.Append("<li class=edge><span class=relationship>").Append(H(attempt.SourceObservationId + " → " + (attempt.ResultObservationId ?? "no reliable result node")))
                    .Append("</span><button type=button data-evidence=\"").Append(H(attempt.Id)).Append("\"><strong>")
                    .Append(H(attempt.Id + ": " + attempt.Instruction.Command)).Append("</strong><span>")
                    .Append(H(edge is null ? "Unresolved attempt; inspect transcript" : TraversalTraceDiagram.KeyLabel(edge)))
                    .Append("</span></button></li>");
            }
        }
        if (graph.Nodes.IsEmpty) body.Append("<li>No reliable observations were retained. Open the structured receipt for the failure stage.</li>");
        body.Append("</ol></section><section aria-label=\"Selected evidence and recording\">")
            .Append("<section aria-labelledby=selection-title><h2 id=selection-title>Selected evidence</h2><h3 id=selected-label>Select a state, action or event</h3>")
            .Append("<p id=selected-detail>The report preserves the observed result, including Unknown and unresolved attempts.</p><p id=selected-interval role=status>No evidence selected.</p>")
            .Append("<details id=identity-disclosure hidden><summary>Raw observation identities</summary><p id=selected-identity></p></details></section>")
            .Append("<h2 id=recording-title>Original recording</h2>");
        if (recording.Media is not null)
            body.Append("<video id=recording controls preload=auto aria-label=\"Original permitted route recording\" src=\"route-")
                .Append(route).Append("-media/original.webm\"></video><p id=media-status role=status>Loading the retained recording…</p>");
        else
            body.Append("<p id=media-status role=status>Recording unavailable. Stage: ").Append(H(recording.Stage)).Append(". Code: ")
                .Append(H(recording.ErrorCode ?? "Not finalized")).Append(". Inspect the structured evidence; partial files may remain in the route media directory.</p>");
        body.Append("<p class=timing>Timing: ").Append(H(recording.Alignment?.ReasonCode ?? recording.ErrorCode ?? "Unknown"))
            .Append(". Conservative uncertainty: ").Append(H(recording.Alignment?.UncertaintyMilliseconds?.ToString("F3", CultureInfo.InvariantCulture) ?? "Unknown"))
            .Append(" ms.</p></section></div>")
            .Append("<section aria-labelledby=timeline-title><h2 id=timeline-title>Reader timeline</h2><p id=timeline-help>Selections highlight related events or overlapping video intervals. Overlap does not establish causation. <a href=#selection-title>Go to selected evidence</a>.</p>")
            .Append("<div class=table-scroll><table><caption>Actual reader events; select an event to inspect its recording interval.</caption><thead><tr><th scope=col>Event</th><th scope=col>Elapsed ms</th><th scope=col>Kind</th><th scope=col>Detail</th></tr></thead><tbody>");
        foreach (var item in receipt.Timeline)
            body.Append("<tr data-timeline-event=\"event-").Append(item.Sequence).Append("\"><th scope=row><button type=button data-evidence=\"event-")
                .Append(item.Sequence).Append("\">Event ").Append(item.Sequence).Append("</button></th><td class=number>")
                .Append(item.ElapsedMilliseconds.ToString("F1", CultureInfo.InvariantCulture)).Append("</td><td>").Append(H(item.Kind.ToString()))
                .Append("</td><td>").Append(H(item.Detail)).Append("</td></tr>");
        body.Append("</tbody></table></div></section><section aria-labelledby=observations-title><h2 id=observations-title>Reader observations</h2>");
        foreach (var observation in receipt.Observations)
        {
            body.Append("<details><summary>").Append(H(observation.Id + ": exposed reader nodes")).Append("</summary><div class=table-scroll><table><caption>")
                .Append(H(observation.Id + ": observed semantics")).Append("</caption><thead><tr><th scope=col>Node</th><th scope=col>Role</th><th scope=col>Exposed name</th><th scope=col>Disabled</th><th scope=col>Selected</th></tr></thead><tbody>");
            foreach (var node in observation.Snapshot.Nodes)
                body.Append("<tr><th scope=row>").Append(H(node.Id)).Append("</th><td>").Append(H(node.Role)).Append("</td><td>")
                    .Append(H(node.Name.Length == 0 ? "(unnamed)" : node.Name)).Append("</td><td>").Append(node.IsDisabled)
                    .Append("</td><td>").Append(node.Selected?.ToString() ?? "Unknown").Append("</td></tr>");
            body.Append("</tbody></table></div></details>");
        }
        body.Append("</section><p class=scope>No path-complexity or accessibility-conformance score. Original media are unchanged. Diagnostics and future replay remain separate work.</p></main>");
        var css = Resource("viewer.css");
        var script = Resource("viewer.js");
        var json = JsonSerializer.Serialize(items, DataOptions);
        var document = Shell(receipt.Plan.Name, body.ToString(), css, script, json);
        if (Encoding.UTF8.GetByteCount(document) <= MaximumDocumentBytes) return document;
        return Shell("Evidence viewer display limit",
            $"<main id=main><h1>Evidence viewer display limit reached</h1><p>This route exceeds the local viewer's 4 MiB display bound. The original evidence remains available.</p><a href=\"route-{route}.md\">Open readable report</a> · <a href=\"route-{route}.json\">Open structured evidence</a></main>",
            css, "", "[]");
    }

    private static string Shell(string title, string body, string css, string script, string data) =>
        "<!doctype html><html lang=en><head><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\">"
        + "<meta http-equiv=Content-Security-Policy content=\"default-src 'none'; base-uri 'none'; form-action 'none'; connect-src 'none'; media-src 'self' file:; "
        + $"style-src 'sha256-{Hash(css)}'; script-src 'sha256-{Hash(script)}'\"><title>{H(title)} · Reader evidence</title><style>{css}</style></head>"
        + "<body><a class=skip href=#main>Skip to evidence</a>" + body
        + $"<noscript><p>Interactive selection needs JavaScript. The readable report and exposed observation tables remain available.</p></noscript><script id=evidence-data type=application/json>{data}</script><script>{script}</script></body></html>";

    private static string Resource(string name)
    {
        using var stream = typeof(TraversalEvidenceViewer).Assembly.GetManifestResourceStream($"HeadlessScreenReader.Cli.Viewer.{name}")
            ?? throw new InvalidOperationException("VIEWER_RESOURCE_MISSING");
        using var reader = new StreamReader(stream);
        // HTML parsing normalizes newlines before applying inline CSP hashes.
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
    private static string H(string value) => WebUtility.HtmlEncode(value);
    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
