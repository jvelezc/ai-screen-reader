using System.Globalization;
using System.Text;
using System.Xml;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>Passive SVG execution trace, with links to the equivalent evidence report.</summary>
public static class TraversalTraceDiagram
{
    public static string Render(TraversalReceipt receipt, int route = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(route, 1);
        var graph = TraversalProjection.Build(receipt);
        var unresolved = graph.UnresolvedAttempts.Length;
        var height = 220 + Math.Max(1, graph.Nodes.Length) * 156 + unresolved * 56;
        var output = new StringBuilder();
        using var xml = XmlWriter.Create(output, new() { OmitXmlDeclaration = true, Indent = true });
        xml.WriteStartElement("svg", "http://www.w3.org/2000/svg");
        xml.WriteAttributeString("width", "800");
        xml.WriteAttributeString("height", height.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("viewBox", $"0 0 800 {height}");
        xml.WriteAttributeString("role", "img");
        xml.WriteAttributeString("aria-labelledby", "trace-title trace-description");
        Element("title", receipt.Plan.Name, "trace-title");
        Element("desc", "Directed execution trace. Nodes are retained observations; arrows are reader commands and actual browser keys. "
            + "The objective is verified only when explicitly satisfied. Every node and arrow links to its evidence report.", "trace-description");
        xml.WriteStartElement("defs");
        xml.WriteStartElement("marker");
        Attribute("id", "arrow"); Attribute("viewBox", "0 0 10 10"); Attribute("refX", "9"); Attribute("refY", "5");
        Attribute("markerWidth", "7"); Attribute("markerHeight", "7"); Attribute("orient", "auto-start-reverse");
        xml.WriteStartElement("path"); Attribute("d", "M 0 0 L 10 5 L 0 10 z"); Attribute("fill", "#42526b"); xml.WriteEndElement();
        xml.WriteEndElement(); xml.WriteEndElement();
        xml.WriteStartElement("rect"); Attribute("width", "800"); Attribute("height", height.ToString(CultureInfo.InvariantCulture));
        Attribute("fill", "#ffffff"); xml.WriteEndElement();
        Text(28, 36, "Execution trace", 24, "#16243a");
        Text(28, 66, $"Objective: {Short(receipt.Plan.Destination.Role + ": " + receipt.Plan.Destination.Name, 78)}", 17, "#16243a");
        Text(28, 94, $"{receipt.Reachability} | {receipt.StopReason} | reasonable-effort assessment: not assessed", 16, "#42526b");

        var positions = graph.Nodes.Select((n, i) => (n.ObservationId, Index: i)).ToDictionary(n => n.ObservationId, n => n.Index);
        foreach (var edge in graph.Edges)
        {
            var sourceY = 136 + positions[edge.SourceObservationId] * 156 + 76;
            var resultY = 136 + positions[edge.ResultObservationId] * 156;
            var firstEvent = receipt.Attempts.Single(a => a.Id == edge.AttemptId).Events.FirstOrDefault();
            Link(firstEvent is null ? edge.SourceObservationId : $"event-{firstEvent.Sequence}");
            xml.WriteStartElement("path"); Attribute("d", $"M 200 {sourceY} L 200 {resultY - 5}");
            Attribute("stroke", "#42526b"); Attribute("stroke-width", "2"); Attribute("fill", "none"); Attribute("marker-end", "url(#arrow)"); xml.WriteEndElement();
            Element("title", $"{edge.AttemptId}: {edge.Command}; {KeyLabel(edge)}; {edge.Outcome}; verification {edge.Verification}");
            Text(218, sourceY + 29, $"{edge.AttemptId}: {edge.Command} | {Short(KeyLabel(edge), 58)}", 15, "#253c5b");
            Text(218, sourceY + 52, $"{edge.Outcome}; goal: {edge.Verification}", 14, "#42526b");
            xml.WriteEndElement();
        }
        for (var index = 0; index < graph.Nodes.Length; index++)
        {
            var node = graph.Nodes[index];
            var y = 136 + index * 156;
            var incoming = graph.Edges.LastOrDefault(e => e.ResultObservationId == node.ObservationId);
            var verified = incoming?.Verification == VerificationStatus.Satisfied;
            var blocked = incoming?.Outcome == ReaderOutcome.Blocked;
            Link(node.ObservationId);
            Element("title", $"{node.ObservationId}: {node.Label}; document {node.DocumentId}; cursor {node.CursorNodeId ?? "none"}; "
                + $"focus {node.FocusedNodeId ?? "none"}; active descendant {node.ActiveDescendantNodeId ?? "none"}");
            xml.WriteStartElement("rect"); Attribute("x", "28"); Attribute("y", y.ToString(CultureInfo.InvariantCulture));
            Attribute("width", "744"); Attribute("height", "76"); Attribute("rx", "8");
            Attribute("fill", verified ? "#e7f5ed" : blocked ? "#fff1dc" : "#f1f5fa");
            Attribute("stroke", verified ? "#257147" : blocked ? "#936014" : "#8d9cb0"); xml.WriteEndElement();
            Text(44, y + 28, $"{node.ObservationId} | {Short(node.Label, 74)}", 17, "#16243a");
            Text(44, y + 54, $"Mode: {node.Mode} | {(verified ? "Objective verified here" : blocked ? "Disabled control observed" : "Observed state")}", 15, "#42526b");
            xml.WriteEndElement();
        }
        var footer = 136 + Math.Max(1, graph.Nodes.Length) * 156;
        if (graph.Nodes.IsEmpty) Text(28, 166, "No reliable state observation was retained.", 17, "#42526b");
        foreach (var attempt in graph.UnresolvedAttempts)
        {
            var sourceY = 136 + positions[attempt.SourceObservationId] * 156 + 76;
            var firstEvent = attempt.Events.FirstOrDefault();
            Link(firstEvent is null ? attempt.SourceObservationId : $"event-{firstEvent.Sequence}");
            xml.WriteStartElement("path"); Attribute("d", $"M 200 {sourceY} L 200 {sourceY + 38}");
            Attribute("stroke", "#936014"); Attribute("stroke-width", "2"); Attribute("stroke-dasharray", "5 4");
            Attribute("marker-end", "url(#arrow)"); xml.WriteEndElement();
            Text(28, footer, $"{attempt.Id}: {attempt.Instruction.Command} | {attempt.Step?.Outcome.ToString() ?? "Interrupted"} | no reliable result node", 15, "#704311");
            var attemptedKeys = string.Join(", ", attempt.Events.Where(e => e.Kind == ReaderEventKind.InputAttempted).Select(e => e.Detail));
            Text(28, footer + 24, $"Key attempts: {Short(attemptedKeys.Length == 0 ? "none observed" : attemptedKeys, 32)} | completed API calls: "
                + $"{attempt.Step?.BrowserKeystrokes ?? 0} | possible input: {attempt.Step?.InputMayHaveOccurred ?? false}", 14, "#704311");
            xml.WriteEndElement();
            footer += 56;
        }
        Text(28, footer + 18, "Reader commands and browser keys are separate effort measures. No path-complexity score.", 14, "#42526b");
        Text(28, footer + 42, "Green: verified objective. Amber: observed disabled control. Gray: observed state.", 14, "#42526b");
        xml.WriteEndElement(); xml.Flush();
        return output.ToString();

        void Attribute(string name, string value) => xml.WriteAttributeString(name, value);
        void Element(string name, string value, string? id = null)
        {
            xml.WriteStartElement(name);
            if (id is not null) Attribute("id", id);
            xml.WriteString(Safe(value)); xml.WriteEndElement();
        }
        void Text(int x, int y, string value, int size, string fill)
        {
            xml.WriteStartElement("text"); Attribute("x", x.ToString(CultureInfo.InvariantCulture)); Attribute("y", y.ToString(CultureInfo.InvariantCulture));
            Attribute("font-family", "Arial, sans-serif"); Attribute("font-size", size.ToString(CultureInfo.InvariantCulture)); Attribute("fill", fill);
            xml.WriteString(Safe(value)); xml.WriteEndElement();
        }
        void Link(string anchor)
        {
            xml.WriteStartElement("a"); Attribute("href", $"route-{route}.md#{Uri.EscapeDataString(anchor)}");
        }
    }

    public static string KeyLabel(TraversalGraphEdge edge)
    {
        var observed = edge.KeyboardInputs.IsEmpty ? "no browser keys observed"
            : string.Join(", ", edge.KeyboardInputs.Select(k => $"{k.Key} ({(k.CompletionEventSequence is null ? "completion unobserved"
                : k.AttemptEventSequence is null ? "completion observed; attempt missing" : "completed")})"));
        return edge.InputEvidenceComplete ? observed : observed + "; keyboard evidence incomplete";
    }

    private static string Short(string value, int length)
    {
        var runes = value.EnumerateRunes().ToArray();
        return runes.Length <= length ? value : string.Concat(runes.Take(length - 3)) + "...";
    }

    private static string Safe(string value) => string.Concat(value.EnumerateRunes().Select(r =>
        r.Value is >= 32 and <= 0xd7ff or >= 0xe000 and <= 0xfffd or >= 0x10000 and <= 0x10ffff
            ? r.ToString() : " "));
}
