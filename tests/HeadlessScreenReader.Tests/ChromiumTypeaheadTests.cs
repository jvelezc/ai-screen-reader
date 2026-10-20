using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumTypeaheadTests
{
    [Theory]
    [InlineData("PE", "Peaches", 2)]
    [InlineData("abcdefghijklmnop", "abcdefghijklmnop target", 16)]
    public async Task ChromiumPrefix_UsesActualFocusedListboxAndPreservesMultiselection(string prefix, string expected, int count)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Typeahead);
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser));
        await reader.ExecuteAsync(ReaderCommand.NextListbox);
        var unfocused = await reader.ExecuteAsync(ReaderCommand.Typeahead, prefix);
        Assert.Equal(ReaderOutcome.Unsupported, unfocused.Outcome);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var step = await reader.ExecuteAsync(ReaderCommand.Typeahead, prefix,
            expectation: new("option", expected, MustBeFocused: true, Selected: false));
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.Equal(count, step.BrowserKeystrokes);
        Assert.True(step.Typeahead!.PrefixMatchesObservedFocus);
        var snapshot = await browser.CaptureAsync();
        Assert.Equal(expected, snapshot.EffectiveFocusedNode!.Name);
        Assert.Equal("Fruit search", snapshot.FocusedNode!.Name);
        Assert.Equal(snapshot.EffectiveFocusedNode.Id, step.CursorNodeId);
        Assert.Equal("Apples", Assert.Single(step.ListboxSelection!.Options, n => n.Selection?.Value == true).Name);
        Assert.DoesNotContain(snapshot.Nodes, n => n.Name == "Peanut hidden");
        var raw = await Assert.ThrowsAsync<BrowserOperationException>(() => browser.PressKeyAsync("p"));
        Assert.Equal("KEY_DENIED", raw.Code);
    }

    [Fact]
    public async Task RepeatedPrefix_ResetsBufferAndVisitsOtherExposedMatchButNeverDisabledOrHiddenOption()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Typeahead);
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser));
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var first = await reader.ExecuteAsync(ReaderCommand.Typeahead, "pe");
        var repeated = await reader.ExecuteAsync(ReaderCommand.Typeahead, "pe");
        Assert.True(first.Typeahead!.PrefixMatchesObservedFocus);
        Assert.True(repeated.Typeahead!.PrefixMatchesObservedFocus);
        Assert.NotEqual(first.CursorNodeId, repeated.CursorNodeId);
        Assert.Contains("Pears", repeated.Announcement);
        var disabledOnly = await reader.ExecuteAsync(ReaderCommand.Typeahead, "pl");
        Assert.Equal(ReaderOutcome.Unknown, disabledOnly.Outcome);
        Assert.False(disabledOnly.Typeahead!.PrefixMatchesObservedFocus);
        Assert.Equal(2, disabledOnly.BrowserKeystrokes);
        Assert.Null(disabledOnly.BarrierCode);
        Assert.NotEqual("Plums", (await browser.CaptureAsync()).EffectiveFocusedNode!.Name);
        Assert.Equal("Apples", Assert.Single(disabledOnly.ListboxSelection!.Options, n => n.Selection?.Value == true).Name);
    }

    [Fact]
    public async Task RovingPrefix_ObservesRealOptionFocusWithoutInventingSelection()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Typeahead);
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser));
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var step = await reader.ExecuteAsync(ReaderCommand.Typeahead, "pep",
            expectation: new("option", "Peppers", MustBeFocused: true, Selected: false));
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.Equal(3, step.BrowserKeystrokes);
        Assert.Null(step.ActiveDescendantNodeId);
        Assert.Equal(step.FocusedNodeId, step.CursorNodeId);
        Assert.Equal("Carrots", Assert.Single(step.ListboxSelection!.Options, n => n.Selection?.Value == true).Name);
    }

    [Fact]
    public async Task SelectionFollowsFocus_IsReportedAsObservedWidgetBehavior()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Typeahead);
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser));
        for (var i = 0; i < 3; i++) await reader.ExecuteAsync(ReaderCommand.Tab);
        var step = await reader.ExecuteAsync(ReaderCommand.Typeahead, "r",
            expectation: new("option", "Red", MustBeFocused: true, Selected: true));
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.True(step.Typeahead!.PrefixMatchesObservedFocus);
        Assert.Equal("Red", Assert.Single(step.ListboxSelection!.Options, n => n.Selection?.Value == true).Name);
        Assert.False(step.ListboxSelection.Options.Single(n => n.Name == "Blue").Selection!.Value);
    }

    [Fact]
    public async Task EditableOverride_AndStaleDocumentCannotAuthorizeTypedInput()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Typeahead);
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser));
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var snapshot = await browser.CaptureAsync();
        var context = ListboxTypeaheadPolicy.FocusContext(snapshot)!;
        var denied = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressListboxKeyAsync(
            new("p", "stale", context.ListboxNodeId, context.DocumentId, context.FrameId)));
        Assert.Equal("TYPEAHEAD_SCOPE_CHANGED", denied.Code);
        Assert.Equal(snapshot.EffectiveFocusedNode!.Id, (await browser.CaptureAsync()).EffectiveFocusedNode!.Id);
        for (var i = 0; i < 3; i++) await reader.ExecuteAsync(ReaderCommand.Tab);
        var step = await reader.ExecuteAsync(ReaderCommand.Typeahead, "e");
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Null(step.BarrierCode);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted && e.Command == ReaderCommand.Typeahead);
    }
}
