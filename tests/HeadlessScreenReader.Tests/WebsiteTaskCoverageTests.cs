using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class WebsiteTaskCoverageTests
{
    [Theory]
    [InlineData("link", "Research courses", "Catalog")]
    [InlineData("textbox", "Research notes", "FormCompletion")]
    [InlineData("link", "Contactless support", "Help")]
    public void PurposeHintsUseWholeEnglishWordsWithoutReclassifyingOriginalNames(string role, string label, string expected)
    {
        var coverage = WebsiteTaskCoverage.Inspect(Snapshot(new AccessibleNode("source", role, label)));
        var group = Assert.Single(coverage.Groups.Where(g => g.ObservedControls > 0));
        Assert.Equal(expected, group.Kind); Assert.Equal(label, Assert.Single(group.Examples).ExposedName);
        Assert.False(group.ExecutionAuthorized); Assert.False(coverage.FullSiteCoverageEstablished);
    }
    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) => new("retained-tree", nodes.ToImmutableArray(), []) { CaptureEpoch = 1 };

    [Theory]
    [InlineData("link", "View pricing", "Pricing", true, 1)]
    [InlineData("link", "Course catalog", "Catalog", true, 1)]
    [InlineData("link", "Contact us", "Contact", true, 1)]
    [InlineData("link", "Get support", "Help", true, 1)]
    [InlineData("button", "View pricing", "Pricing", true, 0)]
    [InlineData("searchbox", "", "Search", false, 0)]
    [InlineData("link", "Search courses", "Search", false, 0)]
    [InlineData("textbox", "Email address", "FormCompletion", false, 0)]
    [InlineData("form", "Contact form", "FormCompletion", false, 0)]
    [InlineData("link", "Sign in", "AccountAccess", false, 0)]
    [InlineData("button", "Register", "EnrollmentOrCheckout", false, 0)]
    public void HintsPreserveTheOriginalControlAndNeverAuthorizeExecution(string role, string name, string kind,
        bool serviceProfile, int linkCandidates)
    {
        var node = new AccessibleNode("original-node", role, name);
        var source = Snapshot(node);
        var report = WebsiteTaskCoverage.Inspect(source);
        var group = Assert.Single(report.Groups.Where(g => g.ObservedControls > 0));
        Assert.Equal(kind, group.Kind);
        Assert.Equal(serviceProfile, group.ServiceLinkProfileAvailable);
        Assert.Equal(linkCandidates, group.ServiceLinkCandidates);
        Assert.Equal(new PublicTaskCoverageExample("original-node", role, name, false), Assert.Single(group.Examples));
        Assert.False(group.ExecutionAuthorized);
        Assert.False(report.FullSiteCoverageEstablished);
        Assert.False(report.LegalCaseDetermined);
        Assert.Equal("NotStartedByCoverage", report.TaskExecution);
        Assert.Equal(node, Assert.Single(source.Nodes));
    }

    [Fact]
    public void DisabledAndUnsupportedControlsRemainVisibleWithoutPretendingTheLinkProfileCanUseThem()
    {
        var source = Snapshot(new("price-link", "link", "Pricing"), new("price-button", "button", "Pricing"),
            new("disabled-course", "link", "Course catalog", IsDisabled: true), new("contact", "link", "Contact"),
            new("help", "link", "Help"), new("search", "searchbox", ""), new("email", "textbox", "Email"),
            new("account", "link", "Sign in"), new("checkout", "link", "Register"), new("unnamed", "link", " "),
            new("slider", "slider", "Volume"), new("form", "form", "Send message"));
        var report = WebsiteTaskCoverage.Inspect(source);
        Assert.Equal("retained-tree", report.DocumentId);
        Assert.Equal(12, report.InspectedNodes);
        Assert.Equal(11, report.InteractiveControls);
        Assert.Equal(2, report.UnnamedInteractiveControls);
        Assert.Equal(2, report.UnclassifiedInteractiveControls);
        Assert.Equal(["Pricing", "Catalog", "Contact", "Help", "Search", "FormCompletion", "AccountAccess", "EnrollmentOrCheckout"],
            report.Groups.Select(g => g.Kind));
        var pricing = report.Groups[0];
        Assert.Equal(2, pricing.ObservedControls);
        Assert.Equal(2, pricing.EnabledControls);
        Assert.Equal(1, pricing.ServiceLinkCandidates);
        Assert.Equal("UnverifiedServiceLinkHint", pricing.State);
        var catalog = report.Groups[1];
        Assert.Equal(1, catalog.ObservedControls);
        Assert.Equal(0, catalog.EnabledControls);
        Assert.Equal(0, catalog.ServiceLinkCandidates);
        Assert.Equal("ObservedWithoutEnabledServiceLink", catalog.State);
        Assert.True(Assert.Single(catalog.Examples).Disabled);
        Assert.Equal("ObservedWithoutSupportedPublicInputProfile", report.Groups[4].State);
        Assert.Equal("", Assert.Single(report.Groups[4].Examples).ExposedName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(2048)]
    public void ExampleBoundsRetainExactTotalsAndOriginalOrdering(int count)
    {
        var source = Snapshot(Enumerable.Range(0, count).Select(i => new AccessibleNode($"node-{i}", "link", $"Pricing {i}")).ToArray());
        var report = WebsiteTaskCoverage.Inspect(source);
        var group = report.Groups[0];
        Assert.Equal(count, group.ObservedControls);
        Assert.Equal(count, group.EnabledControls);
        Assert.Equal(count, group.ServiceLinkCandidates);
        Assert.Equal(Math.Min(count, 2), group.Examples.Length);
        Assert.Equal(count > 2, group.ExamplesTruncated);
        Assert.Equal(Enumerable.Range(0, Math.Min(count, 2)).Select(i => $"node-{i}"), group.Examples.Select(e => e.NodeId));
        Assert.Equal(count == 0 ? "NotDetectedInCapturedTree" : "UnverifiedServiceLinkHint", group.State);
        Assert.All(report.Groups.Skip(1), other => { Assert.Equal(0, other.ObservedControls); Assert.Empty(other.Examples); });
        Assert.Equal("CapturedPageOnly", report.Scope);
        Assert.False(report.FullSiteCoverageEstablished);
    }

    [Fact]
    public void OriginalLimitationsAndTheirTruncationRemainExplicit()
    {
        var limits = Enumerable.Range(0, 20).Select(i => $"Original limitation {i}").ToImmutableArray();
        var source = new AccessibilitySnapshot("retained-tree", [new("heading", "heading", "Page")], limits) { CaptureEpoch = 1 };
        var report = WebsiteTaskCoverage.Inspect(source);
        Assert.Equal(27, report.TotalLimitations);
        Assert.Equal(limits.Take(12), report.Limitations);
        Assert.True(report.LimitationsTruncated);
        Assert.Equal(limits, source.Limitations);
    }

    [Fact]
    public void DuplicateIdentityAndOversizedTreesCannotProduceCoverage()
    {
        Assert.Throws<ArgumentException>(() => WebsiteTaskCoverage.Inspect(Snapshot(new("same", "link", "Pricing"), new("same", "button", "Pricing"))));
        Assert.Throws<ArgumentException>(() => WebsiteTaskCoverage.Inspect(Snapshot(Enumerable.Range(0, 2049)
            .Select(i => new AccessibleNode($"node-{i}", "link", "Pricing")).ToArray())));
    }
}
