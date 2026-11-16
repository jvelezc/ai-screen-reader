using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class SyntheticSearchPointerTests
{
    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1)]
    public async Task ActualMouseSearchFocusAndActivationHaveIndependentReceipts(BundledFixture fixture)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var focus = await browser.FocusSyntheticSearchAsync();
        Assert.True(focus.InputAttempted);
        Assert.True(focus.InputCompleted);
        Assert.True(focus.FocusObserved);
        Assert.Null(focus.ErrorCode);
        Assert.Equal("NativeMouseFocus", focus.Mechanism);
        Assert.Equal("Search courses", focus.After!.FocusedNode!.Name);
        Assert.Equal(focus.Before.DocumentId, focus.After.DocumentId);
        var readerStarted = DateTimeOffset.UtcNow;
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser));
        var mode = await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(0, mode.BrowserKeystrokes);
        var entered = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query);
        Assert.True(entered.SearchInput!.ValueVerified);
        Assert.Equal(13, entered.BrowserKeystrokes);
        var snapshot = await browser.CaptureAsync();
        var scope = new SearchKeyInput("e", snapshot.DocumentId, snapshot.CaptureEpoch, snapshot.FocusedNode!.Id, SyntheticSearchPolicy.Query[..^1]);
        var pointer = await browser.ObserveSyntheticSearchPointerAsync(scope);
        Assert.True(pointer.Attempted);
        Assert.True(pointer.Completed);
        Assert.True(pointer.GoalObserved);
        Assert.Null(pointer.ErrorCode);
        Assert.Equal("Sample course result", pointer.After!.FocusedNode!.Name);
        Assert.Equal(focus.Before.DocumentId, pointer.Before.DocumentId);
        Assert.Equal(focus.Before.DocumentId, pointer.After.DocumentId);
        Assert.Equal(13, reader.Timeline.Count(e => e.Kind == ReaderEventKind.InputCompleted));
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputCompleted && e.Detail is "Tab" or "Enter" or "Space");
        var preparation = new SearchPointerPreparation(focus, readerStarted, entered, reader.Timeline, pointer.Before, null);
        var graph = SyntheticSearchPointerProjection.Build(preparation, pointer);
        Assert.Equal(4, graph.Nodes.Length);
        Assert.Equal(new[] { "NativeMouseFocus", "ReaderKeyboardText", "NativeMouseActivation" }, graph.Edges.Select(e => e.Mechanism));
        Assert.Equal(new[] { 1, 13, 1 }, graph.Edges.Select(e => e.CompletedNativeCalls));
        Assert.All(graph.Edges, e => { Assert.True(e.InputEvidenceComplete); Assert.False(e.InputUnconfirmed); });
        Assert.Equal(SyntheticSearchPolicy.Query, string.Concat(graph.Edges[1].KeyboardInputs.Select(e => e.Key)));
    }

    [Fact]
    public async Task MouseSearchCannotActivateWithoutTheObservedQuery()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.MockSearchKeyboardBlockedV1);
        var focused = await browser.FocusSyntheticSearchAsync();
        Assert.True(focused.FocusObserved);
        var scope = new SearchKeyInput("e", focused.After!.DocumentId, focused.After.CaptureEpoch, focused.After.FocusedNode!.Id, SyntheticSearchPolicy.Query[..^1]);
        var denied = await browser.ObserveSyntheticSearchPointerAsync(scope);
        Assert.False(denied.Attempted);
        Assert.False(denied.Completed);
        Assert.False(denied.GoalObserved);
        Assert.Equal("SEARCH_SCOPE_CHANGED", denied.ErrorCode);
        Assert.Null(denied.After);
        Assert.DoesNotContain((await browser.CaptureAsync()).Nodes, n => n.Name == "Sample course result");
    }

    [Fact]
    public async Task MouseSearchCannotSilentlyReplaceAnExistingQuery()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.MockSearchWorkingV1);
        Assert.True((await browser.FocusSyntheticSearchAsync()).FocusObserved);
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser));
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.True((await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query)).SearchInput!.ValueVerified);
        var denied = await browser.FocusSyntheticSearchAsync();
        Assert.False(denied.InputAttempted);
        Assert.False(denied.InputCompleted);
        Assert.False(denied.FocusObserved);
        Assert.Equal("SYNTHETIC_SEARCH_FRESH_STATE_REQUIRED", denied.ErrorCode);
        Assert.Equal(SyntheticSearchPolicy.Query, await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
    }
}
