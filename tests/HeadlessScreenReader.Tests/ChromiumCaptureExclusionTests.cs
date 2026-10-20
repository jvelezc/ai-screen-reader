using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumCaptureExclusionTests
{
    [Fact]
    public async Task Exclusion_BlocksPayloadAndKeyboardDuringVisibleCanaryThenResumesSameContext()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        using var reader = new ScreenReaderSession(browser);
        var before = await browser.CaptureAsync();
        await reader.ExecuteAsync(ReaderCommand.NextButton);
        await browser.DiagnosticPage.EvaluateAsync("() => { window.contextMarker = 'same-context'; }");
        var lease = await browser.ExcludeCaptureAsync();
        Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
        // Independent trusted diagnostic observer proves sensitive content really existed.
        // This surface deliberately bypasses the reader and never persists its payload.
        await browser.DiagnosticPage.EvaluateAsync("""
            () => {
              const node = document.createElement('h2'); node.id = 'private-canary';
              node.textContent = 'exclusion-visible-secret-canary'; document.body.prepend(node);
              const field = document.createElement('input'); field.id = 'private-field';
              field.value = 'exclusion-field-secret-canary'; document.body.append(field); field.focus();
            }
            """);
        Assert.Equal("exclusion-visible-secret-canary", await browser.DiagnosticPage.Locator("#private-canary").TextContentAsync());
        var deniedCapture = await Assert.ThrowsAsync<BrowserOperationException>(() => browser.CaptureAsync());
        Assert.Equal("CAPTURE_EXCLUDED", deniedCapture.Code);
        var deniedKey = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressKeyAsync("Tab"));
        Assert.Equal("CAPTURE_EXCLUDED", deniedKey.Code);
        Assert.Equal("private-field", await browser.DiagnosticPage.EvaluateAsync<string>("() => document.activeElement.id"));
        var excluded = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(ReaderOutcome.BrowserError, excluded.Outcome);
        Assert.Equal("CAPTURE_EXCLUDED", excluded.ErrorCode);
        Assert.Null(excluded.CursorNodeId);
        Assert.Equal(0, excluded.BrowserKeystrokes);
        await browser.DiagnosticPage.EvaluateAsync("""
            () => { document.querySelector('#private-canary').remove(); document.querySelector('#private-field').remove(); }
            """);
        var resumed = await browser.ResumeCaptureAsync(lease);
        Assert.Equal(before.DocumentId, resumed.DocumentId);
        Assert.Equal(before.CaptureEpoch + 1, resumed.CaptureEpoch);
        Assert.Equal("same-context", await browser.DiagnosticPage.EvaluateAsync<string>("() => window.contextMarker"));
        var fresh = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(ReaderMode.Browse, fresh.Mode);
        Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
        var artifacts = JsonSerializer.Serialize(new { before, resumed, excluded, fresh, reader.Timeline, browser.CaptureStatus });
        Assert.DoesNotContain("exclusion-visible-secret-canary", artifacts);
        Assert.DoesNotContain("exclusion-field-secret-canary", artifacts);
        Assert.Equal(CapturePhase.Permitted, browser.CaptureStatus.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => browser.ResumeCaptureAsync(lease));
        await browser.PressKeyAsync("Tab");
        Assert.NotNull((await browser.CaptureAsync()).FocusedNode);
    }

    [Fact]
    public async Task Resume_ResetsReaderCursorAndModeEvenWhenNoCommandRanDuringGap()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        var prior = await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        Assert.Equal(ReaderMode.Focus, prior.Mode);
        var lease = await browser.ExcludeCaptureAsync();
        await browser.ResumeCaptureAsync(lease);
        var current = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(ReaderMode.Browse, current.Mode);
        Assert.NotEqual(prior.CursorNodeId, current.CursorNodeId);
        Assert.Equal(0, current.BrowserKeystrokes);
        Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
    }

    [Fact]
    public async Task Exclusion_DeniesScopedTypeaheadBeforeAuthorizationCaptureOrDispatch()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Typeahead);
        await browser.PressKeyAsync("Tab");
        var before = await browser.CaptureAsync();
        var context = ListboxTypeaheadPolicy.FocusContext(before)!;
        var input = new ListboxKeyInput("p", before.DocumentId, context.ListboxNodeId, context.DocumentId, context.FrameId);
        await browser.ExcludeCaptureAsync();
        var error = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressListboxKeyAsync(input));
        Assert.Equal("CAPTURE_EXCLUDED", error.Code);
        Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
    }
}
