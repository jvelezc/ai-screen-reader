using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class TypeaheadSessionTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("space")]
    [InlineData("punctuation")]
    [InlineData("unicode")]
    [InlineData("control")]
    [InlineData("long")]
    public async Task InvalidPrefix_IsDeniedWithoutCaptureOrInput(string caseId)
    {
        var prefix = caseId switch
        {
            "null" => null,
            "empty" => "",
            "space" => "p e",
            "punctuation" => "p+e",
            "unicode" => "é",
            "control" => "p\n",
            _ => new string('p', 17)
        };
        var browser = new TypeaheadBrowser();
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Typeahead, prefix);
        Assert.Equal(ReaderOutcome.Denied, step.Outcome);
        Assert.Equal("TYPEAHEAD_PREFIX_INVALID", step.ErrorCode);
        Assert.False(step.InputMayHaveOccurred);
        Assert.Equal(0, browser.Captures);
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task Typeahead_WaitsExactIdleBoundaryThenVerifiesActualFocusWithoutChangingSelection()
    {
        var browser = new TypeaheadBrowser();
        var time = new FakeTimeProvider();
        using var reader = new ScreenReaderSession(browser, timeProvider: time);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var task = reader.ExecuteAsync(ReaderCommand.Typeahead, "PE", expectation: new("option", "Pears", MustBeFocused: true));
        time.Advance(TimeSpan.FromMilliseconds(1099));
        Assert.False(task.IsCompleted);
        Assert.Empty(browser.Keys);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var step = await task;
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
        Assert.Equal(VerificationStatus.Satisfied, step.Verification);
        Assert.Equal(new[] { "p", "e" }, browser.Keys);
        Assert.Equal(2, step.BrowserKeystrokes);
        Assert.Equal("list", step.FocusedNodeId);
        Assert.Equal("b", step.ActiveDescendantNodeId);
        Assert.Equal("b", step.CursorNodeId);
        Assert.Equal(new TypeaheadObservation("pe", 2, "list", "b", true), step.Typeahead);
        Assert.True(step.ListboxSelection!.Options.Single(n => n.NodeId == "a").Selection!.Value);
        Assert.False(step.ListboxSelection.Options.Single(n => n.NodeId == "b").Selection!.Value);
        Assert.Equal(new[] { "p", "e" }, reader.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail));
        Assert.Empty(browser.RawKeys);
    }

    [Fact]
    public async Task MaximumPrefix_SendsExactlySixteenCharacters()
    {
        var browser = new TypeaheadBrowser();
        browser.Replace("b", n => n with { Name = "abcdefghijklmnop target" });
        var time = new FakeTimeProvider();
        using var reader = new ScreenReaderSession(browser, timeProvider: time);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await Send(reader, time, "abcdefghijklmnop");
        Assert.Equal(16, step.BrowserKeystrokes);
        Assert.Equal("abcdefghijklmnop", string.Concat(browser.Keys));
        Assert.True(step.Typeahead!.PrefixMatchesObservedFocus);
    }

    [Fact]
    public async Task NumericPrefix_IsScopedAndObservedWithoutEnablingRawDigitKeys()
    {
        var browser = new TypeaheadBrowser();
        browser.Replace("b", n => n with { Name = "90 choices" });
        var time = new FakeTimeProvider();
        using var reader = new ScreenReaderSession(browser, timeProvider: time);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await Send(reader, time, "90");
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
        Assert.Equal(new[] { "9", "0" }, browser.Keys);
        Assert.Equal(2, step.BrowserKeystrokes);
        Assert.True(step.Typeahead!.PrefixMatchesObservedFocus);
        Assert.False(BrowserKeyPolicy.Allows("9"));
        Assert.False(BrowserKeyPolicy.Allows("0"));
    }

    [Fact]
    public async Task UnmatchedObservedFocus_IsUnknownWithCompletedInputAndUnchangedSelection()
    {
        var browser = new TypeaheadBrowser { MoveFocus = false };
        var time = new FakeTimeProvider();
        using var reader = new ScreenReaderSession(browser, timeProvider: time);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await Send(reader, time, "zz");
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.False(step.Typeahead!.PrefixMatchesObservedFocus);
        Assert.Equal("a", step.Typeahead.ObservedOptionId);
        Assert.Equal(2, step.BrowserKeystrokes);
        Assert.True(step.InputMayHaveOccurred);
        Assert.Null(step.BarrierCode);
        Assert.Equal(new[] { "a" }, step.ListboxSelection!.Options.Where(n => n.Selection?.Value == true).Select(n => n.NodeId));
    }

    [Fact]
    public async Task BrowseMode_CannotTurnPrefixIntoInput()
    {
        var browser = new TypeaheadBrowser();
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.Typeahead, "pe");
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("a")]
    public async Task DisabledFocusOrOwner_BlocksBeforeInput(string disabledId)
    {
        var browser = new TypeaheadBrowser();
        browser.Replace(disabledId, n => n with { IsDisabled = true });
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await reader.ExecuteAsync(ReaderCommand.Typeahead, "pe");
        Assert.Equal(ReaderOutcome.Blocked, step.Outcome);
        Assert.Equal("OBSERVED_DISABLED_CONTROL", step.BarrierCode);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task FocusChangedDuringReset_IsDeniedBeforeFirstCharacter()
    {
        var browser = new TypeaheadBrowser();
        var time = new FakeTimeProvider();
        using var reader = new ScreenReaderSession(browser, timeProvider: time);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var task = reader.ExecuteAsync(ReaderCommand.Typeahead, "pe");
        browser.Replace("list", n => n with { IsFocused = false });
        time.Advance(TimeSpan.FromMilliseconds(1100));
        var step = await task;
        Assert.Equal(ReaderOutcome.Denied, step.Outcome);
        Assert.Equal("TYPEAHEAD_SCOPE_CHANGED", step.ErrorCode);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("policy")]
    [InlineData("browser")]
    [InlineData("cancel")]
    public async Task MidPrefixFailure_PreservesCompletedCharactersAndStopsFurtherInput(string failure)
    {
        using var cancel = new CancellationTokenSource();
        var browser = new TypeaheadBrowser();
        browser.AfterKey = () =>
        {
            if (failure == "scope") browser.Snapshot = browser.Snapshot with { DocumentId = "changed" };
            if (failure == "cancel") cancel.Cancel();
        };
        browser.SecondKeyFailure = failure;
        var time = new FakeTimeProvider();
        using var reader = new ScreenReaderSession(browser, timeProvider: time);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var task = reader.ExecuteAsync(ReaderCommand.Typeahead, "pear", cancel.Token);
        time.Advance(TimeSpan.FromMilliseconds(1100));
        var step = await task;
        Assert.Equal(failure == "browser" ? ReaderOutcome.BrowserError : ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal(1, step.BrowserKeystrokes);
        Assert.True(step.InputMayHaveOccurred);
        Assert.Null(step.BarrierCode);
        Assert.Equal(new[] { "p" }, browser.Keys);
        Assert.Single(reader.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted));
        if (failure == "policy") Assert.Contains(reader.Timeline, e => e.Kind == ReaderEventKind.InputDenied);
        if (failure == "cancel") Assert.Equal(VerificationStatus.Interrupted, step.Verification);
    }

    [Fact]
    public async Task CancellationDuringReset_DoesNotDispatchInput()
    {
        using var cancel = new CancellationTokenSource();
        var browser = new TypeaheadBrowser();
        var time = new FakeTimeProvider();
        using var reader = new ScreenReaderSession(browser, timeProvider: time);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var task = reader.ExecuteAsync(ReaderCommand.Typeahead, "pe", cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    internal static Task<ReaderStep> Send(ScreenReaderSession reader, FakeTimeProvider time, string prefix)
    {
        var task = reader.ExecuteAsync(ReaderCommand.Typeahead, prefix);
        time.Advance(TimeSpan.FromMilliseconds(1100));
        return task;
    }
}

internal sealed class TypeaheadBrowser : IScreenReaderBrowser
{
    public AccessibilitySnapshot Snapshot { get; set; } = new("doc", [
        new("root", "RootWebArea", "Test", FrameId: "frame", DocumentId: "doc"),
        new("list", "listbox", "Fruit", IsFocused: true, ActiveDescendantId: "a", ParentId: "root", FrameId: "frame", DocumentId: "doc",
            MultiSelectable: new(true, SemanticSource.ChromiumAccessibility)),
        new("a", "option", "Apples", Selected: true, Selection: new(true, SemanticSource.ChromiumAccessibility), ParentId: "list", FrameId: "frame", DocumentId: "doc"),
        new("b", "option", "Pears", Selected: false, Selection: new(false, SemanticSource.ChromiumAccessibility), ParentId: "list", FrameId: "frame", DocumentId: "doc")], []);
    public List<string> Keys { get; } = [];
    public List<string> RawKeys { get; } = [];
    public int Captures { get; private set; }
    public bool MoveFocus { get; set; } = true;
    public Action? AfterKey { get; set; }
    public string? SecondKeyFailure { get; set; }
    public void Replace(string id, Func<AccessibleNode, AccessibleNode> change) =>
        Snapshot = Snapshot with { Nodes = Snapshot.Nodes.Select(n => n.Id == id ? change(n) : n).ToImmutableArray() };
    public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken token = default)
    { token.ThrowIfCancellationRequested(); Captures++; return Task.FromResult(Snapshot); }
    public Task PressKeyAsync(string key, CancellationToken token = default)
    { RawKeys.Add(key); return Task.CompletedTask; }
    public Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken token = default)
    {
        if (Keys.Count == 1 && SecondKeyFailure == "policy") throw new BrowserInputDeniedException("TEST_POLICY_DENIAL");
        if (Keys.Count == 1 && SecondKeyFailure == "browser") throw new BrowserOperationException("TEST_INPUT_FAILURE");
        Keys.Add(input.Key);
        if (MoveFocus) Replace("list", n => n with { ActiveDescendantId = "b" });
        AfterKey?.Invoke();
        return Task.CompletedTask;
    }
}
