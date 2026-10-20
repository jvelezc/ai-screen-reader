using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumReaderCaptureExclusionTests
{
    [Fact]
    public async Task WholeReaderExclusion_DeniesEveryEntryPointAndRetainsNoCanaryAcrossSameContextResume()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(ReaderMode.Focus, (await reader.ExecuteAsync(ReaderCommand.EnterFocusMode)).Mode);
        var before = await browser.CaptureAsync();
        var boundary = reader.Timeline[^1].Sequence;
        await browser.DiagnosticPage.EvaluateAsync("() => window.readerContextMarker = 'same-reader-context'");
        var lease = await reader.ExcludeCaptureAsync();
        Assert.Equal(CapturePhase.Excluded, reader.CaptureStatus.Phase);
        Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
        // Independent trusted positive control: the excluded payload really is visible.
        // Diagnostic content stays in memory and is never a reader observation.
        await browser.DiagnosticPage.EvaluateAsync("""
            () => {
              const heading = document.createElement('h2'); heading.id = 'reader-private';
              heading.textContent = 'reader-excluded-visible-canary'; document.body.prepend(heading);
              const field = document.createElement('input'); field.id = 'reader-password'; field.type = 'password';
              field.value = 'reader-excluded-password-canary'; document.body.append(field); field.focus();
            }
            """);
        Assert.True(await browser.DiagnosticPage.Locator("#reader-private").IsVisibleAsync());
        Assert.Equal("reader-excluded-visible-canary", await browser.DiagnosticPage.Locator("#reader-private").InnerTextAsync());
        Assert.Equal("reader-excluded-password-canary", await browser.DiagnosticPage.Locator("#reader-password").InputValueAsync());
        foreach (var command in Enum.GetValues<ReaderCommand>())
            Assert.Equal("READER_CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(
                () => reader.ExecuteAsync(command, "pe"))).Code);
        await using (var idle = reader.ObserveChangesAsync().GetAsyncEnumerator())
            await Assert.ThrowsAsync<BrowserOperationException>(() => idle.MoveNextAsync().AsTask());
        Assert.Throws<BrowserOperationException>(() => reader.Timeline);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressKeyAsync("Tab"));
        Assert.Equal("reader-password", await browser.DiagnosticPage.EvaluateAsync<string>("() => document.activeElement.id"));
        await browser.DiagnosticPage.EvaluateAsync("""
            () => { document.querySelector('#reader-private').remove(); document.querySelector('#reader-password').remove(); }
            """);
        var resumed = await reader.ResumeCaptureAsync(lease);
        Assert.Equal(before.DocumentId, resumed.DocumentId);
        Assert.Equal(before.CaptureEpoch + 1, resumed.CaptureEpoch);
        Assert.Equal("same-reader-context", await browser.DiagnosticPage.EvaluateAsync<string>("() => window.readerContextMarker"));
        var current = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(ReaderMode.Browse, current.Mode);
        var after = reader.Timeline.Where(e => e.Sequence > boundary).ToArray();
        Assert.Single(after, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
        Assert.DoesNotContain(after, e => e.Kind is ReaderEventKind.LiveRegionChanged
            or ReaderEventKind.FocusChanged or ReaderEventKind.WidgetStateChanged);
        var evidence = JsonSerializer.Serialize(new { current, resumed, reader.Timeline, reader.CaptureStatus, Adapter = browser.CaptureStatus });
        Assert.DoesNotContain("reader-excluded-visible-canary", evidence);
        Assert.DoesNotContain("reader-excluded-password-canary", evidence);
        var path = Path.GetFullPath(Path.Combine("artifacts", "reader-exclusion", Guid.NewGuid().ToString("N"), "receipt.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, evidence);
        Assert.Equal(evidence, await File.ReadAllTextAsync(path));
        await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ResumeCaptureAsync(lease));
        Assert.Equal(1, (await reader.ExecuteAsync(ReaderCommand.Tab)).BrowserKeystrokes);
    }
}
