using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumSearchInputTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1, "Enter", true)]
    [InlineData(BundledFixture.MockSearchWorkingV1, "Space", true)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, "Enter", false)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, "Space", false)]
    public async Task ActualSearchTraversalRetainsEveryCharacterOnTheTypedEdge(BundledFixture fixture, string activation, bool reached)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var receipt = await new TraversalRunner().RunAsync(new NoEditableEntryBrowser(browser), SyntheticSearchPolicy.KeyboardPlan(activation),
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(300), PollInterval = TimeSpan.FromMilliseconds(50), MaxPolls = 3 });
        Assert.Equal(TraversalExecution.Completed, receipt.Execution);
        Assert.Equal(reached ? TraversalReachability.Reached : TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(16, receipt.CompletedKeystrokes);
        Assert.Equal(16, receipt.AttemptedKeystrokes);
        Assert.False(receipt.HasRecording);
        var graph = TraversalProjection.Build(receipt);
        Assert.Equal(activation == "Enter" ? 4 : 5, graph.Edges.Length);
        Assert.Equal(graph.Edges.Length + 1, graph.Nodes.Length);
        var typed = Assert.Single(graph.Edges, e => e.Command == ReaderCommand.EnterSearchText);
        Assert.Equal(13, typed.CompletedKeystrokes);
        Assert.True(typed.InputEvidenceComplete);
        Assert.False(typed.InputMayHaveOccurred);
        Assert.Equal(SyntheticSearchPolicy.Query, string.Concat(typed.KeyboardInputs.Select(k => k.Key)));
        Assert.All(typed.KeyboardInputs, k =>
        {
            Assert.NotNull(k.AttemptEventSequence);
            Assert.NotNull(k.CompletionEventSequence);
            Assert.True(k.CompletionEventSequence > k.AttemptEventSequence);
        });
        Assert.True(receipt.Attempts.Single(a => a.Instruction.Command == ReaderCommand.EnterSearchText).Step!.SearchInput!.ValueVerified);
        Assert.All(graph.Edges, e => Assert.True(e.InputEvidenceComplete));
    }

    [Fact]
    public async Task ActualReaderSearchTypesTheFixedQueryAndVerifiesItsNativeValue()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.MockSearchWorkingV1);
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser));
        var focus = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(ReaderMode.Focus, focus.Mode);
        Assert.Contains("Search courses", focus.Announcement);
        await browser.DiagnosticPage.EvaluateAsync("""
            () => {
              window.searchKeyEvents = [];
              const field = document.getElementById('sample-search');
              for (const kind of ['keydown','input','keyup'])
                field.addEventListener(kind, event => window.searchKeyEvents.push({kind, key:event.key ?? null}));
            }
            """);
        var step = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { step, Focus = (await browser.CaptureAsync()).FocusedNode }));
        Assert.Equal(ReaderOutcome.Observed, step.Outcome);
        Assert.Equal(13, step.BrowserKeystrokes);
        Assert.True(step.SearchInput!.ValueVerified);
        Assert.Equal(SyntheticSearchPolicy.Query, await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
        Assert.Equal(SyntheticSearchPolicy.Query, await browser.DiagnosticPage.EvaluateAsync<string>(
            "() => window.searchKeyEvents.filter(e=>e.kind==='keydown').map(e=>e.key).join('')"));
        Assert.Equal(13, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.searchKeyEvents.filter(e=>e.kind==='input').length"));
        Assert.Equal(13, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.searchKeyEvents.filter(e=>e.kind==='keyup').length"));
        Assert.DoesNotContain((await browser.CaptureAsync()).Nodes, n => n.Name == "Sample course result");
        var repeated = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query);
        Assert.Equal(ReaderOutcome.Denied, repeated.Outcome);
        Assert.Equal(0, repeated.BrowserKeystrokes);
        Assert.False(repeated.InputMayHaveOccurred);
        Assert.Equal(SyntheticSearchPolicy.Query, await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
    }

    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1, true)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, false)]
    public async Task ActualSearchFixturesKeepKeyboardAndPointerOutcomesDistinct(BundledFixture fixture, bool keyboardWorks)
    {
        await using (var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture))
        {
            using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(browser),
                waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(300), PollInterval = TimeSpan.FromMilliseconds(50), MaxPolls = 3 });
            await reader.ExecuteAsync(ReaderCommand.Tab);
            var entered = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query);
            Assert.True(entered.SearchInput!.ValueVerified);
            await reader.ExecuteAsync(ReaderCommand.Tab);
            var activated = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: new("heading", "Sample course result", MustBeFocused: true));
            Assert.Equal(1, activated.BrowserKeystrokes);
            Assert.Equal(keyboardWorks ? VerificationStatus.Satisfied : VerificationStatus.TimedOut, activated.Verification);
            Assert.Equal(keyboardWorks, (await browser.CaptureAsync()).Nodes.Any(n => n.Name == "Sample course result"));
            Assert.Equal(16, reader.Timeline.Count(e => e.Kind == ReaderEventKind.InputCompleted));
        }
        // A separate fresh context supplies an independent native pointer activation comparison.
        await using var pointer = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        using var pointerReader = new ScreenReaderSession(new NoEditableEntryBrowser(pointer));
        await pointer.DiagnosticPage.GetByLabel("Search courses", new() { Exact = true }).ClickAsync();
        await pointerReader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.True((await pointerReader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query)).SearchInput!.ValueVerified);
        await pointer.DiagnosticPage.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        var observed = await pointer.CaptureAsync();
        Assert.Equal("Sample course result", observed.FocusedNode!.Name);
        Assert.Equal(13, pointerReader.Timeline.Count(e => e.Kind == ReaderEventKind.InputCompleted));
    }

    [Fact]
    public async Task ActualSearchFocusChangeStopsRemainingCharacters()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.MockSearchWorkingV1);
        await browser.DiagnosticPage.EvaluateAsync("() => document.querySelector('#sample-search').addEventListener('input', () => document.querySelector('button').focus(), {once:true})");
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var step = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal("SEARCH_SCOPE_CHANGED", step.ErrorCode);
        Assert.Equal(1, step.BrowserKeystrokes);
        Assert.Equal("s", await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
        Assert.Equal("Search", (await browser.CaptureAsync()).FocusedNode!.Name);
        Assert.Null(step.BarrierCode);
    }

    [Fact]
    public async Task SearchTextHonorsCaptureExclusionAndStaleEpoch()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.MockSearchWorkingV1);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var snapshot = await browser.CaptureAsync();
        var input = new SearchKeyInput("s", snapshot.DocumentId, snapshot.CaptureEpoch, snapshot.FocusedNode!.Id, "");
        var lease = await reader.ExcludeCaptureAsync();
        Assert.Equal("READER_CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query))).Code);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressSearchKeyAsync(input));
        Assert.Equal("", await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
        var resumed = await reader.ResumeCaptureAsync(lease);
        Assert.Equal(snapshot.CaptureEpoch + 1, resumed.CaptureEpoch);
        Assert.Equal("SEARCH_SCOPE_CHANGED", (await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressSearchKeyAsync(input))).Code);
        Assert.Equal("", await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
    }

    [Fact]
    public async Task OtherFixturesCannotObtainSyntheticSearchEntry()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Foundation);
        var snapshot = await browser.CaptureAsync();
        var denied = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressSearchKeyAsync(
            new("s", snapshot.DocumentId, snapshot.CaptureEpoch, "unknown", "")));
        Assert.Equal("SYNTHETIC_SEARCH_FIXTURE_REQUIRED", denied.Code);
    }

    [Theory]
    [InlineData("existing-value")]
    [InlineData("readonly")]
    [InlineData("wrong-native-type")]
    public async Task NativeSearchCannotOverwriteExistingContentOrEnterUnsupportedFields(string boundary)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.MockSearchWorkingV1);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await browser.DiagnosticPage.EvaluateAsync("boundary => { const e=document.getElementById('sample-search'); if(boundary==='existing-value')e.value='existing synthetic content'; else if(boundary==='readonly')e.readOnly=true; else { e.type='text'; e.setAttribute('role','searchbox'); } }", boundary);
        var step = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query);
        Assert.Equal(ReaderOutcome.Denied, step.Outcome);
        Assert.Equal("SEARCH_SCOPE_CHANGED", step.ErrorCode);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.False(step.InputMayHaveOccurred);
        Assert.Equal(boundary == "existing-value" ? "existing synthetic content" : "", await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputCompleted && e.Command == ReaderCommand.EnterSearchText);
    }
}
