using System.Globalization;
using System.Net;
using System.Text;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>A passive Markdown report. Selecting evidence never invokes browser operations.</summary>
public static class TraversalReport
{
    public static string Render(TraversalReceipt receipt)
    {
        var graph = TraversalProjection.Build(receipt);
        var text = new StringBuilder();
        text.AppendLine($"# {Cell(receipt.Plan.Name)}").AppendLine();
        text.AppendLine($"Profile: {Cell(receipt.Profile)}. Schema: {Cell(receipt.Schema)}.").AppendLine();
        text.AppendLine("| Execution | Destination | Barrier | Policy | Stop reason |");
        text.AppendLine("| --- | --- | --- | --- | --- |");
        text.AppendLine($"| {receipt.Execution} | {receipt.Reachability} | {receipt.Barrier} | {receipt.Policy} | {receipt.StopReason} |").AppendLine();
        text.AppendLine($"**Requested destination:** {Cell(receipt.Plan.Destination.Role)}: {Cell(receipt.Plan.Destination.Name)}.").AppendLine();
        text.AppendLine($"Attempted keys: {receipt.AttemptedKeystrokes}; completed keyboard API calls: {receipt.CompletedKeystrokes}. "
            + $"Unexecuted route instructions: {receipt.UnexecutedInstructions}. Timeline truncated: {receipt.TimelineTruncated}.").AppendLine();
        text.AppendLine("Structured reader observations and events only. No recording, credential entry, human takeover, automatic replay, "
            + "site-wide coverage percentage or accessibility-conformance claim. Routes run on fresh synthetic fixtures.").AppendLine();
        if (receipt.ErrorCode is not null) text.AppendLine($"Engine/policy code: {Cell(receipt.ErrorCode)}.").AppendLine();
        text.AppendLine("## Observed traversal graph").AppendLine();
        text.AppendLine("Every arrow below joins observations actually retained for this attempt. The requested goal does not create a node. "
            + "Observations are kept separate, including repeated states and different states at the same URL.").AppendLine();
        text.AppendLine("| Attempt | Source | Actual result | Command | Reader outcome | Goal verification | Events |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var attempt in receipt.Attempts)
        {
            var edge = graph.Edges.SingleOrDefault(e => e.AttemptId == attempt.Id);
            var destination = edge is null ? "No reliable result observation" : Link(edge.ResultObservationId);
            var events = attempt.Events.IsEmpty ? "Unavailable" : $"[Transcript](#event-{attempt.Events[0].Sequence})";
            text.AppendLine($"| {Cell(attempt.Id)} | {Link(attempt.SourceObservationId)} | {destination} | "
                + $"{attempt.Instruction.Command} | {attempt.Step?.Outcome.ToString() ?? "Interrupted"} | "
                + $"{attempt.Step?.Verification.ToString() ?? "NotRequested"} | {events} |");
        }
        text.AppendLine().AppendLine("## Observations").AppendLine();
        foreach (var observation in receipt.Observations)
        {
            text.AppendLine($"<a id=\"{WebUtility.HtmlEncode(observation.Id)}\"></a>");
            text.AppendLine($"### Observation {Cell(observation.Id)}").AppendLine();
            text.AppendLine($"Document: {Cell(observation.Snapshot.DocumentId)}; mode: {observation.Mode}; "
                + $"cursor: {Cell(observation.CursorNodeId ?? "none")}; focus: {Cell(observation.Snapshot.FocusedNode?.Id ?? "none")}; "
                + $"active descendant: {Cell(observation.Snapshot.FocusedNode?.ActiveDescendantId ?? "none")}.").AppendLine();
            text.AppendLine("| Node | Role | Exposed name | Disabled | Selected |");
            text.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var node in observation.Snapshot.Nodes)
                text.AppendLine($"| {Cell(node.Id)} | {Cell(node.Role)} | {Cell(node.Name.Length == 0 ? "(unnamed)" : node.Name)} | "
                    + $"{node.IsDisabled} | {node.Selected?.ToString() ?? "Unknown"} |");
            text.AppendLine();
            foreach (var limitation in observation.Snapshot.Limitations) text.AppendLine($"- {Cell(limitation)}");
            text.AppendLine();
        }
        text.AppendLine("## Transcript").AppendLine();
        text.AppendLine("| Event | Command sequence | Elapsed ms | Kind | Detail |");
        text.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var item in receipt.Timeline)
            text.AppendLine($"| <a id=\"event-{item.Sequence}\"></a>{item.Sequence} | {item.CommandSequence} | "
                + $"{item.ElapsedMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} | {item.Kind} | {Cell(item.Detail)} |");
        return text.ToString();
    }

    private static string Link(string id) => $"[{Cell(id)}](#{Uri.EscapeDataString(id)})";

    private static string Cell(string value)
    {
        var encoded = WebUtility.HtmlEncode(value).Replace("\r", " ").Replace("\n", " ");
        foreach (var character in new[] { '\\', '|', '`', '[', ']', '*', '_' })
            encoded = encoded.Replace(character.ToString(), $"\\{character}", StringComparison.Ordinal);
        return encoded;
    }
}
