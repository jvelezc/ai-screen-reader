using System.Security.Cryptography;
using System.Text;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record BundledFixtureIdentity(BundledFixture Fixture, string ContentSha256);

public sealed record FixtureBrowserEnvironment(
    string BrowserVersion, string PlaywrightVersion, bool Headed, BundledFixtureIdentity Content,
    FrameCaptureOptions? FrameLimits = null);

/// <summary>Only trusted, embedded fixtures. Content hashes identify the UTF-8 text sent to Chromium.</summary>
public static class BundledFixtureCatalog
{
    public static string ReadHtml(BundledFixture fixture)
    {
        if (!Enum.IsDefined(fixture)) throw new ArgumentOutOfRangeException(nameof(fixture));
        using var stream = typeof(BundledFixtureCatalog).Assembly.GetManifestResourceStream(
            $"HeadlessScreenReader.Browser.Playwright.Fixtures.{fixture.ToString().ToLowerInvariant()}.html")
            ?? throw new InvalidOperationException("Bundled fixture is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static BundledFixtureIdentity Describe(BundledFixture fixture) =>
        new(fixture, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ReadHtml(fixture)))));
}
