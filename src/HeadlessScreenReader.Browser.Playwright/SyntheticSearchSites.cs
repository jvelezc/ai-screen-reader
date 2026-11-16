using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record SyntheticSearchSite(string Id, BundledFixture Fixture, string SourceSha256)
{
    public string Profile => SyntheticSearchPolicy.Profile;
}

/// <summary>Exact compiled search examples. They do not extend the v1 admitted mock-source catalogue.</summary>
public static class SyntheticSearchSites
{
    public static ImmutableArray<BundledFixture> Fixtures => [BundledFixture.MockSearchWorkingV1, BundledFixture.MockSearchKeyboardBlockedV1];

    public static SyntheticSearchSite Describe(BundledFixture fixture)
    {
        var source = fixture switch
        {
            BundledFixture.MockSearchWorkingV1 => new SyntheticSearchSite("search-working", fixture,
                "b6de520a94841c634d567a30a3a5809a7d51f0dee43354c1e6f6297ef35a2f12"),
            BundledFixture.MockSearchKeyboardBlockedV1 => new("search-keyboard-blocked", fixture,
                "e486f3e0e3fd694cd80fbad8ab6955e29ae19b41b52f98bafab7fd0af5b19d9f"),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture), "Only the two embedded synthetic search sources are supported.")
        };
        if (BundledFixtureCatalog.Describe(fixture).ContentSha256 != source.SourceSha256)
            throw new BrowserOperationException("SYNTHETIC_SEARCH_SOURCE_CHANGED");
        return source;
    }
}
