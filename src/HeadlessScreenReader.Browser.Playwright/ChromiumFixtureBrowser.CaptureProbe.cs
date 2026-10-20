using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record ProbeArtifact(string File, long Bytes, string Sha256, ProbePixels Pixels);
public sealed record CaptureProbeReceipt(string Schema, string Folder, bool Verified, string? ErrorCode, string Stage,
    bool PositiveControlDetected, bool ObservationDenied, bool KeyboardDenied, bool SameContext, bool FreshEpoch,
    ImmutableArray<ProbeSegment> Segments, ImmutableArray<ProbeArtifact> Artifacts,
    CaptureStatus? Lifecycle, AccessibilitySnapshot? Before, AccessibilitySnapshot? After);

public sealed partial class ChromiumFixtureBrowser
{
    /// <summary>Only a synthetic, network-blocked feasibility experiment; never records a caller's page.</summary>
    public static async Task<CaptureProbeReceipt> RunCapturePrivacyProbeAsync(string outputRoot,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.GetFullPath(Path.Combine(outputRoot, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var segments = ImmutableArray.CreateBuilder<ProbeSegment>();
        var artifacts = ImmutableArray.CreateBuilder<ProbeArtifact>();
        AccessibilitySnapshot? before = null, after = null;
        CaptureStatus? lifecycle = null;
        var positive = false;
        var observationDenied = false;
        var keyboardDenied = false;
        var sameContext = false;
        var freshEpoch = false;
        string? error = null;
        var stage = "Launch";
        try
        {
            byte[] control;
            var browser = await LaunchCoreAsync(false, cancellationToken, BundledFixture.CapturePrivacy, null,
                Path.Combine(folder, "staging")).ConfigureAwait(false);
            try
            {
                await browser._page.SetViewportSizeAsync(320, 240).ConfigureAwait(false);
                var marker = await browser._page.EvaluateAsync<string>("() => window.contextMarker").ConfigureAwait(false);
                stage = "CollectBeforeExclusion";
                var collector = await CaptureProbeSegment.StartAsync(browser._page, folder, "segment-1").ConfigureAwait(false);
                browser._stopProbeCollector = () => collector.StopAsync();
                browser._restartProbeCollector = async () =>
                {
                    collector = await CaptureProbeSegment.StartAsync(browser._page, folder, "segment-2").ConfigureAwait(false);
                };
                try { await collector.WaitForFramesAsync().ConfigureAwait(false); }
                catch { segments.Add(collector.Receipt); throw; }
                before = await browser.CaptureAsync(cancellationToken).ConfigureAwait(false);
                stage = "StopBeforeExclusion";
                var lease = await browser.ExcludeCaptureAsync().ConfigureAwait(false);
                segments.Add(collector.Receipt);
                stage = "ExcludedSyntheticControl";
                // Trusted synthetic diagnostic control only, held in memory and never saved.
                // This deliberately bypasses the reader, to prove the canary was visible.
                await browser._page.EvaluateAsync("() => window.captureProbePhase('excluded')").ConfigureAwait(false);
                control = await browser._page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg }).ConfigureAwait(false);
                try { await browser.CaptureAsync(cancellationToken).ConfigureAwait(false); }
                catch (BrowserOperationException exception) when (exception.Code == "CAPTURE_EXCLUDED") { observationDenied = true; }
                try { await browser.PressKeyAsync("Tab", cancellationToken).ConfigureAwait(false); }
                catch (BrowserInputDeniedException exception) when (exception.Code == "CAPTURE_EXCLUDED") { keyboardDenied = true; }
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                await browser._page.EvaluateAsync("() => window.captureProbePhase('permitted')").ConfigureAwait(false);
                await browser._page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))").ConfigureAwait(false);
                stage = "ResumeAndCollect";
                after = await browser.ResumeCaptureAsync(lease).ConfigureAwait(false);
                await collector.WaitForFramesAsync().ConfigureAwait(false);
                stage = "FinalizeSegments";
                await browser.ExcludeCaptureAsync().ConfigureAwait(false);
                segments.Add(collector.Receipt);
                sameContext = before.DocumentId == after.DocumentId
                    && marker == await browser._page.EvaluateAsync<string>("() => window.contextMarker").ConfigureAwait(false);
                freshEpoch = after.CaptureEpoch == before.CaptureEpoch + 1;
            }
            finally
            {
                await browser.DisposeAsync().ConfigureAwait(false);
                lifecycle = browser.CaptureStatus;
            }

            using var decoderPlaywright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
            await using var decoderBrowser = await decoderPlaywright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true }).ConfigureAwait(false);
            var decoderPage = await decoderBrowser.NewPageAsync().ConfigureAwait(false);
            await decoderPage.RouteAsync("**/*", route => route.AbortAsync()).ConfigureAwait(false);
            stage = "DecodePositiveControl";
            var controlPixels = await CaptureProbeDecoder.InspectAsync(decoderPage, control, video: false).ConfigureAwait(false);
            positive = controlPixels.CanaryPixels > 320 * 240 / 2;
            stage = "InspectOriginalsAndStaging";
            // Scan producer originals AND its explicit staging directory after producer shutdown.
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(file);
                if (extension is not (".webm" or ".jpg")) throw new InvalidDataException("CAPTURE_ARTIFACT_UNRECOGNIZED");
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
                var pixels = await CaptureProbeDecoder.InspectAsync(decoderPage, bytes, extension == ".webm").ConfigureAwait(false);
                artifacts.Add(new(Path.GetRelativePath(folder, file).Replace('\\', '/'), bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)), pixels));
            }
            if (artifacts.Count == 0 || artifacts.Any(a => a.Pixels.CanaryPixels != 0)) error = "CAPTURE_CANARY_LEAK_OR_EMPTY_EVIDENCE";
            if (!positive || !observationDenied || !keyboardDenied || !sameContext || !freshEpoch
                || segments.Count != 2 || segments.Any(s => s.Frames.Length < 3)
                || segments.SelectMany(s => s.Frames).Any(f => !artifacts.Any(a => a.File == f.File))
                || !artifacts.Any(a => a.File == "segment-1.webm") || !artifacts.Any(a => a.File == "segment-2.webm"))
                error ??= "CAPTURE_PROBE_INCOMPLETE";
            if (error is null) stage = "Complete";
        }
        catch (OperationCanceledException) { error = "CAPTURE_PROBE_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch (Exception exception) when (exception is PlaywrightException or IOException or UnauthorizedAccessException or TimeoutException)
        { error = "CAPTURE_PROBE_FAILED"; }
        var receipt = new CaptureProbeReceipt("capture-privacy-probe-v1", folder, error is null, error, stage,
            positive, observationDenied, keyboardDenied, sameContext, freshEpoch, segments.ToImmutable(), artifacts.ToImmutable(), lifecycle, before, after);
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions
        { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
        if (json.Contains("capture-visible-secret-canary", StringComparison.Ordinal)
            || json.Contains("capture-field-secret-canary", StringComparison.Ordinal))
            throw new BrowserOperationException("CAPTURE_STRUCTURED_CANARY_LEAK");
        await using (var stream = new FileStream(Path.Combine(folder, "receipt.json"), FileMode.CreateNew))
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json)).ConfigureAwait(false);
        var report = "# Synthetic capture exclusion probe\n\n"
            + $"Result: **{(receipt.Verified ? "Verified for this controlled probe" : "Failed: " + receipt.ErrorCode + " at " + receipt.Stage)}**. "
            + "No real credentials, human lease or general page recording capability.\n\n"
            + "Enabled producer collectors: AX observations, callback JPEGs and original WebM segments. "
            + "Trace, HAR, clipboard, network-body and typed-value logging are disabled. "
            + "The independent synthetic positive-control screenshot was decoded in memory and never saved.\n\n"
            + "[Structured receipt, lifecycle gaps and artifact hashes](receipt.json)\n\n"
            + string.Join("\n", receipt.Segments.Select((s, i) => $"- [Original segment {i + 1}]({s.Video}): {s.Frames.Length} retained JPEG frames. "
                + (s.Frames.Length > 0 ? $"[First original frame]({s.Frames[0].File})." : "No frame observed.")))
            + "\n\n" + (receipt.Verified ? "All producer originals and capture staging files were inspected after producer shutdown. "
                : "Inspection is incomplete; retained partial files have not passed validation. ")
            + "The synthetic visible canary is a solid red viewport plus visible text; the detector checks the decoded red pixels. "
            + "This does not certify arbitrary sensitive content or synchronization. No continuity is inferred through exclusions.\n";
        report += "\nPlaywright .NET 1.63 exposes presentation timestamps as Single epoch milliseconds. "
            + "Their quantization uncertainty is retained per segment; these values cannot establish 250 ms synchronization.\n";
        await using (var stream = new FileStream(Path.Combine(folder, "index.md"), FileMode.CreateNew))
            await stream.WriteAsync(Encoding.UTF8.GetBytes(report)).ConfigureAwait(false);
        return receipt;
    }
}
