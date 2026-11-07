using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>A closed authored lab task. A plan or expected result is not a recorded finding.</summary>
public sealed record MockSiteTask(string Id, BundledFixture Fixture, string SourceSha256,
    string Heading, string GoalName)
{
    public TraversalPlan KeyboardPlan(string key)
    {
        if (key is not ("Enter" or "Space")) throw new ArgumentException("Only Enter and Space lab activation are supported.", nameof(key));
        return new($"{Id}: keyboard {key}", new("heading", Heading), new("heading", GoalName, MustBeFocused: true),
            key == "Enter" ? [new(ReaderCommand.Tab), new(ReaderCommand.Activate)]
                : [new(ReaderCommand.Tab), new(ReaderCommand.EnterFocusMode), new(ReaderCommand.PressKey, key)]);
    }
}

/// <summary>Exact embedded v1 sources; other fixtures, URLs and synthetic credential channels are excluded.</summary>
public static class MockSiteTasks
{
    public static ImmutableArray<BundledFixture> Fixtures => [BundledFixture.MockCheckoutWorkingV1,
        BundledFixture.MockCheckoutKeyboardBlockedV1, BundledFixture.MockNamedControlV1, BundledFixture.MockUnnamedControlV1];

    public static MockSiteTask Describe(BundledFixture fixture)
    {
        var task = fixture switch
        {
            BundledFixture.MockCheckoutWorkingV1 => new MockSiteTask("checkout-working", fixture,
                "70f779f8bb6ffeac7734677b05e3c5e13df28fa7aff6c095db7dcda2b10695bf", "Reference checkout", "Sample order placed"),
            BundledFixture.MockCheckoutKeyboardBlockedV1 => new("checkout-keyboard-blocked", fixture,
                "7729a19422eff5e111d16d2c4394724b6732cebde3f195dc6cbd9ce00785ad39", "Keyboard-blocked checkout", "Sample order placed"),
            BundledFixture.MockNamedControlV1 => new("control-named", fixture,
                "8cb47651cf45f562dd0da7ef465fadc0f248e9ab27a420a7e3a1f6301f396bbc", "Named details control", "Sample details opened"),
            BundledFixture.MockUnnamedControlV1 => new("control-unnamed", fixture,
                "711e83f678990642c42a5e1b267c1abecad3804af7a1b7007de2fe551d7bc4bb", "Unnamed details control", "Sample details opened"),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture), "Only the four v1 mock sites are supported.")
        };
        if (BundledFixtureCatalog.Describe(fixture).ContentSha256 != task.SourceSha256)
            throw new BrowserOperationException("MOCK_TASK_SOURCE_CHANGED");
        return task;
    }
}
