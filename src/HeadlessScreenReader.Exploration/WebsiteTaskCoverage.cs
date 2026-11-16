using System.Collections.Immutable;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>English diagnostic hints over original reader observations; never repairs names or chooses hidden controls.</summary>
public static partial class WebsiteTaskCoverage
{
    public static PublicTaskCoverage Inspect(AccessibilitySnapshot snapshot)
    {
        // Reuse the established complete-tree identity and bounded-text validation.
        var screening = WebsiteScreening.Inspect(snapshot);
        string[] kinds = ["Pricing", "Catalog", "Contact", "Help", "Search", "FormCompletion", "AccountAccess", "EnrollmentOrCheckout"];
        var controls = kinds.ToDictionary(k => k, _ => new List<PublicTaskCoverageExample>(), StringComparer.Ordinal);
        var purposes = screening.SuggestedTasks.ToDictionary(t => t.NodeId, t => t.Purpose, StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes)
        {
            string? kind = null;
            if (node.Role == "searchbox" || (node.Role is "textbox" or "link" or "button")
                && SearchWord().IsMatch(node.Name)) kind = "Search";
            else if (node.Role is "textbox" or "combobox" or "form") kind = "FormCompletion";
            else if (purposes.TryGetValue(node.Id, out var purpose))
                kind = purpose switch
                {
                    WebsiteTaskPurpose.AccountAccess => "AccountAccess",
                    WebsiteTaskPurpose.Enrollment => "EnrollmentOrCheckout",
                    WebsiteTaskPurpose.ReadPricing => "Pricing",
                    WebsiteTaskPurpose.DiscoverServices => "Catalog",
                    WebsiteTaskPurpose.ContactSupport when ContactWord().IsMatch(node.Name) => "Contact",
                    WebsiteTaskPurpose.ContactSupport => "Help",
                    _ => null
                };
            if (kind is not null) controls[kind].Add(new(node.Id, node.Role, node.Name, node.IsDisabled));
        }
        var groups = kinds.Select(kind => new PublicTaskCoverageGroup(kind, PublicTaskGoal.SupportsKind(kind),
            controls[kind].Count, controls[kind].Count(c => !c.Disabled),
            PublicTaskGoal.SupportsKind(kind) ? controls[kind].Count(c => c.Role == "link" && !c.Disabled) : 0,
            controls[kind].Take(2).ToImmutableArray())).ToImmutableArray();
        var limits = screening.Limitations.Add("Coverage hints do not attempt a task or authorize input. A matching label is not a verified destination.")
            .Add("Not detected means absent from this captured tree's hints, not absent from the website. Other pages and unsupported frames remain unassessed.")
            .Add("A service-link profile only follows a public link. Search entry, form completion, sign-in and transactions require separately supported profiles.");
        return new(PublicTaskCoverage.CurrentSchema, PublicTaskCoverage.CurrentPolicy, snapshot.DocumentId, snapshot.CaptureEpoch,
            snapshot.Nodes.Length, screening.InteractiveControls, screening.Issues.Length,
            screening.InteractiveControls - controls.Values.SelectMany(v => v).Count(c => c.Role != "form"),
            groups, limits.Take(12).ToImmutableArray(), limits.Length);
    }

    [GeneratedRegex("(?<![A-Za-z])search(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SearchWord();
    [GeneratedRegex("(?<![A-Za-z])contact(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex ContactWord();
}
