using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumSyntheticInputTests
{
    [Fact]
    public async Task FixedSyntheticInput_RequiresNativeFocusAndGapThenClearsBeforeSameContextResume()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.SyntheticHandoff);
        await browser.PressKeyAsync("Tab"); var before = await browser.CaptureAsync();
        Assert.Equal("Synthetic canary field", before.FocusedNode!.Name);
        var gap = await browser.ExcludeCaptureAsync(); var lease = browser.GrantSyntheticInput(gap, 1);
        var typed = await browser.ExecuteSyntheticInputAsync(lease, SyntheticInputAction.EnterCanary);
        Assert.Equal(1, typed.InputRequests); Assert.Equal(1, typed.CompletedInputCalls); Assert.Null(typed.ErrorCode);
        Assert.False(typed.InputUncertain); Assert.False(typed.CanaryCleared);
        Assert.Equal(browser.SyntheticCanaryForAudit, await browser.DiagnosticPage.Locator("#synthetic-sensitive").InputValueAsync());
        Assert.Equal(browser.SyntheticCanaryForAudit, await browser.DiagnosticPage.Locator("h1").InnerTextAsync());
        await Assert.ThrowsAsync<BrowserOperationException>(() => browser.CaptureAsync());
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressKeyAsync("Tab"));
        Assert.Equal("SYNTHETIC_CLEAR_REQUIRED", (await Assert.ThrowsAsync<BrowserOperationException>(() => browser.ResumeCaptureAsync(gap))).Code);
        var duplicate = await browser.ExecuteSyntheticInputAsync(lease, SyntheticInputAction.EnterCanary);
        Assert.Equal("SYNTHETIC_CLEAR_REQUIRED", duplicate.ErrorCode); Assert.Equal(0, duplicate.InputRequests);
        var clear = await browser.ExecuteSyntheticInputAsync(lease, SyntheticInputAction.ClearCanary);
        Assert.Null(clear.ErrorCode); Assert.Equal(2, clear.CompletedInputCalls); Assert.True(clear.CanaryCleared);
        Assert.Equal("", await browser.DiagnosticPage.Locator("#synthetic-sensitive").InputValueAsync());
        await browser.RevokeSyntheticInputAsync(lease);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.ExecuteSyntheticInputAsync(lease, SyntheticInputAction.EnterCanary));
        var after = await browser.ResumeCaptureAsync(gap);
        Assert.Equal(before.DocumentId, after.DocumentId); Assert.Equal(2, after.CaptureEpoch);
        Assert.DoesNotContain(browser.SyntheticCanaryForAudit, JsonSerializer.Serialize(after));
        Assert.DoesNotContain(browser.SyntheticCanaryForAudit, JsonSerializer.Serialize(typed));
    }

    [Theory]
    [InlineData(BundledFixture.Foundation)]
    [InlineData(BundledFixture.CapturePrivacy)]
    [InlineData(BundledFixture.Traversal)]
    public async Task OtherFixtures_CannotGrantTheSyntheticInputChannel(BundledFixture fixture)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var gap = await browser.ExcludeCaptureAsync();
        Assert.False(browser.SyntheticStatus.Supported);
        Assert.Equal("SYNTHETIC_FIXTURE_REQUIRED", Assert.Throws<BrowserInputDeniedException>(() => browser.GrantSyntheticInput(gap, 1)).Code);
        Assert.False(browser.SyntheticStatus.AuthorityActive);
    }

    [Theory]
    [InlineData("unfocused")]
    [InlineData("disabled")]
    [InlineData("readonly")]
    [InlineData("wrong-type")]
    public async Task UnsupportedActualFocus_CannotReceiveTheFixedCanary(string caseId)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.SyntheticHandoff);
        if (caseId != "unfocused") await browser.PressKeyAsync("Tab");
        if (caseId == "disabled") await browser.DiagnosticPage.EvaluateAsync("() => document.querySelector('input').disabled = true");
        if (caseId == "readonly") await browser.DiagnosticPage.EvaluateAsync("() => document.querySelector('input').readOnly = true");
        if (caseId == "wrong-type") await browser.DiagnosticPage.EvaluateAsync("() => document.querySelector('input').type = 'text'");
        var gap = await browser.ExcludeCaptureAsync(); var lease = browser.GrantSyntheticInput(gap, 1);
        var result = await browser.ExecuteSyntheticInputAsync(lease, SyntheticInputAction.EnterCanary);
        Assert.Equal("SYNTHETIC_FOCUS_REQUIRED", result.ErrorCode); Assert.Equal(0, result.InputRequests);
        Assert.False(result.InputMayHaveOccurred); Assert.True(result.CanaryCleared);
        Assert.Equal("", await browser.DiagnosticPage.Locator("input").InputValueAsync());
        await browser.RevokeSyntheticInputAsync(lease);
    }

    [Fact]
    public async Task InvalidActionAndPreInputCancellation_CannotMutateTheProtectedFixture()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.SyntheticHandoff);
        await browser.PressKeyAsync("Tab");
        var gap = await browser.ExcludeCaptureAsync(); var lease = browser.GrantSyntheticInput(gap, 1);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.ExecuteSyntheticInputAsync(lease, (SyntheticInputAction)999));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await browser.ExecuteSyntheticInputAsync(lease, SyntheticInputAction.EnterCanary, cancellation.Token);
        Assert.Equal("SYNTHETIC_INPUT_CANCELLED", result.ErrorCode); Assert.Equal(0, result.InputRequests);
        Assert.False(result.InputMayHaveOccurred); Assert.True(result.CanaryCleared);
        Assert.Equal("", await browser.DiagnosticPage.Locator("input").InputValueAsync());
        await browser.RevokeSyntheticInputAsync(lease);
    }
}
