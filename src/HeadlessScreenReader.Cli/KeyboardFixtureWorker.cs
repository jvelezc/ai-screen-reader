using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>Only the four fixed sanitized plans. No caller-supplied instructions, credentials or URLs.</summary>
public static class KeyboardFixtureWorker
{
    public static async Task<byte[]> RecordAsync(FixtureJob job, CancellationToken token = default,
        Func<TraversalCheckpoint, CancellationToken, Task>? checkpoint = null,
        Func<ImmutableArray<CompletedCaptureSource>, CancellationToken, Task>? sourceSink = null)
    {
        job.Validate();
        if (job.TraversalRoute is < 1 or > 4) throw new BrowserOperationException("WORKER_KEYBOARD_ROUTE_INVALID");
        if (BundledFixtureCatalog.Describe(BundledFixture.TraversalRecording).ContentSha256 != FixtureObservation.RecordingContentSha256)
            throw new BrowserOperationException("WORKER_FIXTURE_IDENTITY_CHANGED");
        var folder = Path.Combine(Path.GetTempPath(), "hsr-keyboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await TraversalRecordingDemo.RunRouteAsync(folder, TraversalDemo.Plans[job.TraversalRoute - 1], token, checkpoint, sourceSink);
            token.ThrowIfCancellationRequested();
            if (result.Receipt is not { Observations.IsDefaultOrEmpty: false } receipt || result.Recording.Media is not { } media
                || result.Recording.Shutdown is not { } shutdown || result.Recording.CaptureLifecycle is not { } capture
                || result.Recording.Alignment is not { } alignment || result.Recording.RunnerOriginHostMilliseconds is not { } origin
                || result.Recording.ErrorCode is not null)
                throw new BrowserOperationException(result.Recording.ErrorCode ?? "WORKER_KEYBOARD_RECORDING_UNCONFIRMED");
            // These zero-input facts describe only the retained pre-traversal capture.
            // The actual route's attempted/completed inputs and verification remain in traversal.json.
            var baseline = new FixtureObservation(FixtureObservation.CurrentSchema, job, "synthetic-shop", FixtureObservation.Profile,
                FixtureObservation.RecordingContentSha256, receipt.Observations[0].Snapshot, 0, false, shutdown)
            { RecordedSnapshots = receipt.Observations };
            baseline.Validate(job);
            var observation = JsonSerializer.SerializeToUtf8Bytes(baseline, ObservationStore.Json);
            var video = await File.ReadAllBytesAsync(Path.Combine(folder, PassiveObservationRecording.VideoFile), token);
            var original = media.Artifacts.Single(a => a.File == PassiveObservationRecording.VideoFile);
            if (TraversalArchive.Hash(video) != original.Sha256 || original.Bytes != video.Length || original.DecodedFrames != media.Video.Length)
                throw new BrowserOperationException("WORKER_KEYBOARD_ORIGINAL_CHANGED");
            var sources = ImmutableArray.CreateBuilder<RecordedSourceFile>();
            using var sourceBytes = new MemoryStream();
            using (var zip = new ZipArchive(sourceBytes, ZipArchiveMode.Create, true))
                foreach (var frame in media.Artifacts.Where(a => a.File.EndsWith(".jpg", StringComparison.Ordinal)))
                {
                    var bytes = await File.ReadAllBytesAsync(Path.Combine(folder, frame.File), token);
                    if (bytes.Length != frame.Bytes || TraversalArchive.Hash(bytes) != frame.Sha256)
                        throw new BrowserOperationException("WORKER_KEYBOARD_ORIGINAL_CHANGED");
                    sources.Add(new(frame.File, frame.Bytes, frame.Sha256));
                    using var output = zip.CreateEntry(frame.File, CompressionLevel.NoCompression).Open(); output.Write(bytes);
                }
            var frames = sourceBytes.ToArray();
            var recording = new AdmittedKeyboardRecording(AdmittedKeyboardRecording.CurrentSchema, job, TraversalArchive.Hash(observation),
                original.Sha256, video.Length, original.DecodedFrames, TraversalArchive.Hash(frames), sources.ToImmutable(), receipt,
                TraversalProjection.Build(receipt), alignment, capture, result.Recording.ClockSamples, origin, media.Source, media.Video,
                TraversalArchive.Hash(await File.ReadAllBytesAsync(typeof(KeyboardFixtureWorker).Assembly.Location, token)),
                typeof(KeyboardFixtureWorker).Assembly.GetName().Version?.ToString() ?? "unknown", result.Recording.Stage, result.Recording.ErrorCode)
            {
                SourcePresentationOrder = media.SourcePresentationOrder,
                Environment = new(result.Environment!.BrowserVersion, result.Environment.PlaywrightVersion,
                    System.Runtime.InteropServices.RuntimeInformation.OSDescription, "320x240")
            };
            return new KeyboardRecordingBundle(observation, video, JsonSerializer.SerializeToUtf8Bytes(recording, ObservationStore.Json), frames).Encode(job);
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(folder)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
                throw new IOException("WORKER_KEYBOARD_CLEANUP_SCOPE_INVALID");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
