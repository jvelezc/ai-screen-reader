using System.Collections.Immutable;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>Conservative diagnostic inference. Never changes exposed names, reader output, task outcomes or evidence sufficiency.</summary>
public static partial class WebsiteScreening
{
    public const string MissingControlName = "ExposedInteractiveControlNameMissing";
    private static readonly HashSet<string> InteractiveRoles = new(StringComparer.Ordinal)
    {
        "button", "link", "textbox", "searchbox", "combobox", "listbox", "checkbox", "radio", "switch",
        "slider", "spinbutton", "tab", "menuitem", "menuitemcheckbox", "menuitemradio", "treeitem"
    };

    public static WebsitePageScreening Inspect(AccessibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.DocumentId) || snapshot.DocumentId.Length > 8192 || snapshot.CaptureEpoch < 1
            || snapshot.Nodes.IsDefault || snapshot.Nodes.Length > 2048
            || snapshot.Nodes.Any(n => n is null || string.IsNullOrWhiteSpace(n.Id) || n.Id.Length > 8192
                || n.Role is null || n.Role.Length > 100 || n.Name is null || n.Name.Length > 8192)
            || snapshot.Nodes.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Nodes.Length)
            throw new ArgumentException("A bounded, identity-preserving captured tree is required.", nameof(snapshot));
        var issues = ImmutableArray.CreateBuilder<WebsiteScreeningIssue>();
        var tasks = ImmutableArray.CreateBuilder<WebsiteTaskCandidate>();
        var interactive = 0;
        foreach (var node in snapshot.Nodes)
        {
            if (!InteractiveRoles.Contains(node.Role)) continue;
            interactive++;
            if (string.IsNullOrWhiteSpace(node.Name))
                issues.Add(new($"i{issues.Count + 1}", MissingControlName, node.Id, node.Role, node.Name, node.IsDisabled));
            else if (node.Role is "link" or "button" && Purpose(node.Name) is { } purpose)
                tasks.Add(new($"t{tasks.Count + 1}", purpose, node.Id, node.Role, node.Name, node.IsDisabled));
        }
        var limits = snapshot.Limitations.IsDefault ? ImmutableArray<string>.Empty : snapshot.Limitations;
        limits = limits.AddRange(new string[] {
            "Only exposed interactive-control names were checked; this is not a complete WCAG evaluation.",
            "Service-journey suggestions use English label heuristics. Purpose, destinations and task outcomes remain unverified.",
            "No keyboard or pointer task, independent repeat, task recording or task graph was collected by this screening.",
            "Disability, personal encounter, injury, standing, legal coverage and jurisdiction are outside this screening."
        });
        return new(WebsitePageScreening.CurrentSchema, WebsitePageScreening.CurrentPolicy, snapshot.DocumentId,
            snapshot.CaptureEpoch, snapshot.Nodes.Length, interactive, issues.ToImmutable(), tasks.ToImmutable(), limits);
    }

    private static WebsiteTaskPurpose? Purpose(string label)
    {
        var words = Words().Matches(label).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (words.Overlaps(["login", "account", "password"]) || words.Contains("in") && words.Overlaps(["log", "sign"]))
            return WebsiteTaskPurpose.AccountAccess;
        if (words.Overlaps(["pricing", "price", "prices", "plans", "fees", "cost"])) return WebsiteTaskPurpose.ReadPricing;
        if (words.Overlaps(["enroll", "enrollment", "register", "subscribe", "subscription", "checkout", "order", "book", "certified"])
            || words.Contains("sign") && words.Contains("up")) return WebsiteTaskPurpose.Enrollment;
        if (words.Overlaps(["contact", "support", "help", "accessibility", "assistance"])) return WebsiteTaskPurpose.ContactSupport;
        if (words.Overlaps(["service", "services", "course", "courses", "catalog", "products", "apps"]))
            return WebsiteTaskPurpose.DiscoverServices;
        return null;
    }

    [GeneratedRegex("[A-Za-z]+", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Words();
}
