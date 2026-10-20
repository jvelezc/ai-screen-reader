using System.Security.Cryptography;
using System.Text;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumReferenceTests
{
    [Theory]
    [InlineData(BundledFixture.Foundation, "Screen reader fixture")]
    [InlineData(BundledFixture.Widgets, "Preference workflow")]
    public async Task ReferenceFixture_IdentifiesActualContentAndClosesWithoutAutomaticInput(
        BundledFixture fixture, string initialHeading)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var environment = browser.Environment;
        Assert.NotNull(environment.Content);
        Assert.Equal(fixture, environment.Content.Fixture);
        Assert.False(environment.Headed); // This automated harness check is not a headed NVDA run.
        using var stream = typeof(ChromiumFixtureBrowser).Assembly.GetManifestResourceStream(
            $"HeadlessScreenReader.Browser.Playwright.Fixtures.{fixture.ToString().ToLowerInvariant()}.html")!;
        using var resource = new StreamReader(stream);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(await resource.ReadToEndAsync())));
        Assert.Equal(expectedHash, environment.Content.ContentSha256);
        Assert.True(Version.TryParse(environment.BrowserVersion, out var version) && version.Major >= 100);
        var before = await browser.CaptureAsync();
        Assert.Contains(before.Nodes, node => node.Role == "heading" && node.Name == initialHeading);
        Assert.DoesNotContain(before.Nodes, node => node.Name is "Details opened" or "Preferences saved" || node.Role == "dialog");
        var closed = browser.WaitForCloseAsync(TimeSpan.FromSeconds(5));
        Assert.False(closed.IsCompleted);
        await browser.DisposeAsync();
        await closed;
        Assert.True(closed.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ReferenceFixture_ExposesHeadingLinkAndLandmarkNavigation()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        using var headings = new ScreenReaderSession(browser);
        Assert.Equal("Screen reader fixture, heading level 1", (await headings.ExecuteAsync(ReaderCommand.NextHeading)).Announcement);
        Assert.Equal("Controls, heading level 2", (await headings.ExecuteAsync(ReaderCommand.NextHeading)).Announcement);
        using var links = new ScreenReaderSession(browser);
        var link = await links.ExecuteAsync(ReaderCommand.NextLink);
        Assert.Equal("Read details, link", link.Announcement);
        Assert.Equal(0, link.BrowserKeystrokes);
        using var landmarks = new ScreenReaderSession(browser);
        Assert.Equal("Primary, navigation", (await landmarks.ExecuteAsync(ReaderCommand.NextLandmark)).Announcement);
        Assert.Equal("main", (await landmarks.ExecuteAsync(ReaderCommand.NextLandmark)).Announcement);
        Assert.DoesNotContain((await browser.CaptureAsync()).Nodes, node => node.Name == "Details opened");
    }
}
