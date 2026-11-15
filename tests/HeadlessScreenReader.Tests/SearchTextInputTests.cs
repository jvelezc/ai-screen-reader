using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class SearchTextInputTests
{
    [Theory]
    [InlineData("Tab")]
    [InlineData("a")]
    [InlineData("Alt+F4")]
    public void SyntheticSearchPlansCannotAcceptOtherActivationKeys(string key) =>
        Assert.Throws<ArgumentException>(() => SyntheticSearchPolicy.KeyboardPlan(key));

    internal static AccessibilitySnapshot Snapshot() => new("search-document",
        [new("search", "searchbox", "Search courses", IsFocused: true,
            Editability: new("plaintext", SemanticSource.ChromiumAccessibility))], [])
    { CaptureEpoch = 4 };

    [Theory]
    [InlineData("document")]
    [InlineData("epoch")]
    [InlineData("node")]
    [InlineData("focus")]
    [InlineData("role")]
    [InlineData("disabled")]
    [InlineData("noneditable")]
    [InlineData("richtext")]
    [InlineData("protected")]
    [InlineData("descendant")]
    [InlineData("wrong-character")]
    [InlineData("wrong-prefix")]
    [InlineData("finished-prefix")]
    public void SearchKeyRequiresTheSameEditableUnprotectedSearchFocus(string boundary)
    {
        var snapshot = Snapshot();
        var input = new SearchKeyInput("s", snapshot.DocumentId, snapshot.CaptureEpoch, "search", "");
        Assert.True(input.IsAuthorizedBy(snapshot));
        var node = snapshot.Nodes[0];
        switch (boundary)
        {
            case "document": snapshot = snapshot with { DocumentId = "changed" }; break;
            case "epoch": snapshot = snapshot with { CaptureEpoch = 5 }; break;
            case "node": input = input with { NodeId = "other" }; break;
            case "focus": node = node with { IsFocused = false }; break;
            case "role": node = node with { Role = "textbox" }; break;
            case "disabled": node = node with { IsDisabled = true }; break;
            case "noneditable": node = node with { Editability = null }; break;
            case "richtext": node = node with { Editability = new("richtext", SemanticSource.ChromiumAccessibility) }; break;
            case "protected": node = node with { Protection = new(true, SemanticSource.ChromiumAccessibility) }; break;
            case "descendant": node = node with { ActiveDescendantId = "unknown" }; break;
            case "wrong-character": input = input with { Key = "x" }; break;
            case "wrong-prefix": input = input with { ExpectedPrefix = "someone else's content" }; break;
            case "finished-prefix": input = input with { ExpectedPrefix = SyntheticSearchPolicy.Query }; break;
            default: throw new ArgumentOutOfRangeException(nameof(boundary));
        }
        snapshot = snapshot with { Nodes = [node] };
        Assert.False(input.IsAuthorizedBy(snapshot));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("partial")]
    [InlineData("different")]
    [InlineData("control")]
    [InlineData("long")]
    public async Task SearchTextRequiresTheDeclaredSyntheticQuery(string boundary)
    {
        string? text = boundary switch
        {
            "null" => null,
            "empty" => "",
            "partial" => "sample",
            "different" => "password",
            "control" => "sample course\n",
            _ => new('x', 1000)
        };
        var browser = new SearchBrowser();
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, text);
        Assert.Equal(ReaderOutcome.Denied, step.Outcome);
        Assert.Equal("SEARCH_TEXT_INVALID", step.ErrorCode);
        Assert.Equal(0, browser.Captures);
        Assert.Empty(browser.Keys);
        Assert.False(step.InputMayHaveOccurred);
        Assert.Equal(0, step.BrowserKeystrokes);
    }

    [Fact]
    public async Task SearchTextRequiresFocusAndAnExplicitBrowserCapability()
    {
        var browser = new SearchBrowser();
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal(ReaderOutcome.Unsupported, (await Enter(reader)).Outcome);
        Assert.Empty(browser.Keys);
        using var unsupported = new ScreenReaderSession(new ObserveOnly());
        await unsupported.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(ReaderOutcome.Unsupported, (await Enter(unsupported)).Outcome);
    }

    [Fact]
    public async Task SearchTextRetainsEveryCompletedCharacterAndVerifiesValueWithoutClaimingSubmission()
    {
        var browser = new SearchBrowser();
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await Enter(reader);
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
        Assert.Equal(new SearchTextObservation(SyntheticSearchPolicy.Profile, SyntheticSearchPolicy.Query, 13, true), step.SearchInput);
        Assert.Equal(SyntheticSearchPolicy.Query, string.Concat(browser.Keys));
        Assert.Equal(13, step.BrowserKeystrokes);
        Assert.False(step.InputMayHaveOccurred);
        Assert.Equal(VerificationStatus.NotRequested, step.Verification);
        Assert.Null(step.BarrierCode);
        Assert.Contains("has not been submitted", step.Announcement);
        Assert.Equal(SyntheticSearchPolicy.Query, string.Concat(reader.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail)));
        Assert.Equal(1, browser.ValueChecks);
    }

    [Fact]
    public async Task SearchTextStopsAfterFocusChangesWithoutRetry()
    {
        var browser = new SearchBrowser { LoseFocusAfter = 1 };
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await Enter(reader);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal("SEARCH_SCOPE_CHANGED", step.ErrorCode);
        Assert.Equal(new[] { "s" }, browser.Keys);
        Assert.Equal(1, step.BrowserKeystrokes);
        Assert.Equal(1, step.SearchInput!.CompletedCharacters);
        Assert.False(step.SearchInput.ValueVerified);
        Assert.True(step.InputMayHaveOccurred);
        Assert.Equal(0, browser.ValueChecks);
        Assert.Null(step.BarrierCode);
    }

    [Fact]
    public async Task SearchTextPreservesUnconfirmedNativeInputWithoutRetry()
    {
        var browser = new SearchBrowser { ThrowOnCharacter = 2 };
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await Enter(reader);
        Assert.Equal(ReaderOutcome.BrowserError, step.Outcome);
        Assert.Equal("CHROMIUM_INPUT_FAILED", step.ErrorCode);
        Assert.Equal(new[] { "s", "a" }, browser.Keys);
        Assert.Equal(1, step.BrowserKeystrokes);
        Assert.True(step.InputMayHaveOccurred);
        Assert.Null(step.BarrierCode);
        Assert.Equal(2, reader.Timeline.Count(e => e.Kind == ReaderEventKind.InputAttempted));
        Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.InputCompleted);
        Assert.Equal(0, browser.ValueChecks);
    }

    [Fact]
    public async Task SearchTextDoesNotInferValueOrGoalFromCompletedKeys()
    {
        var browser = new SearchBrowser { ValueMatches = false };
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await Enter(reader);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal(13, step.BrowserKeystrokes);
        Assert.False(step.SearchInput!.ValueVerified);
        Assert.True(step.InputMayHaveOccurred);
        Assert.Equal(VerificationStatus.NotRequested, step.Verification);
        Assert.Null(step.BarrierCode);
        Assert.Equal(1, browser.ValueChecks);
    }

    [Fact]
    public async Task SearchTextCancellationPreservesTheCompletedCharacterWithoutRetry()
    {
        using var cancel = new CancellationTokenSource();
        var browser = new SearchBrowser { AfterKey = cancel.Cancel };
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query, cancel.Token);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal(VerificationStatus.Interrupted, step.Verification);
        Assert.Equal(1, step.BrowserKeystrokes);
        Assert.True(step.InputMayHaveOccurred);
        Assert.Equal(new[] { "s" }, browser.Keys);
        Assert.Equal(0, browser.ValueChecks);
        Assert.Null(step.BarrierCode);
    }

    [Fact]
    public async Task PublicReaderWrapperCannotForwardSyntheticSearchEntry()
    {
        var browser = new SearchBrowser();
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser, linksOnly: true));
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await Enter(reader);
        Assert.Equal(ReaderOutcome.Denied, step.Outcome);
        Assert.Equal("SYNTHETIC_SEARCH_FIXTURE_REQUIRED", step.ErrorCode);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.False(step.InputMayHaveOccurred);
        Assert.Empty(browser.Keys);
        Assert.Equal(0, browser.ValueChecks);
    }

    [Fact]
    public async Task LegacyReaderStepsOmitTheSearchExtension()
    {
        using var reader = new ScreenReaderSession(new ObserveOnly());
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(step));
        Assert.False(json.RootElement.TryGetProperty("SearchInput", out _));
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
        Assert.Equal("hsr-desktop-reader-v13", ScreenReaderSession.Profile);
    }

    private static Task<ReaderStep> Enter(ScreenReaderSession reader) => reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query);

    private sealed class ObserveOnly : IScreenReaderBrowser
    {
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot());
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No raw key is expected.");
    }

    private sealed class SearchBrowser : IScreenReaderBrowser, ISyntheticSearchBrowser
    {
        public List<string> Keys { get; } = [];
        public int Captures { get; private set; }
        public int ValueChecks { get; private set; }
        public int? LoseFocusAfter { get; init; }
        public int? ThrowOnCharacter { get; init; }
        public bool ValueMatches { get; init; } = true;
        public Action? AfterKey { get; init; }
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Captures++;
            var snapshot = Snapshot();
            return Task.FromResult(LoseFocusAfter is { } count && Keys.Count >= count
                ? snapshot with { Nodes = [snapshot.Nodes[0] with { IsFocused = false }] } : snapshot);
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No raw key is expected.");
        public Task PressSearchKeyAsync(SearchKeyInput input, CancellationToken cancellationToken = default)
        {
            Assert.Equal(string.Concat(Keys), input.ExpectedPrefix);
            Keys.Add(input.Key);
            if (Keys.Count == ThrowOnCharacter) throw new BrowserOperationException("CHROMIUM_INPUT_FAILED");
            AfterKey?.Invoke();
            return Task.CompletedTask;
        }
        public Task<bool> SearchValueMatchesAsync(SearchKeyInput scope, string expected, CancellationToken cancellationToken = default)
        {
            ValueChecks++;
            Assert.Equal(SyntheticSearchPolicy.Query, expected);
            return Task.FromResult(ValueMatches);
        }
    }
}
