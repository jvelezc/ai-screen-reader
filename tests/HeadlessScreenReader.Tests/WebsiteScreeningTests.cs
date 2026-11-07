using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class WebsiteScreeningTests
{
    [Theory]
    [InlineData("button", true)]
    [InlineData("link", true)]
    [InlineData("textbox", true)]
    [InlineData("combobox", true)]
    [InlineData("checkbox", true)]
    [InlineData("generic", false)]
    [InlineData("paragraph", false)]
    [InlineData("heading", false)]
    [InlineData("image", false)]
    public void OnlyExposedInteractiveRolesProducePotentialNameIssues(string role, bool potential)
    {
        var node = new AccessibleNode("original-node", role, " \t ", IsDisabled: true);
        var snapshot = Snapshot(node);
        var report = WebsiteScreening.Inspect(snapshot);
        Assert.Equal(potential ? 1 : 0, report.InteractiveControls);
        if (potential)
        {
            var issue = Assert.Single(report.Issues);
            Assert.Equal("ExposedInteractiveControlNameMissing", issue.Rule);
            Assert.Equal(node.Id, issue.NodeId); Assert.Equal(role, issue.Role); Assert.Equal(" \t ", issue.ExposedName);
            Assert.True(issue.Disabled); Assert.Equal("PotentialIssue", issue.Classification); Assert.Equal("NotAssessed", issue.TaskImpact);
        }
        else Assert.Empty(report.Issues);
        Assert.Equal(node, Assert.Single(snapshot.Nodes)); Assert.Empty(report.SuggestedTasks);
        Assert.Equal("NotStarted", report.TaskExecution); Assert.Equal("NotAssessed", report.TaskBarrier);
        Assert.Equal("InsufficientTaskEvidence", report.EvidenceReadiness); Assert.False(report.LegalCaseDetermined);
    }

    [Theory]
    [InlineData("View Course Catalog", WebsiteTaskPurpose.DiscoverServices)]
    [InlineData("See pricing →", WebsiteTaskPurpose.ReadPricing)]
    [InlineData("Get Certified", WebsiteTaskPurpose.Enrollment)]
    [InlineData("Sign Up", WebsiteTaskPurpose.Enrollment)]
    [InlineData("Log In", WebsiteTaskPurpose.AccountAccess)]
    [InlineData("Forgot password?", WebsiteTaskPurpose.AccountAccess)]
    [InlineData("Find Support", WebsiteTaskPurpose.ContactSupport)]
    public void ObservedLabelsSuggestServiceJourneysWithoutExecutionOrAuthority(string label, WebsiteTaskPurpose expected)
    {
        var report = WebsiteScreening.Inspect(Snapshot(new AccessibleNode("source-link", "link", label)));
        var task = Assert.Single(report.SuggestedTasks);
        Assert.Equal(expected, task.Purpose); Assert.Equal("source-link", task.NodeId); Assert.Equal(label, task.ExposedName);
        Assert.Equal("NotTested", task.State); Assert.False(task.ExecutionAuthorized); Assert.Empty(report.Issues);
        Assert.Equal("CapturedPageOnly", report.Scope); Assert.Equal("NotStarted", report.TaskExecution);
        Assert.Contains(report.Limitations, text => text.Contains("English", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Privacy Policy", "link")]
    [InlineData("Supportive families", "button")]
    [InlineData("View Course Catalog", "StaticText")]
    [InlineData("Iniciar sesión", "link")]
    public void UnknownPurposeAndNoninteractiveTextStayUnclassified(string label, string role)
    {
        var report = WebsiteScreening.Inspect(Snapshot(new AccessibleNode("source", role, label)));
        Assert.Empty(report.SuggestedTasks); Assert.Empty(report.Issues);
        Assert.False(report.LegalCaseDetermined); Assert.Equal("InsufficientTaskEvidence", report.EvidenceReadiness);
    }

    [Fact]
    public void InspectionCoversTheWholeStoredTreeWithoutRepairingExposedNames()
    {
        var nodes = Enumerable.Range(1, 120).Select(i => new AccessibleNode($"layout-{i}", "generic", "")).ToList();
        nodes.Add(new("late-unnamed", "button", "")); nodes.Add(new("late-course", "link", "View Course Catalog"));
        var report = WebsiteScreening.Inspect(Snapshot([.. nodes]));
        Assert.Equal(122, report.InspectedNodes); Assert.Equal(2, report.InteractiveControls);
        Assert.Equal("late-unnamed", Assert.Single(report.Issues).NodeId);
        Assert.Equal("late-course", Assert.Single(report.SuggestedTasks).NodeId);
        Assert.Equal("", nodes[^2].Name);
    }

    [Fact]
    public void EmptyCaptureAndSourceLimitationsCannotBecomeAnAccessibilityPass()
    {
        var report = WebsiteScreening.Inspect(Snapshot() with { Limitations = ["Snapshot truncated by original collector"] });
        Assert.Equal(0, report.InspectedNodes); Assert.Empty(report.Issues); Assert.Empty(report.SuggestedTasks);
        Assert.Contains("Snapshot truncated by original collector", report.Limitations);
        Assert.Equal("NotAssessed", report.TaskBarrier); Assert.Equal("InsufficientTaskEvidence", report.EvidenceReadiness);
    }

    [Fact]
    public void DuplicateNodeIdentityIsRejectedInsteadOfMergingUnrelatedEvidence()
    {
        var snapshot = Snapshot(new AccessibleNode("same", "button", ""), new("same", "link", "View courses"));
        Assert.Throws<ArgumentException>(() => WebsiteScreening.Inspect(snapshot));
    }

    private static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) =>
        new("source-document", nodes.ToImmutableArray(), []) { CaptureEpoch = 1 };
}
