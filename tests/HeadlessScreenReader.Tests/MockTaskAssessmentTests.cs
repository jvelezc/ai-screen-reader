using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed partial class MockTaskRecordingTests
{
    private static readonly Lazy<Task<(string Folder, RecordedTraversalResult[] Runs, MockPointerRecording Pointer)>> Recorded = new(async () =>
    {
        var folder = NewFolder(); await MockTaskRecordings.RecordAsync(folder, BundledFixture.MockCheckoutWorkingV1);
        var runs = new List<RecordedTraversalResult>();
        for (var i = 1; i <= 3; i++)
            runs.Add(JsonSerializer.Deserialize<RecordedTraversalResult>(await File.ReadAllTextAsync(Path.Combine(folder, $"route-{i}.json")), TraversalDemo.JsonOptions)!);
        var pointer = JsonSerializer.Deserialize<MockPointerRecording>(await File.ReadAllTextAsync(Path.Combine(folder, "pointer.json")), TraversalDemo.JsonOptions)!;
        Assert.Equal("AllTestedKeyboardGoalsObserved", MockTaskRecordings.DescribeObservation(runs, pointer));
        return (folder, runs.ToArray(), pointer);
    });

    [Theory]
    [InlineData("missing-run")]
    [InlineData("policy-denial")]
    [InlineData("missing-video")]
    [InlineData("capture-open")]
    [InlineData("capture-truncated")]
    [InlineData("cleanup-unconfirmed")]
    [InlineData("pointer-not-attempted")]
    [InlineData("pointer-after-missing")]
    [InlineData("pointer-goal-invented")]
    [InlineData("pointer-reused-run")]
    [InlineData("source-changed")]
    [InlineData("space-not-delivered")]
    [InlineData("keyboard-goal-invented")]
    [Trait("Category", "Browser")]
    public async Task InconsistentOrPartialExperimentsCannotBecomeACompleteComparison(string change)
    {
        var original = await Recorded.Value;
        var runs = original.Runs.ToArray(); var pointer = original.Pointer; var first = runs[0];
        switch (change)
        {
            case "missing-run": runs = runs[..2]; break;
            case "policy-denial": runs[0] = first with { Receipt = first.Receipt! with { Policy = TraversalPolicy.Denied } }; break;
            case "missing-video": runs[0] = first with { Recording = first.Recording with { Media = null } }; break;
            case "capture-open": runs[0] = first with { Recording = first.Recording with { CaptureLifecycle = first.Recording.CaptureLifecycle! with { Phase = CapturePhase.Permitted } } }; break;
            case "capture-truncated": pointer = pointer with { Capture = pointer.Capture! with { EventsTruncated = true } }; break;
            case "cleanup-unconfirmed": pointer = pointer with { Shutdown = pointer.Shutdown! with { ActiveCalls = 1 } }; break;
            case "pointer-not-attempted": pointer = pointer with { Receipt = pointer.Receipt! with { Attempted = false } }; break;
            case "pointer-after-missing": pointer = pointer with { Receipt = pointer.Receipt! with { After = null } }; break;
            case "pointer-goal-invented": pointer = pointer with { Receipt = pointer.Receipt! with { After = pointer.Receipt.Before } }; break;
            case "pointer-reused-run": pointer = pointer with { Receipt = pointer.Receipt! with { RunId = first.Receipt!.RunId } }; break;
            case "source-changed": pointer = pointer with { Environment = pointer.Environment! with { Content = BundledFixtureCatalog.Describe(BundledFixture.Foundation) } }; break;
            case "space-not-delivered":
                runs[2] = runs[2] with { Receipt = runs[2].Receipt! with { Timeline = [.. runs[2].Receipt!.Timeline.Select(e => e.Kind == ReaderEventKind.InputCompleted && e.Detail == "Space" ? e with { Detail = "Enter" } : e)] } };
                break;
            case "keyboard-goal-invented":
                var receipt = first.Receipt!;
                runs[0] = first with
                {
                    Receipt = receipt with
                    {
                        Observations = receipt.Observations.SetItem(receipt.Observations.Length - 1,
                    receipt.Observations[^1] with { Snapshot = receipt.Observations[0].Snapshot })
                    }
                };
                break;
        }
        Assert.Equal("IncompleteOrMixedResults", MockTaskRecordings.DescribeObservation(runs, pointer));
        Assert.Equal("AllTestedKeyboardGoalsObserved", MockTaskRecordings.DescribeObservation(original.Runs, original.Pointer));
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task MarkerlessMockVideoPlaysToNativeEndedWithoutInventingGraphSeekPositions()
    {
        var original = await Recorded.Value;
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(new Uri(Path.Combine(original.Folder, "route-1.html")).AbsoluteUri,
            new() { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded, Timeout = 15000 });
        var video = page.Locator("video");
        var ready = await video.EvaluateAsync<bool>("""
            v => new Promise(resolve => {
              const timer=setTimeout(()=>resolve(false),15000);
              const check=()=>{if(v.readyState>=2){clearTimeout(timer);resolve(true);}};
              v.addEventListener('loadeddata',check,{once:true});v.addEventListener('error',()=>{clearTimeout(timer);resolve(false);},{once:true});check();
            })
            """, options: new() { Timeout = 17000 });
        Assert.True(ready);
        await page.Locator("[data-evidence=o1]").ClickAsync();
        Assert.Contains("Unknown", await page.Locator("#selected-interval").InnerTextAsync());
        Assert.True(await video.EvaluateAsync<bool>("v => v.paused && v.currentTime === 0"));
        var ended = await video.EvaluateAsync<bool>("""
            v => new Promise(resolve => {
              const timer=setTimeout(()=>resolve(false),5000);
              v.addEventListener('ended',()=>{clearTimeout(timer);resolve(v.ended&&v.currentTime>0&&!v.error);},{once:true});
              v.play().catch(()=>{clearTimeout(timer);resolve(false);});
            })
            """);
        Assert.True(ended); Assert.Contains("recording ended", await page.Locator("#media-status").InnerTextAsync());
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync(new Uri(Path.Combine(original.Folder, "index.html")).AbsoluteUri);
        Assert.Equal("Every keyboard attempt reached the goal.", await page.Locator("h1").InnerTextAsync());
        Assert.Equal(3, await page.Locator("main ol li").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));
        Assert.DoesNotContain("AllTestedKeyboardGoalsObserved", await page.Locator("body").InnerTextAsync());
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(Path.GetDirectoryName(original.Folder)!,
            "report-preview-" + Path.GetFileName(original.Folder) + ".png"),
            FullPage = true
        });
    }
}
