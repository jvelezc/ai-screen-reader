using System.Net;
using System.Text;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public static class FeatureReport
{
    public static string Render(FeatureDiscovery discovery, TraversalReceipt receipt)
    {
        var inventory = FeatureProvenance.Build(discovery, receipt);
        var text = new StringBuilder("\n\n## Diagnostic feature candidates\n\n");
        text.AppendLine("Diagnostic hypotheses do not repair reader-visible names or establish feature success, barriers or reasonable effort. Route verification above remains independent.")
            .AppendLine().AppendLine($"Discovery: {Cell(discovery.DiscoveryId)}. Main-document-only inventory; truncated: {discovery.Truncated}.")
            .AppendLine().AppendLine("| Candidate | Reader role/name | Diagnostic-only hint and source | Initial disabled | Source binding | Observations | Related actual actions |")
            .AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var feature in inventory.Features)
        {
            var candidate = feature.Candidate;
            text.AppendLine($"| {Cell(candidate.Id)} | {Cell(candidate.ObservedNode.Role)}: {Cell(candidate.ObservedNode.Name.Length == 0 ? "(unnamed)" : candidate.ObservedNode.Name)} | "
                + $"{Cell(candidate.Hint is null ? candidate.HintState.ToString() : candidate.Hint.Value + " [" + candidate.Hint.Source + "]")} | "
                + $"{candidate.ObservedNode.IsDisabled} | {feature.Binding} | {string.Join(", ", feature.ObservationIds.Select(Link))} | "
                + $"{string.Join(", ", feature.Relations.Select(r => Link(r.AttemptId)))} |");
        }
        foreach (var limitation in discovery.Limitations) text.AppendLine().AppendLine(Cell(limitation));
        return text.ToString();
    }
    private static string Link(string id) => $"[{Cell(id)}](#{Uri.EscapeDataString(id)})";
    private static string Cell(string value) => WebUtility.HtmlEncode(value).Replace("|", "&#124;", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
        .Replace("[", "&#91;", StringComparison.Ordinal).Replace("]", "&#93;", StringComparison.Ordinal)
        .Replace("*", "&#42;", StringComparison.Ordinal).Replace("_", "&#95;", StringComparison.Ordinal)
        .Replace(((char)96).ToString(), "&#96;", StringComparison.Ordinal).Replace("\\", "&#92;", StringComparison.Ordinal);
}
