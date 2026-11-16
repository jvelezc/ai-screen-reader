using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchAuthorityTests
{
    [Theory]
    [InlineData("sequence")]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("document")]
    [InlineData("document-control")]
    [InlineData("epoch")]
    [InlineData("node")]
    [InlineData("character")]
    [InlineData("character-upper-bound")]
    [InlineData("key")]
    [InlineData("ordinary-key")]
    [InlineData("mouse-key")]
    [InlineData("mouse-character")]
    public void SearchAuthorityIntentRejectsInvalidNativeScopes(string change)
    {
        var valid = new SyntheticSearchInputIntent(1, SyntheticSearchInputKind.Character, "a", 2, new('a', 64), "document", 1, "field");
        valid.Validate();
        var invalid = change switch
        {
            "sequence" => valid with { NativeSequence = 0 },
            "kind" => valid with { Kind = (SyntheticSearchInputKind)99 },
            "source" => valid with { SourceSha256 = "invalid" },
            "document" => valid with { DocumentId = new('d', 129) },
            "document-control" => valid with { DocumentId = "document\n" },
            "epoch" => valid with { CaptureEpoch = 0 },
            "node" => valid with { NodeId = "" },
            "character" => valid with { CharacterIndex = 0 },
            "character-upper-bound" => valid with { CharacterIndex = 14 },
            "key" => valid with { NativeKey = "z" },
            "ordinary-key" => valid with { Kind = SyntheticSearchInputKind.Key, NativeKey = "ArrowDown", CharacterIndex = 0 },
            "mouse-key" => valid with { Kind = SyntheticSearchInputKind.MouseFocus, CharacterIndex = 0 },
            _ => valid with { Kind = SyntheticSearchInputKind.MouseActivation, NativeKey = null }
        };
        Assert.Equal("SYNTHETIC_SEARCH_INPUT_INTENT_INVALID", Assert.Throws<BrowserInputDeniedException>(invalid.Validate).Code);
    }

    [Theory]
    [InlineData(BundledFixture.Foundation)]
    [InlineData(BundledFixture.MockCheckoutWorkingV1)]
    [InlineData((BundledFixture)999)]
    public async Task GuardedSearchRejectsUnrelatedSourcesAndMissingAuthority(BundledFixture fixture)
    {
        var folder = NewFolder();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(folder, fixture, Grant));
        Assert.False(Directory.Exists(folder));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(folder, BundledFixture.MockSearchWorkingV1, null!));
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1, TraversalReachability.Reached)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, TraversalReachability.Unknown)]
    [Trait("Category", "Browser")]
    public async Task ActualGuardedSearchUsesOneFreshScopedGrantPerNativeInput(BundledFixture fixture, TraversalReachability reached)
    {
        var requests = new List<SyntheticSearchInputIntent>();
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), fixture,
            (intent, _) => { requests.Add(intent); return Task.FromResult(SyntheticSearchInputDecision.Granted); });
        var receipt = await new TraversalRunner().RunAsync(probe.Browser, SyntheticSearchPolicy.KeyboardPlan("Enter"),
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 });
        Assert.Equal(reached, receipt.Reachability);
        Assert.Equal(16, receipt.CompletedKeystrokes);
        Assert.Equal(16, requests.Count);
        Assert.Equal(Enumerable.Range(1, 16).Select(i => (long)i), requests.Select(i => i.NativeSequence));
        Assert.All(requests, request =>
        {
            request.Validate();
            Assert.Equal(SyntheticSearchSites.Describe(fixture).SourceSha256, request.SourceSha256);
            Assert.Equal(receipt.Observations[0].Snapshot.DocumentId, request.DocumentId);
            Assert.Equal(receipt.Observations[0].Snapshot.CaptureEpoch, request.CaptureEpoch);
        });
        Assert.Equal(new[] { "Tab", "Tab", "Enter" }, requests.Where(i => i.Kind == SyntheticSearchInputKind.Key).Select(i => i.NativeKey));
        var characters = requests.Where(i => i.Kind == SyntheticSearchInputKind.Character).ToArray();
        Assert.Equal(Enumerable.Range(1, 13), characters.Select(i => i.CharacterIndex));
        Assert.Equal("sample course".Select(c => c == ' ' ? "Space" : c.ToString()), characters.Select(i => i.NativeKey));
        Assert.Single(characters.Select(i => i.NodeId).Distinct());
        Assert.True(Assert.Single(receipt.Attempts, a => a.Instruction.Command == ReaderCommand.EnterSearchText).Step!.SearchInput!.ValueVerified);
        var media = await probe.FinishAndInspectAsync();
        Assert.NotEmpty(media.Source);
        Assert.True(Assert.Single(media.Artifacts, a => a.File == "original.webm").DecodedFrames > 0);
        Assert.True(probe.ShutdownStatus.ProcessExitConfirmed);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task MidQueryAuthorityRevocationStopsTheNextCharacterWithoutReplay()
    {
        var requests = new List<SyntheticSearchInputIntent>();
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchWorkingV1,
            (intent, _) =>
            {
                requests.Add(intent);
                return Task.FromResult(intent.Kind == SyntheticSearchInputKind.Character && intent.CharacterIndex == 7
                    ? SyntheticSearchInputDecision.Revoked : SyntheticSearchInputDecision.Granted);
            });
        var browser = (ChromiumFixtureBrowser)probe.Browser;
        using var reader = new ScreenReaderSession(probe.Browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await browser.DiagnosticPage.EvaluateAsync("() => { window.nativeKeys = []; document.addEventListener('keydown', e => window.nativeKeys.push(e.key)); }");
        var result = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, "sample course");
        Assert.Equal(ReaderOutcome.Unknown, result.Outcome);
        Assert.Equal("SYNTHETIC_SEARCH_AUTHORITY_REVOKED", result.ErrorCode);
        Assert.Equal(6, result.BrowserKeystrokes);
        Assert.Equal(6, result.SearchInput!.CompletedCharacters);
        Assert.False(result.SearchInput.ValueVerified);
        Assert.Null(result.BarrierCode);
        Assert.Equal("sample", await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
        Assert.Equal("sample", string.Concat(await browser.DiagnosticPage.EvaluateAsync<string[]>("() => window.nativeKeys")));
        Assert.Equal(8, requests.Count);
        Assert.Equal(7, requests[^1].CharacterIndex);
        Assert.Equal(7, reader.Timeline.Count(e => e.Kind == ReaderEventKind.InputCompleted));
        var media = await probe.FinishAndInspectAsync();
        Assert.NotEmpty(media.Source);
        Assert.Contains(media.Artifacts, a => a.File == "original.webm" && a.DecodedFrames > 0);
        Assert.True(probe.ShutdownStatus.ProcessExitConfirmed);
        Assert.Equal(8, requests.Count);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task LateAuthorityResultCannotDispatchAfterItsDeadline()
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SyntheticSearchInputDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchWorkingV1,
            (_, _) => { asked.TrySetResult(); return release.Task; });
        var browser = (ChromiumFixtureBrowser)probe.Browser;
        await browser.DiagnosticPage.EvaluateAsync("() => { window.nativeKeys = 0; document.addEventListener('keydown', () => window.nativeKeys++); }");
        using var reader = new ScreenReaderSession(probe.Browser);
        var operation = reader.ExecuteAsync(ReaderCommand.Tab);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var denied = await operation.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED", denied.ErrorCode);
        Assert.Equal(0, denied.BrowserKeystrokes);
        release.SetResult(SyntheticSearchInputDecision.Granted);
        await browser.CaptureAsync();
        Assert.Equal(0, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.nativeKeys"));
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputCompleted);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task AuthorityDeadlineCoversNativeChecksAfterTheGrant()
    {
        ChromiumFixtureBrowser? browser = null;
        var folder = NewFolder();
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(folder, BundledFixture.MockSearchWorkingV1,
            async (intent, _) =>
            {
                if (intent.Kind == SyntheticSearchInputKind.Character && intent.CharacterIndex == 1)
                    await browser!.DiagnosticPage.Locator("#sample-search").EvaluateAsync("""
                        e => {
                          const value = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
                          let waited = false;
                          Object.defineProperty(e, 'value', {configurable:true,
                            get() { if (!waited) { waited = true; const end = performance.now() + 3400; while(performance.now() < end) {} }
                              return value.get.call(this); }, set(v) { value.set.call(this, v); }});
                        }
                        """);
                return SyntheticSearchInputDecision.Granted;
            });
        browser = (ChromiumFixtureBrowser)probe.Browser;
        using var reader = new ScreenReaderSession(probe.Browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await browser.DiagnosticPage.EvaluateAsync("() => { window.nativeKeys = 0; document.addEventListener('keydown', () => window.nativeKeys++); }");
        var result = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, "sample course");
        var actual = await browser.DiagnosticPage.EvaluateAsync<int>("() => window.nativeKeys");
        var media = await probe.FinishAndInspectAsync();
        var video = Assert.Single(media.Artifacts, a => a.File == "original.webm");
        await File.WriteAllTextAsync(Path.Combine(folder, "authority-scope-deadline.json"), JsonSerializer.Serialize(new
        { actual, result.BrowserKeystrokes, result.ErrorCode, result.Outcome, video.Sha256, video.DecodedFrames }));
        Assert.Equal(0, actual);
        Assert.Equal(0, result.BrowserKeystrokes);
        Assert.Equal("SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED", result.ErrorCode);
        Assert.Null(result.BarrierCode);
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("native-type")]
    [Trait("Category", "Browser")]
    public async Task ScopeChangeDuringAuthorityWaitCannotRedirectTheNextCharacter(string change)
    {
        ChromiumFixtureBrowser? browser = null;
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchWorkingV1,
            async (intent, _) =>
            {
                if (intent.Kind == SyntheticSearchInputKind.Character && intent.CharacterIndex == 5)
                {
                    if (change == "focus") await browser!.DiagnosticPage.Locator("h1").EvaluateAsync("e => { e.tabIndex = -1; e.focus(); }");
                    else await browser!.DiagnosticPage.Locator("#sample-search").EvaluateAsync("e => { e.setAttribute('role','searchbox'); e.type = 'text'; }");
                }
                return SyntheticSearchInputDecision.Granted;
            });
        browser = (ChromiumFixtureBrowser)probe.Browser;
        using var reader = new ScreenReaderSession(probe.Browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var result = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, "sample course");
        Assert.Equal("SEARCH_SCOPE_CHANGED", result.ErrorCode);
        Assert.Equal(4, result.BrowserKeystrokes);
        Assert.Equal("samp", await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
        Assert.Equal(change == "focus" ? "heading" : "searchbox", (await browser.CaptureAsync()).FocusedNode!.Role);
        Assert.Equal(4, result.SearchInput!.CompletedCharacters);
        Assert.False(result.SearchInput.ValueVerified);
    }

    [Theory]
    [InlineData(SyntheticSearchInputDecision.Revoked, "SYNTHETIC_SEARCH_AUTHORITY_REVOKED")]
    [InlineData(SyntheticSearchInputDecision.Expired, "SYNTHETIC_SEARCH_AUTHORITY_EXPIRED")]
    [InlineData(SyntheticSearchInputDecision.Unconfirmed, "SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED")]
    [InlineData((SyntheticSearchInputDecision)99, "SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED")]
    [Trait("Category", "Browser")]
    public async Task NonGrantedAuthorityCannotDispatchAnOrdinarySearchKey(SyntheticSearchInputDecision decision, string expectedCode)
    {
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchWorkingV1,
            (_, _) => Task.FromResult(decision));
        var browser = (ChromiumFixtureBrowser)probe.Browser;
        await browser.DiagnosticPage.EvaluateAsync("() => { window.nativeKeys = 0; document.addEventListener('keydown', () => window.nativeKeys++); }");
        using var reader = new ScreenReaderSession(probe.Browser);
        var result = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(0, result.BrowserKeystrokes);
        Assert.Null(result.BarrierCode);
        Assert.Equal(0, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.nativeKeys"));
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task GuardedSearchAuthorityCannotBeReplacedOrGrantUnsupportedKeys()
    {
        var calls = 0;
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchWorkingV1,
            (_, _) => { calls++; return Task.FromResult(SyntheticSearchInputDecision.Granted); });
        var browser = (ChromiumFixtureBrowser)probe.Browser;
        Assert.Equal("SYNTHETIC_SEARCH_AUTHORITY_ALREADY_BOUND", Assert.Throws<BrowserInputDeniedException>(() => browser.BindSyntheticSearchAuthority(Grant)).Code);
        Assert.Equal("SYNTHETIC_SEARCH_INPUT_INTENT_INVALID", (await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressKeyAsync("ArrowDown"))).Code);
        Assert.Equal(0, calls);
        await browser.PressKeyAsync("Tab");
        Assert.Equal(1, calls);
        Assert.Equal("searchbox", (await browser.CaptureAsync()).FocusedNode!.Role);
        await using var unrelated = await ChromiumFixtureBrowser.LaunchAsync();
        Assert.Equal("SYNTHETIC_SEARCH_FIXTURE_REQUIRED", Assert.Throws<BrowserInputDeniedException>(() => unrelated.BindSyntheticSearchAuthority(Grant)).Code);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task CancelledAuthorityWaitCannotDispatchALaterGrant()
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SyntheticSearchInputDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchWorkingV1,
            (_, _) => { asked.TrySetResult(); return release.Task; });
        var browser = (ChromiumFixtureBrowser)probe.Browser;
        await browser.DiagnosticPage.EvaluateAsync("() => { window.nativeKeys = 0; document.addEventListener('keydown', () => window.nativeKeys++); }");
        using var reader = new ScreenReaderSession(probe.Browser);
        using var stop = new CancellationTokenSource();
        var operation = reader.ExecuteAsync(ReaderCommand.Tab, cancellationToken: stop.Token);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, result.BrowserKeystrokes);
        Assert.NotEqual(ReaderOutcome.Observed, result.Outcome);
        Assert.Null(result.BarrierCode);
        release.SetResult(SyntheticSearchInputDecision.Granted);
        await browser.CaptureAsync();
        Assert.Equal(0, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.nativeKeys"));
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task AuthorityFailureCannotDiscloseItsMessageOrDispatchInput()
    {
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchWorkingV1,
            (_, _) => throw new IOException("never-log-authority-secret"));
        using var reader = new ScreenReaderSession(probe.Browser);
        var result = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal("SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED", result.ErrorCode);
        Assert.Equal(0, result.BrowserKeystrokes);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputCompleted);
        Assert.DoesNotContain("never-log-authority-secret", JsonSerializer.Serialize(new { result, reader.Timeline }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Browser")]
    public async Task MouseSearchActionsCannotBypassTheirOwnAuthority(bool denyActivation)
    {
        var requests = new List<SyntheticSearchInputIntent>();
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchKeyboardBlockedV1,
            (intent, _) =>
            {
                requests.Add(intent);
                return Task.FromResult(intent.Kind == (denyActivation ? SyntheticSearchInputKind.MouseActivation : SyntheticSearchInputKind.MouseFocus)
                    ? SyntheticSearchInputDecision.Revoked : SyntheticSearchInputDecision.Granted);
            });
        var browser = (ChromiumFixtureBrowser)probe.Browser;
        var focus = await probe.FocusSyntheticSearchAsync();
        if (!denyActivation)
        {
            Assert.False(focus.InputAttempted);
            Assert.False(focus.InputCompleted);
            Assert.Equal("SYNTHETIC_SEARCH_AUTHORITY_REVOKED", focus.ErrorCode);
            Assert.Single(requests);
        }
        else
        {
            Assert.True(focus.FocusObserved);
            using var reader = new ScreenReaderSession(probe.Browser);
            await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
            Assert.True((await reader.ExecuteAsync(ReaderCommand.EnterSearchText, "sample course")).SearchInput!.ValueVerified);
            var after = await browser.CaptureAsync();
            var receipt = await probe.ObserveSyntheticSearchPointerAsync(new("e", after.DocumentId, after.CaptureEpoch, after.FocusedNode!.Id, "sample cours"));
            Assert.False(receipt.Attempted);
            Assert.False(receipt.Completed);
            Assert.False(receipt.GoalObserved);
            Assert.Equal("SYNTHETIC_SEARCH_AUTHORITY_REVOKED", receipt.ErrorCode);
            Assert.Equal(15, requests.Count);
            Assert.Equal(SyntheticSearchInputKind.MouseFocus, requests[0].Kind);
            Assert.Equal(SyntheticSearchInputKind.MouseActivation, requests[^1].Kind);
            Assert.Equal(13, requests.Count(i => i.Kind == SyntheticSearchInputKind.Character));
        }
        Assert.DoesNotContain((await browser.CaptureAsync()).Nodes, n => n.Role == "heading" && n.Name == "Sample course result");
    }

    private static Task<SyntheticSearchInputDecision> Grant(SyntheticSearchInputIntent intent, CancellationToken token) => Task.FromResult(SyntheticSearchInputDecision.Granted);

    [Fact]
    [Trait("Category", "Browser")]
    public async Task CaptureExclusionJoinsPendingAuthorityBeforeAcknowledgingTheGap()
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SyntheticSearchInputDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewFolder(), BundledFixture.MockSearchWorkingV1,
            (_, _) => { calls++; asked.TrySetResult(); return release.Task; });
        var browser = (ChromiumFixtureBrowser)probe.Browser;
        await browser.DiagnosticPage.EvaluateAsync("() => { window.nativeKeys = 0; document.addEventListener('keydown', () => window.nativeKeys++); }");
        using var reader = new ScreenReaderSession(probe.Browser);
        var operation = reader.ExecuteAsync(ReaderCommand.Tab);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var exclusion = browser.ExcludeCaptureAsync();
        Assert.False(exclusion.IsCompleted);
        release.SetResult(SyntheticSearchInputDecision.Granted);
        var step = await operation;
        await exclusion;
        Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.NotEqual(ReaderOutcome.Observed, step.Outcome);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputCompleted);
        Assert.Equal(0, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.nativeKeys"));
        var denied = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(0, denied.BrowserKeystrokes);
        Assert.NotEqual(ReaderOutcome.Observed, denied.Outcome);
        Assert.Equal(1, calls);
        Assert.Equal(0, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.nativeKeys"));
        Assert.Null(denied.BarrierCode);
    }

    private static string NewFolder() => Path.GetFullPath(Path.Combine("artifacts", "synthetic-search-authority-tests", Guid.NewGuid().ToString("N")));
}
