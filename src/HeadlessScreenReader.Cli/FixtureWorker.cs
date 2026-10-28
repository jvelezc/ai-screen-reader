using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

public static class FixtureWorker
{
    public static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<FixtureObservation> ObserveAsync(FixtureJob job, CancellationToken token = default)
    {
        job.Validate();
        if (job.RecordObservation) throw new BrowserOperationException("WORKER_RECORDING_BUNDLE_REQUIRED");
        if (BundledFixtureCatalog.Describe(BundledFixture.Traversal).ContentSha256 != FixtureObservation.ContentSha256)
            throw new BrowserOperationException("WORKER_FIXTURE_IDENTITY_CHANGED");
        var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: token);
        AccessibilitySnapshot snapshot;
        try { snapshot = await browser.CaptureAsync(token); }
        finally { await browser.DisposeAsync(); }
        var receipt = new FixtureObservation(FixtureObservation.CurrentSchema, job, "synthetic-shop",
            FixtureObservation.Profile, FixtureObservation.ContentSha256, snapshot, 0, false, browser.ShutdownStatus);
        receipt.Validate(job);
        return receipt;
    }

    public static async Task<int> RunAsync(Stream input, Stream output)
    {
        try
        {
            using var bytes = new MemoryStream();
            var buffer = new byte[1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer);
                if (read == 0) break;
                if (bytes.Length + read > 1024) throw new BrowserOperationException("WORKER_DESCRIPTOR_INVALID");
                bytes.Write(buffer, 0, read);
            }
            var job = JsonSerializer.Deserialize<FixtureJob>(bytes.ToArray(), Json)
                ?? throw new BrowserOperationException("WORKER_DESCRIPTOR_INVALID");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var result = job.TraversalRoute > 0 ? await KeyboardFixtureWorker.RecordAsync(job, timeout.Token)
                : job.RecordObservation ? await RecordAsync(job, timeout.Token)
                : JsonSerializer.SerializeToUtf8Bytes(await ObserveAsync(job, timeout.Token), Json);
            if (result.Length > (job.RecordObservation ? PassiveObservationRecording.MaximumBundleBytes : FixtureObservation.MaximumBytes))
                throw new BrowserOperationException("WORKER_OBSERVATION_TOO_LARGE");
            await output.WriteAsync(result);
            return 0;
        }
        catch (Exception)
        {
            // Engine/runtime messages can contain private data. Never forward them to the coordinator log.
            Console.Error.WriteLine("FIXTURE_OBSERVATION_UNCONFIRMED");
            return 3;
        }
    }

    public static async Task<byte[]> RecordAsync(FixtureJob job, CancellationToken token = default)
    {
        job.Validate();
        if (!job.RecordObservation || job.TraversalRoute != 0 || BundledFixtureCatalog.Describe(BundledFixture.TraversalRecording).ContentSha256 != FixtureObservation.RecordingContentSha256)
            throw new BrowserOperationException("WORKER_RECORDING_DESCRIPTOR_INVALID");
        var folder = Path.Combine(Path.GetTempPath(), "hsr-passive-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var probe = await ChromiumFixtureBrowser.LaunchTraversalRecordingAsync(folder, token);
            var snapshot = await probe.Browser.CaptureAsync(token);
            // Measurement hold only: no reader commands, input, traversal or objective.
            await Task.Delay(350, token);
            var media = await probe.FinishAndInspectAsync();
            token.ThrowIfCancellationRequested();
            var observation = new FixtureObservation(FixtureObservation.CurrentSchema, job, "synthetic-shop",
                FixtureObservation.Profile, FixtureObservation.RecordingContentSha256, snapshot, 0, false, probe.ShutdownStatus);
            observation.Validate(job);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(observation, Json);
            var video = await File.ReadAllBytesAsync(Path.Combine(folder, PassiveObservationRecording.VideoFile), token);
            var original = media.Artifacts.Single(a => a.File == PassiveObservationRecording.VideoFile);
            if (original.Sha256 != TraversalArchive.Hash(video) || original.Bytes != video.Length || original.DecodedFrames != media.Video.Length)
                throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
            var digest = TraversalArchive.Hash(bytes);
            var capture = new PassiveObservationRecording(PassiveObservationRecording.CurrentSchema, job, digest,
                original.Sha256, video.Length, original.DecodedFrames, media.Source.Length, probe.CaptureStatus,
                [new("capture-1", snapshot.DocumentId, snapshot.CaptureEpoch, digest)], []);
            return new PassiveRecordingBundle(bytes, video, JsonSerializer.SerializeToUtf8Bytes(capture, Json)).Encode(job);
        }
        finally
        {
            // The only removal target is the newly owned fixed-prefix temporary directory.
            if (Path.GetDirectoryName(Path.GetFullPath(folder)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
                throw new IOException("WORKER_RECORDING_CLEANUP_SCOPE_INVALID");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
