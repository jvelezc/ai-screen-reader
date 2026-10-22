using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumEvidenceViewerTests
{
    [Theory]
    [InlineData(0, "Reached")]
    [InlineData(1, "NotReached")]
    [InlineData(2, "Unknown")]
    [InlineData(3, "Unknown")]
    public async Task FourRoutes_OfflineViewerSelectsRealNodesActionsAndEventsWithoutLiveInputOrMediaMutation(int route, string outcome)
    {
        var folder = NewFolder();
        var result = await TraversalRecordingDemo.RunRouteAsync(Path.Combine(folder, "route-1-media"), TraversalDemo.Plans[route]);
        await AssertRecordingAsync(result.Recording, folder);
        var receipt = result.Receipt!;
        await TraversalArtifactWriter.WriteAsync(folder, 1, receipt, result.Environment, result.Recording);
        var original = Path.Combine(folder, "route-1-media", "original.webm");
        var before = SHA256.HashData(await File.ReadAllBytesAsync(original));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var context = await browser.NewContextAsync();
        var live = await context.NewPageAsync();
        await live.SetContentAsync("<button id=live>Untouched live control</button><script>window.keys=0;window.clicks=0;addEventListener('keydown',()=>keys++);document.querySelector('button').addEventListener('click',()=>clicks++);</script>");
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(5000);
        var errors = new List<string>();
        var externalRequests = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Request += (_, request) => { if (request.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) externalRequests.Add(request.Url); };
        await OpenReportAsync(page, Path.Combine(folder, "route-1.html"));
        await WaitForMediaAsync(page, "ready");
        Assert.Contains("Destination " + outcome, await page.Locator(".outcome").InnerTextAsync());
        Assert.Equal(320, await page.Locator("video").EvaluateAsync<int>("v => v.videoWidth"));
        Assert.Equal(receipt.Observations.Length + receipt.Attempts.Length, await page.Locator(".trace button[data-evidence]").CountAsync());
        foreach (var observation in receipt.Observations)
        {
            await page.Locator("[data-evidence='" + observation.Id + "']").ClickAsync();
            Assert.Contains(observation.Id + ":", await page.Locator("#selected-label").InnerTextAsync());
            Assert.Contains("accessibility acquisition", await page.Locator("#selected-detail").InnerTextAsync());
            var cue = result.Recording.Alignment!.Cues.Single(c => c.EvidenceId == observation.Id);
            await AssertSeek(page, cue);
        }
        var attempt = receipt.Attempts[^1];
        // Native Tab/Enter reaches the actual report control; focus is not injected.
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15_000 });
        await WaitForMediaAsync(page, "ready");
        var focused = false;
        for (var i = 0; i < 40; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            focused = await page.EvaluateAsync<bool>("id => document.activeElement?.dataset.evidence === id", attempt.Id);
            if (focused) break;
        }
        Assert.True(focused, "The action evidence control must be reachable with native Tab.");
        await page.Keyboard.PressAsync("Enter");
        var detail = await page.Locator("#selected-detail").InnerTextAsync();
        Assert.Contains("Source " + attempt.SourceObservationId, detail);
        Assert.Contains("actual result " + attempt.ResultObservationId, detail);
        Assert.Contains("goal verification " + attempt.Step!.Verification, detail);
        Assert.Equal(attempt.Events.Length, await page.Locator("tr.in-range").CountAsync());
        await AssertSeek(page, result.Recording.Alignment!.Cues.Single(c => c.EvidenceId == attempt.Id));
        var input = receipt.Timeline.FirstOrDefault(e => e.Kind == ReaderEventKind.InputAttempted) ?? receipt.Timeline[0];
        await page.Locator("[data-evidence='event-" + input.Sequence + "']").ClickAsync();
        Assert.Contains("Event " + input.Sequence + ":", await page.Locator("#selected-label").InnerTextAsync());
        Assert.Equal(1, await page.Locator("tr[aria-current=true]").CountAsync());
        Assert.True(await page.Locator("video").EvaluateAsync<bool>("v => v.paused"));
        Assert.Empty(errors);
        Assert.Empty(externalRequests);
        Assert.Equal(0, await live.EvaluateAsync<int>("() => window.keys + window.clicks"));
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(original)));
        Assert.Equal(outcome, receipt.Reachability.ToString());
    }

    [Fact]
    public async Task NativePlaybackEvents_KeepStatusConsistentThroughPlayPauseEndAndEvidenceSelection()
    {
        var folder = NewFolder();
        var result = await TraversalRecordingDemo.RunRouteAsync(Path.Combine(folder, "route-1-media"), TraversalDemo.Plans[0]);
        await AssertRecordingAsync(result.Recording, folder);
        await TraversalArtifactWriter.WriteAsync(folder, 1, result.Receipt!, result.Environment, result.Recording);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(5000);
        await OpenReportAsync(page, Path.Combine(folder, "route-1.html"));
        await WaitForMediaAsync(page, "ready");
        Assert.Contains("ready and paused", await page.Locator("#media-status").InnerTextAsync());
        // Use the real media API and its native events, rather than dispatching synthetic events.
        await page.Locator("video").EvaluateAsync("v => v.play()");
        await Assertions.Expect(page.Locator("#media-status")).ToContainTextAsync("Playing");
        Assert.False(await page.Locator("video").EvaluateAsync<bool>("v => v.paused"));
        await page.Locator("video").EvaluateAsync("v => v.pause()");
        await Assertions.Expect(page.Locator("#media-status")).ToContainTextAsync("ready and paused");
        await page.Locator("video").EvaluateAsync("v => { v.currentTime = Math.max(0, v.duration - 0.08); return v.play(); }");
        await WaitForMediaAsync(page, "ended");
        Assert.Contains("recording ended", await page.Locator("#media-status").InnerTextAsync());
        await page.Locator("[data-evidence=o1]").ClickAsync();
        await WaitForMediaAsync(page, "selected");
        Assert.Contains("ready and paused", await page.Locator("#media-status").InnerTextAsync());
    }

    [Fact]
    public async Task MissingOriginalOrUnknownPartialOutsideTiming_RemainsExplicitAndDoesNotInventSeek()
    {
        var folder = NewFolder();
        var result = await TraversalRecordingDemo.RunRouteAsync(Path.Combine(folder, "route-1-media"), TraversalDemo.Plans[0]);
        await AssertRecordingAsync(result.Recording, folder);
        var receipt = result.Receipt!;
        var first = result.Recording.Alignment!.Cues.Single(c => c.EvidenceId == "o1");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(5000);
        foreach (var coverage in new[] { RecordingCueCoverage.Unknown, RecordingCueCoverage.OutsideClip, RecordingCueCoverage.Partial })
        {
            var cue = coverage == RecordingCueCoverage.Partial ? first with { Coverage = coverage }
                : first with { Coverage = coverage, StartSeconds = null, EndSeconds = null, FirstFrame = null, LastFrame = null };
            var recording = result.Recording with
            {
                Alignment = result.Recording.Alignment with
                { Cues = result.Recording.Alignment.Cues.SetItem(0, cue) }
            };
            var path = Path.Combine(folder, coverage + ".html");
            await File.WriteAllTextAsync(path, TraversalEvidenceViewer.Render(receipt, 1, recording));
            await OpenReportAsync(page, path);
            await WaitForMediaAsync(page, "ready");
            await page.Locator("[data-evidence=o1]").ClickAsync();
            Assert.Contains(coverage.ToString(), await page.Locator("#selected-interval").InnerTextAsync());
            if (coverage != RecordingCueCoverage.Partial)
                Assert.Equal(0, await page.Locator("video").EvaluateAsync<double>("v => v.currentTime"));
        }
        var missing = Path.Combine(folder, "missing.html");
        await File.WriteAllTextAsync(missing, TraversalEvidenceViewer.Render(receipt, 2, result.Recording));
        await OpenReportAsync(page, missing);
        await WaitForMediaAsync(page, "hidden");
        Assert.Contains("Original video could not be loaded", await page.Locator("#media-status").InnerTextAsync());
        await page.Locator("[data-evidence=a2]").ClickAsync();
        Assert.Contains("Playback is unavailable", await page.Locator("#selected-interval").InnerTextAsync());
        Assert.Contains("Destination Reached", await page.Locator(".outcome").InnerTextAsync());
    }

    [Fact]
    public async Task FailedCollectorAndInjectedMarkup_KeepReadableEvidenceWithNoScriptExecution()
    {
        var receipt = await TraversalEvidenceViewerTests.Receipt();
        receipt = receipt with
        {
            Plan = receipt.Plan with { Name = "</script><script>window.injected=true</script><img src=https://example.com/leak>" },
            Timeline = [receipt.Timeline[0] with { Detail = "<img src=https://example.com/leak onerror=alert(1)>" }]
        };
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "failed.html");
        await File.WriteAllTextAsync(path, TraversalEvidenceViewer.Render(receipt, 1, TraversalEvidenceViewerTests.Failed));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync();
        var requests = new List<string>();
        page.Request += (_, request) => { if (request.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) requests.Add(request.Url); };
        await OpenReportAsync(page, path);
        Assert.False(await page.EvaluateAsync<bool>("() => !!window.injected"));
        Assert.Equal(0, await page.Locator("img,iframe,form").CountAsync());
        Assert.Empty(requests);
        Assert.Contains("DECODE_FAILED", await page.Locator("#media-status").InnerTextAsync());
        await page.Locator("[data-evidence=a1]").ClickAsync();
        Assert.Contains("Unknown", await page.Locator("#selected-interval").InnerTextAsync());
        Assert.Contains("actual result o2", await page.Locator("#selected-detail").InnerTextAsync());
        Assert.Contains("Destination Unknown", await page.Locator(".outcome").InnerTextAsync());
    }

    private static Task<IResponse?> OpenReportAsync(IPage page, string path) =>
        // Document startup uses the existing fixture startup allowance. Media readiness
        // and all native interaction/seek assertions remain separate, with their original bounds.
        page.GotoAsync(new Uri(path).AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15_000 });

    private static async Task AssertRecordingAsync(TraversalRecordingEvidence recording, string folder)
    {
        var safe = new
        {
            recording.Stage,
            recording.ErrorCode,
            recording.Shutdown,
            recording.CaptureLifecycle,
            Calibrated = recording.Alignment?.Calibrated,
            AlignmentCode = recording.Alignment?.ReasonCode
        };
        Directory.CreateDirectory(folder);
        var json = JsonSerializer.Serialize(safe);
        await File.WriteAllTextAsync(Path.Combine(folder, "recording-receipt.json"), json);
        Assert.True(recording.ErrorCode is null, json);
    }

    private static async Task WaitForMediaAsync(IPage page, string state, double? expected = null)
    {
        // Direct function invocation respects the report CSP. Observe actual native media
        // events; WaitForFunction's string evaluation requires forbidden unsafe-eval.
        var reached = await page.Locator("video").EvaluateAsync<bool>("""
            (video, {state, expected}) => new Promise(resolve => {
                const matches = () => {
                    switch (state) {
                        case 'ready': return video.readyState >= 2 && !video.seeking;
                        case 'ended': return video.ended;
                        case 'selected': return !video.seeking && !video.ended;
                        case 'hidden': return video.hidden;
                        case 'seek': return Math.abs(video.currentTime - expected) < 0.045;
                        default: throw new Error('Unknown media condition');
                    }
                };
                const events = ['loadeddata', 'canplay', 'seeked', 'timeupdate', 'ended', 'error'];
                const finish = value => {
                    clearTimeout(timer);
                    for (const event of events) video.removeEventListener(event, check);
                    resolve(value);
                };
                const check = () => { if (matches()) finish(true); };
                const timer = setTimeout(() => finish(matches()), 5000);
                for (const event of events) video.addEventListener(event, check);
                check();
            })
            """, new { state, expected });
        Assert.True(reached, "Native media condition was not observed within 5000ms: " + state);
    }

    private static async Task AssertSeek(IPage page, TraversalRecordingCue cue)
    {
        await WaitForMediaAsync(page, "seek", cue.StartSeconds);
        Assert.True(await page.Locator("video").EvaluateAsync<bool>("v => v.paused"));
        Assert.Contains(cue.Coverage.ToString(), await page.Locator("#selected-interval").InnerTextAsync());
    }
    private static string NewFolder() => Path.GetFullPath(Path.Combine("artifacts", "viewer-tests", Guid.NewGuid().ToString("N")));
}
