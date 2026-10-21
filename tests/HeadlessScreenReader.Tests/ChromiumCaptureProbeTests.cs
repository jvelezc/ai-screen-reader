using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumCaptureProbeTests
{
    [Fact]
    public async Task NativeDecoder_InvalidMediaFailsWithSanitizedCode()
    {
        var invalid = await Assert.ThrowsAsync<BrowserOperationException>(() => PlaywrightVideoFrames.DecodeAsync([0, 1, 2, 3]));
        Assert.Equal("CAPTURE_DECODE_FAILED", invalid.Code);
        Assert.Null(invalid.InnerException);
        var empty = await Assert.ThrowsAsync<BrowserOperationException>(() => PlaywrightVideoFrames.DecodeAsync([]));
        Assert.Equal("CAPTURE_MEDIA_SIZE_INVALID", empty.Code);
    }

    [Fact]
    public async Task VideoDecoder_InspectsActualFirstAndLastFramesWithIndependentSolidColorControl()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "capture-decoder-controls", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.CapturePrivacy);
        var page = browser.DiagnosticPage;
        await page.SetViewportSizeAsync(320, 240);
        // Deliberately recorded positive control, separate from the exclusion probe's artifacts.
        await page.EvaluateAsync("() => { document.documentElement.style.background = document.body.style.background = '#e41a3d'; }");
        // A DOM style acknowledgement does not prove that Chromium has painted it.
        // Screenshot forces an actual composited frame before the new screencast starts.
        // Do not trim/retry the original video's first frame to hide a stale paint.
        var painted = await page.ScreenshotAsync(new() { Type = Microsoft.Playwright.ScreenshotType.Jpeg });
        var paintedControl = await CaptureProbeDecoder.InspectAsync(page, painted, video: false);
        Assert.True(paintedControl.CanaryPixels > 320 * 240 / 2, JsonSerializer.Serialize(paintedControl));
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var last = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = 0;
        var greenAfter = int.MaxValue;
        var video = Path.Combine(folder, "positive-control.webm");
        _ = await page.Screencast.StartAsync(new()
        {
            Path = video,
            Size = new() { Width = 320, Height = 240 },
            OnFrame = _ =>
            {
                var count = Interlocked.Increment(ref frames);
                if (count >= 1) first.TrySetResult();
                if (count >= Volatile.Read(ref greenAfter)) last.TrySetResult();
                return Task.CompletedTask;
            }
        });
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await page.EvaluateAsync("() => { document.documentElement.style.background = document.body.style.background = '#20b840'; }");
        Interlocked.Exchange(ref greenAfter, Volatile.Read(ref frames) + 3);
        await last.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await page.Screencast.StopAsync();
        var decoded = await CaptureProbeDecoder.InspectAsync(page, await File.ReadAllBytesAsync(video), video: true);
        Assert.True(decoded.Frames > 1);
        Assert.True(decoded.FirstFrameCanaryPixels > 320 * 240 / 2, JsonSerializer.Serialize(decoded));
        Assert.Equal(0, decoded.LastFrameCanaryPixels);
        Assert.True(decoded.CanaryPixels >= decoded.FirstFrameCanaryPixels);
    }

    [Fact]
    public async Task AlreadyCancelledProbe_RetainsExplicitFailedReceiptWithoutStartingCollectors()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var receipt = await ChromiumFixtureBrowser.RunCapturePrivacyProbeAsync(Path.Combine("artifacts", "capture-probe-tests"), cancellation.Token);
        Assert.False(receipt.Verified);
        Assert.Equal("CAPTURE_PROBE_CANCELLED", receipt.ErrorCode);
        Assert.Equal("Launch", receipt.Stage);
        Assert.Null(receipt.Lifecycle);
        Assert.Empty(receipt.Segments);
        Assert.Empty(receipt.Artifacts);
        Assert.True(File.Exists(Path.Combine(receipt.Folder, "receipt.json")));
        Assert.Contains("Inspection is incomplete", File.ReadAllText(Path.Combine(receipt.Folder, "index.md")));
    }

    [Fact]
    public async Task OriginalSegmentsAndStaging_ExcludeVisibleCanaryWithIndependentPositivePixelControl()
    {
        var receipt = await ChromiumFixtureBrowser.RunCapturePrivacyProbeAsync(Path.Combine("artifacts", "capture-probe-tests"));
        Assert.True(receipt.Verified, receipt.ErrorCode);
        Assert.Null(receipt.ErrorCode);
        Assert.Equal("Complete", receipt.Stage);
        Assert.True(receipt.PositiveControlDetected);
        Assert.True(receipt.ObservationDenied);
        Assert.True(receipt.KeyboardDenied);
        Assert.True(receipt.SameContext);
        Assert.True(receipt.FreshEpoch);
        Assert.Equal(2, receipt.Segments.Length);
        Assert.Equal(CapturePhase.Closed, receipt.Lifecycle!.Phase);
        Assert.Equal(2, receipt.Lifecycle.Events.Count(e => e.Phase == CapturePhase.Excluded));
        Assert.Equal(receipt.Before!.DocumentId, receipt.After!.DocumentId);
        Assert.Equal(receipt.Before.CaptureEpoch + 1, receipt.After.CaptureEpoch);
        Assert.All(receipt.Segments, s => Assert.True(s.Frames.Length >= 3));
        Assert.All(receipt.Segments, s => Assert.True(s.TimestampUncertaintyMilliseconds > 250));
        Assert.Contains(receipt.Artifacts, a => a.File == "segment-1.webm");
        Assert.Contains(receipt.Artifacts, a => a.File == "segment-2.webm");
        Assert.All(receipt.Artifacts, a =>
        {
            Assert.Equal(0, a.Pixels.CanaryPixels);
            Assert.True(a.Pixels.Frames >= 1);
            var bytes = File.ReadAllBytes(Path.Combine(receipt.Folder, a.File));
            Assert.Equal(bytes.Length, a.Bytes);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), a.Sha256);
        });
        Assert.All(receipt.Segments.SelectMany(s => s.Frames), f => Assert.Contains(receipt.Artifacts, a => a.File == f.File));
        var json = File.ReadAllText(Path.Combine(receipt.Folder, "receipt.json"));
        using var parsed = JsonDocument.Parse(json);
        Assert.True(parsed.RootElement.GetProperty("Verified").GetBoolean());
        Assert.DoesNotContain("capture-visible-secret-canary", json);
        Assert.DoesNotContain("capture-field-secret-canary", json);
        Assert.Contains("Original segment 1", File.ReadAllText(Path.Combine(receipt.Folder, "index.md")));
    }
}
