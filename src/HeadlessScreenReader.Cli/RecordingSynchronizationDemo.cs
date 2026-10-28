using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Cli;

public sealed record RecordingSynchronizationReceipt(string Schema, string Folder, string Stage, string? ErrorCode,
    RecordingSynchronizationResult? Synchronization, ImmutableArray<ClockRoundTrip> ClockSamples,
    double ReaderOriginUncertaintyMilliseconds, ImmutableArray<RecordedActionMarker> Actions,
    ImmutableArray<ReaderStep> Steps, ImmutableArray<ReaderEvent> Timeline, RecordingProbeMedia? Media,
    CaptureStatus? CaptureLifecycle, FixtureBrowserEnvironment? Environment)
{
    public BrowserShutdownStatus? Shutdown { get; init; }
}

public static class RecordingSynchronizationDemo
{
    public static async Task<RecordingSynchronizationReceipt> RunAsync(string outputRoot, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetFullPath(Path.Combine(outputRoot, Guid.NewGuid().ToString("N")));
        var time = TimeProvider.System;
        var started = time.GetTimestamp();
        double HostNow() => time.GetElapsedTime(started).TotalMilliseconds;
        var samples = ImmutableArray.CreateBuilder<ClockRoundTrip>();
        var steps = ImmutableArray.CreateBuilder<ReaderStep>();
        ImmutableArray<ReaderEvent> timeline = [];
        ImmutableArray<RecordedActionMarker> actions = [];
        RecordingProbeMedia? media = null;
        RecordingSynchronizationResult? analysis = null;
        CaptureStatus? lifecycle = null;
        FixtureBrowserEnvironment? environment = null;
        ChromiumRecordingProbe? ownedProbe = null;
        var originUncertainty = 0d;
        var stage = "Launch";
        string? error = null;
        try
        {
            await using var probe = await ChromiumFixtureBrowser.LaunchRecordingProbeAsync(folder, cancellationToken).ConfigureAwait(false);
            ownedProbe = probe;
            environment = probe.Environment;
            async Task Calibrate()
            {
                for (var i = 0; i < 7; i++)
                {
                    var before = HostNow();
                    var browserTime = await probe.ReadBrowserClockAsync().ConfigureAwait(false);
                    samples.Add(new(before, browserTime, HostNow()));
                }
            }
            stage = "CalibrateBefore";
            await Calibrate().ConfigureAwait(false);
            var originBefore = HostNow();
            using var reader = new ScreenReaderSession(probe.Browser, timeProvider: time);
            var originAfter = HostNow();
            var origin = (originBefore + originAfter) / 2;
            originUncertainty = (originAfter - originBefore) / 2;
            try
            {
                await reader.ExecuteAsync(ReaderCommand.ReadCurrent, cancellationToken: cancellationToken,
                    expectation: new("heading", "Marker 0")).ConfigureAwait(false);
                await reader.ExecuteAsync(ReaderCommand.Tab, cancellationToken: cancellationToken).ConfigureAwait(false);
                await reader.ExecuteAsync(ReaderCommand.EnterFocusMode, cancellationToken: cancellationToken).ConfigureAwait(false);
                stage = "ExecuteTwentyReaderKeys";
                for (var objective = 1; objective <= 20; objective++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    steps.Add(await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowRight", cancellationToken,
                        expectation: new("heading", $"Marker {objective}")).ConfigureAwait(false));
                    // Controlled fixture hold so every independent marker can be sampled.
                    // This is measurement time, never an interaction-difficulty metric.
                    await Task.Delay(80, cancellationToken).ConfigureAwait(false);
                }
            }
            finally { timeline = reader.Timeline; }
            var browserEvents = await probe.ReadActionMarkersAsync().ConfigureAwait(false);
            var attempts = timeline.Where(e => e.Kind == ReaderEventKind.InputAttempted && e.Detail == "ArrowRight").ToArray();
            var completions = timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted && e.Detail == "ArrowRight").ToArray();
            string[] expectedKeys = ["Tab", .. Enumerable.Repeat("ArrowRight", 20)];
            if (attempts.Length != 20 || completions.Length != 20 || browserEvents.Length != 20
                || !timeline.Where(e => e.Kind == ReaderEventKind.InputAttempted).Select(e => e.Detail).SequenceEqual(expectedKeys)
                || !timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail).SequenceEqual(expectedKeys))
                throw new BrowserOperationException("RECORDING_INPUT_EVIDENCE_INCOMPLETE");
            actions = Enumerable.Range(0, 20).Select(i => new RecordedActionMarker(browserEvents[i].Objective, attempts[i].Sequence,
                origin + attempts[i].ElapsedMilliseconds, origin + completions[i].ElapsedMilliseconds,
                browserEvents[i].UnixMilliseconds, steps[i].Verification == VerificationStatus.Satisfied)).ToImmutableArray();
            stage = "CalibrateAfter";
            await Calibrate().ConfigureAwait(false);
            stage = "FinalizeAndDecodeOriginalMedia";
            media = await probe.FinishAndInspectAsync().ConfigureAwait(false);
            lifecycle = probe.CaptureStatus;
            stage = "AnalyzeSynchronization";
            analysis = RecordingSynchronization.Analyze(RecordingSynchronization.Calibrate(samples.ToImmutable()),
                originUncertainty, actions, media.SourceInPresentationOrder, media.Video);
            stage = "Complete";
        }
        catch (OperationCanceledException) { error = "RECORDING_PROBE_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch (Exception exception) when (exception is PlaywrightException or IOException or TimeoutException or UnauthorizedAccessException)
        { error = "RECORDING_PROBE_FAILED"; }
        finally { lifecycle = ownedProbe?.CaptureStatus; }
        var receipt = new RecordingSynchronizationReceipt("recording-synchronization-v1", folder, stage, error,
            analysis, samples.ToImmutable(), originUncertainty, actions, steps.ToImmutable(), timeline, media, lifecycle, environment)
        { Shutdown = ownedProbe?.ShutdownStatus };
        Directory.CreateDirectory(folder);
        await WriteNewAsync(Path.Combine(folder, "receipt.json"), JsonSerializer.Serialize(receipt, TraversalDemo.JsonOptions)).ConfigureAwait(false);
        var recordingLink = !File.Exists(Path.Combine(folder, "original.webm")) ? "No original recording finalized."
            : media is null ? "[Retained original (unverified)](original.webm)"
            : "[Original recording](original.webm)";
        var report = "# Controlled recording synchronization\n\n"
            + $"Stage: {stage}. Result: {error ?? analysis?.ReasonCode ?? "Unverified"}.\n\n"
            + recordingLink + " · [Structured receipt, clock samples, reader events and hashes](receipt.json)\n\n"
            + "One actual baseline Tab establishes button focus, then twenty reader ArrowRight commands each verify an exact exposed heading goal. "
            + "A deliberate 80 ms hold between markers is measurement time, not a user-effort metric. "
            + "Video frame zero is anchored to independently decoded raw-source marker/tick evidence, not fitted to the tested objectives. "
            + "Conservative errors include anchor ambiguity, clock round trips, reader-origin bracketing and frame quantization.\n\n"
            + "| Objective | Video frame | Measured lag ms | Conservative absolute error ms |\n| --- | --- | --- | --- |\n"
            + string.Join("\n", (analysis?.Markers ?? []).Select(m =>
                FormattableString.Invariant($"| {m.Objective} | {m.VideoFrameIndex?.ToString() ?? "Unknown"} | {m.MeasuredLagMilliseconds:F3} | {m.ConservativeAbsoluteErrorMilliseconds:F3} |")))
            + "\n\nOriginal media are unmodified. This controlled fixture does not enable arbitrary recording, credential entry or human input. "
            + "Unknown timing or an engine error is not an accessibility barrier.\n";
        await WriteNewAsync(Path.Combine(folder, "index.md"), report).ConfigureAwait(false);
        return receipt;
    }

    private static async Task WriteNewAsync(string path, string text)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text)).ConfigureAwait(false);
    }
}
