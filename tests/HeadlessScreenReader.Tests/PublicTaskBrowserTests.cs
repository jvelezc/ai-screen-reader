using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class PublicTaskBrowserTests
{
    [Theory]
    [InlineData("$14.99", "/month", true)]
    [InlineData("£20.00", "Billed yearly", true)]
    [InlineData("€19.99", "one-time payment", true)]
    [InlineData("$14.99", "See pricing", false)]
    [InlineData("See pricing", "Billed monthly", false)]
    public void PricingGoalUsesObservedAmountAndBillingTerms(string amount, string terms, bool expected)
    {
        var goal = new PublicTaskGoal("Pricing", "Reach pricing information", "See pricing", "https://example.com/pricing");
        var snapshot = new AccessibilitySnapshot("pricing", [new("h", "heading", "Start your journey"),
            new("amount", "StaticText", amount), new("terms", "StaticText", terms)], []);
        Assert.Equal(expected, PublicNavigationBrowser.GoalContentObserved(goal, snapshot));
    }

    [Fact]
    public async Task DiscoveryReadsAnActualVisibleAnchorWithoutExecutingIt()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        await browser.DiagnosticPage.EvaluateAsync("() => {document.body.innerHTML='<h1>Service</h1><a href=\"https://example.com/pricing\">View pricing</a>'}");
        await using var task = new PublicNavigationBrowser(browser, PublicTaskTests.Start().Job, Path.GetTempPath());
        var goal = await task.DiscoverAsync(default);
        Assert.Equal(new PublicTaskGoal("Pricing", "Reach pricing information", "View pricing", "https://example.com/pricing"), goal);
        Assert.True(await task.SameGoalAsync(goal!, default));
        Assert.DoesNotContain("https://example.com/", browser.DiagnosticPage.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActualFocusedNodeIdentityMatchesTheOriginalAccessibilitySnapshot()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        await browser.DiagnosticPage.EvaluateAsync("() => {document.body.innerHTML='<a href=\"https://example.com/pricing\">View pricing</a>'}");
        await using var task = new PublicNavigationBrowser(browser, PublicTaskTests.Start().Job, Path.GetTempPath());
        await browser.PressKeyAsync("Tab");
        var focus = await task.FocusAsync(default); var snapshot = await task.CaptureAsync(default);
        Assert.NotNull(focus); Assert.Equal("View pricing", focus.Name); Assert.Equal("https://example.com/pricing", focus.AnchorUrl);
        Assert.Contains(snapshot.Nodes, n => n.Id == focus.NodeId && n.IsFocused && n.Role == "link");
    }
}
