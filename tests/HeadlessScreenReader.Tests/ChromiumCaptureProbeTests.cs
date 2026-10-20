using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumCaptureProbeTests
{
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
