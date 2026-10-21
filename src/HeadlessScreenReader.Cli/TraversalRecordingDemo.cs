using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Cli;

public sealed record TraversalRecordingEvidence(string Stage, string? ErrorCode,
    TraversalRecordingAlignment? Alignment, ImmutableArray<ClockRoundTrip> ClockSamples,
    RecordingProbeMedia? Media, CaptureStatus? CaptureLifecycle)
{
    public double? RunnerOriginHostMilliseconds { get; init; }
    public BrowserShutdownStatus? Shutdown { get; init; }
}
public sealed record RecordedTraversalResult(TraversalReceipt? Receipt, TraversalRecordingEvidence Recording,
    FixtureBrowserEnvironment? Environment);

/// <summary>Four fixed synthetic routes. No arbitrary recording, user fields, replay or human input.</summary>
public static class TraversalRecordingDemo
{
    public static async Task<RecordedTraversalResult> RunRouteAsync(string mediaFolder, TraversalPlan plan,
        CancellationToken cancellationToken = default)
    {
        var time = TimeProvider.System;
        var started = time.GetTimestamp();
        double HostNow() => time.GetElapsedTime(started).TotalMilliseconds;
        var samples = ImmutableArray.CreateBuilder<ClockRoundTrip>();
        TraversalReceipt? receipt = null;
        RecordingProbeMedia? media = null;
        TraversalRecordingAlignment? alignment = null;
        FixtureBrowserEnvironment? environment = null;
        ChromiumRecordingProbe? ownedProbe = null;
        var stage = "Launch";
        string? error = null;
        double? runnerOriginHostMilliseconds = null;
        try
        {
            await using var probe = await ChromiumFixtureBrowser.LaunchTraversalRecordingAsync(mediaFolder, cancellationToken).ConfigureAwait(false);
            ownedProbe = probe; environment = probe.Environment;
            // Keep the full baseline uncertainty window inside the clip, outside route duration.
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            async Task Calibrate()
            {
                for (var i = 0; i < 7; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var before = HostNow();
                    var browserTime = await probe.ReadBrowserClockAsync().ConfigureAwait(false);
                    samples.Add(new(before, browserTime, HostNow()));
                }
            }
            stage = "CalibrateBefore";
            await Calibrate().ConfigureAwait(false);
            var documentId = (await probe.Browser.CaptureAsync(cancellationToken).ConfigureAwait(false)).DocumentId;
            stage = "Traverse";
            receipt = await new TraversalRunner(time).RunAsync(probe.Browser, plan,
                waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            runnerOriginHostMilliseconds = time.GetElapsedTime(started, receipt.Clock!.OriginTimestamp).TotalMilliseconds;
            stage = "CalibrateAfter";
            await Calibrate().ConfigureAwait(false);
            // Retain a trailing frame even for routes that perform only virtual reader commands.
            // This measurement hold is outside the route and never counts as user effort.
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            stage = "FinalizeAndDecodeOriginalMedia";
            media = await probe.FinishAndInspectAsync().ConfigureAwait(false);
            receipt = receipt with { HasRecording = true };
            stage = "AlignEvidence";
            alignment = TraversalRecording.Align(receipt, RecordingSynchronization.Calibrate(samples.ToImmutable()),
                runnerOriginHostMilliseconds.Value, documentId,
                media.Artifacts.Single(a => a.File == "original.webm").Sha256, media.Source, media.Video);
            stage = "Complete";
        }
        catch (OperationCanceledException) { error = "TRAVERSAL_RECORDING_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch (Exception exception) when (exception is PlaywrightException or IOException or TimeoutException or UnauthorizedAccessException)
        { error = "TRAVERSAL_RECORDING_FAILED"; }
        return new(receipt, new(stage, error, alignment, samples.ToImmutable(), media, ownedProbe?.CaptureStatus)
        { RunnerOriginHostMilliseconds = runnerOriginHostMilliseconds, Shutdown = ownedProbe?.ShutdownStatus }, environment);
    }

    public static async Task<int> RunAsync(string outputRoot, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetFullPath(Path.Combine(outputRoot, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var results = new List<RecordedTraversalResult>();
        for (var i = 0; i < TraversalDemo.Plans.Length; i++)
        {
            var result = await RunRouteAsync(Path.Combine(folder, $"route-{i + 1}-media"), TraversalDemo.Plans[i], cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (result.Receipt is { } receipt)
                await TraversalArtifactWriter.WriteAsync(folder, i + 1, receipt, result.Environment, result.Recording).ConfigureAwait(false);
            else
                await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, $"route-{i + 1}-failure.json"),
                    JsonSerializer.Serialize(result, TraversalDemo.JsonOptions)).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) break;
        }
        var ready = results.Count == 4 && results.All(r => r.Recording.ErrorCode is null && r.Recording.Alignment?.Calibrated == true
            && r.Recording.Alignment.Cues.All(c => c.Coverage == RecordingCueCoverage.Covered))
            && results[0].Receipt?.Reachability == TraversalReachability.Reached
            && results[1].Receipt is { Barrier: TraversalBarrier.Observed, CompletedKeystrokes: 0 }
            && results[2].Receipt?.Reachability == TraversalReachability.Unknown
            && results[3].Receipt?.Reachability == TraversalReachability.Unknown;
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "index.md"),
            "# Recorded synthetic shop routes\n\nSelect a trace node or action, then its passive original-video interval. "
            + "Each route uses a fresh input-free fixture. Acquisition windows are separate from goal verification.\n\n"
            + string.Join("\n", results.Select((r, i) => r.Receipt is null
                ? $"- Route {i + 1}: [{r.Recording.Stage}; {r.Recording.ErrorCode}](route-{i + 1}-failure.json)"
                : $"- [{r.Receipt.Plan.Name}](route-{i + 1}.html): {r.Receipt.Reachability}; {r.Recording.Alignment?.ReasonCode ?? r.Recording.ErrorCode}. [Readable report](route-{i + 1}.md) · [Trace](route-{i + 1}.svg)"))
            + "\n\nNo recording of private inputs, arbitrary pages, human takeover or automatic replay. "
            + "Missing or partial media never changes the route's barrier assessment.\n").ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new { RecordedRoutesReady = ready, Routes = results.Count, Report = Path.Combine(folder, "index.md") }));
        return cancellationToken.IsCancellationRequested ? 130 : ready ? 0 : 3;
    }
}
