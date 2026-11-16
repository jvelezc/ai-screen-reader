using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class PublicTaskBrowserTests
{
    [Theory]
    [InlineData(null, "Pricing", "View pricing", "/pricing")]
    [InlineData("Pricing", "Pricing", "View pricing", "/pricing")]
    [InlineData("Catalog", "Catalog", "Course catalog", "/catalog")]
    [InlineData("Contact", "Contact", "Contact us", "/contact")]
    [InlineData("Help", "Help", "Get help", "/help")]
    public async Task ActualChromiumDiscoveryChoosesTheRequestedServiceInsteadOfAnEarlierPricingLink(
        string? requested, string expectedKind, string expectedName, string expectedPath)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        await browser.DiagnosticPage.SetContentAsync("<h1>Services</h1><a href='https://example.com/pricing'>View pricing</a>"
            + "<a href='https://example.com/contact'>Contact us</a><a href='https://example.com/catalog'>Course catalog</a>"
            + "<a href='https://example.com/help'>Get help</a>");
        var before = browser.DiagnosticPage.Url;
        var job = PublicTaskTests.Start().Job with { GoalKind = requested };
        await using var task = new PublicNavigationBrowser(browser, job, Path.GetTempPath());
        var goal = await task.DiscoverAsync(default);
        Assert.NotNull(goal);
        Assert.Equal(expectedKind, goal.Kind);
        Assert.Equal(expectedName, goal.LinkName);
        Assert.Equal("https://example.com" + expectedPath, goal.DestinationUrl);
        Assert.Equal(before, browser.DiagnosticPage.Url);
    }

    [Fact]
    public async Task MissingSelectedServiceDoesNotSilentlyFallBackToPricing()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        await browser.DiagnosticPage.SetContentAsync("<a href='https://example.com/pricing'>View pricing</a>");
        var job = PublicTaskTests.Start().Job with { GoalKind = "Contact" };
        await using var task = new PublicNavigationBrowser(browser, job, Path.GetTempPath());
        Assert.Null(await task.DiscoverAsync(default));
        Assert.DoesNotContain("https://example.com/", browser.DiagnosticPage.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActualChromiumNetworkTimelineDistinguishesHttpErrorsFromTransportFailures()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        var page = browser.DiagnosticPage;
        var recorder = new PublicTaskNetworkRecorder(); recorder.Attach(page.Context);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Context.RequestFinished += (_, r) => { if (r.Url.Contains("/bad-status", StringComparison.Ordinal)) finished.TrySetResult(); };
        page.Context.RequestFailed += (_, r) => { if (r.Url.Contains("/broken", StringComparison.Ordinal)) failed.TrySetResult(); };
        await page.Context.RouteAsync("https://api.example.net/**", route => route.Request.Url.Contains("/bad-status", StringComparison.Ordinal)
            ? route.FulfillAsync(new() { Status = 422, Body = "private-response-body", Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" } })
            : route.AbortAsync("connectionrefused"));
        await page.EvaluateAsync("async () => { await fetch('https://api.example.net/bad-status?secret=never-store-query', {method:'POST',body:'never-store-body'}).catch(()=>{}); await fetch('https://api.example.net/broken').catch(()=>{}); }");
        await Task.WhenAll(finished.Task, failed.Task).WaitAsync(TimeSpan.FromSeconds(5));
        var trace = recorder.Snapshot(); trace.Validate();
        Assert.Contains(trace.Events, e => e.Method == "POST" && e.ResourceType == "fetch" && e.Phase == "Response" && e.Status == 422 && e.Code == "HTTP_ERROR");
        Assert.Contains(trace.Events, e => e.Phase == "Failed" && e.Code == "ERR_CONNECTION_REFUSED");
        var json = System.Text.Json.JsonSerializer.Serialize(trace);
        Assert.DoesNotContain("never-store", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-response-body", json, StringComparison.Ordinal);
    }

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
