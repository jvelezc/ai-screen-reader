using System.Text.Json;
using System.IO.Compression;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PassiveRecordingTests
{
    private static (FixtureJob Job, PassiveRecordingBundle Bundle) DeclaredBundle()
    {
        var job = FixtureWorkerTests.Job() with { RecordObservation = true };
        var receipt = FixtureWorkerTests.Receipt(job) with { FixtureSha256 = FixtureObservation.RecordingContentSha256 };
        var observation = JsonSerializer.SerializeToUtf8Bytes(receipt, ObservationStore.Json);
        byte[] video = [0x1a, 0x45, 0xdf, 0xa3, 9, 8];
        var digest = TraversalArchive.Hash(observation);
        var capture = new PassiveObservationRecording(PassiveObservationRecording.CurrentSchema, job, digest,
            TraversalArchive.Hash(video), video.Length, 1, 1, new(CapturePhase.Closed, 1,
                [new(1, CapturePhase.Excluded, 1, 0), new(2, CapturePhase.Closed, 1, 1)], false),
            [new("capture-1", receipt.Snapshot.DocumentId, receipt.Snapshot.CaptureEpoch, digest)], []);
        return (job, new(observation, video, JsonSerializer.SerializeToUtf8Bytes(capture, ObservationStore.Json)));
    }

    [Theory]
    [InlineData("unrequested")]
    [InlineData("tenant")]
    [InlineData("generation")]
    [InlineData("video")]
    [InlineData("header")]
    [InlineData("capture-job")]
    [InlineData("observation-hash")]
    [InlineData("decode")]
    [InlineData("collector")]
    [InlineData("truncated")]
    [InlineData("state")]
    [InlineData("edge")]
    public void PassiveTransportRejectsWrongScopeIncompleteCaptureAndInventedActionEdges(string failure)
    {
        var (job, bundle) = DeclaredBundle();
        var capture = JsonSerializer.Deserialize<PassiveObservationRecording>(bundle.Capture, ObservationStore.Json)!;
        switch (failure)
        {
            case "unrequested": job = job with { RecordObservation = false }; break;
            case "tenant": job = job with { OrganizationId = Guid.NewGuid() }; break;
            case "generation": job = job with { Generation = 2 }; break;
            case "video": bundle = bundle with { Video = [0x1a, 0x45, 0xdf, 0xa3, 8, 9] }; break;
            case "header": bundle = bundle with { Video = [0, 1, 2, 3] }; break;
            case "capture-job": capture = capture with { Job = job with { JobId = Guid.NewGuid() } }; break;
            case "observation-hash": capture = capture with { ObservationSha256 = new('f', 64) }; break;
            case "decode": capture = capture with { DecodedVideoFrames = 0 }; break;
            case "collector": capture = capture with { Capture = capture.Capture with { Phase = CapturePhase.Faulted } }; break;
            case "truncated": capture = capture with { Capture = capture.Capture with { EventsTruncated = true } }; break;
            case "state": capture = capture with { States = [capture.States[0] with { DocumentId = "different" }] }; break;
            case "edge": capture = capture with { Edges = [new("capture-1", "capture-1", "Enter")] }; break;
        }
        bundle = bundle with { Capture = JsonSerializer.SerializeToUtf8Bytes(capture, ObservationStore.Json) };
        Assert.Throws<BrowserOperationException>(() => bundle.Encode(job));
    }

    [Theory]
    [InlineData("../observation.json")]
    [InlineData("Observation.json")]
    [InlineData("observation.json")]
    public void ExactEntryTransportRejectsEscapedMisnamedAndDuplicatedEntriesWithoutExtractingFiles(string extra)
    {
        var (job, bundle) = DeclaredBundle(); using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, value) in new[] { ("observation.json", bundle.Observation), ("original.webm", bundle.Video), ("capture-state.json", bundle.Capture), (extra, bundle.Observation) })
            { using var output = zip.CreateEntry(path).Open(); output.Write(value); }
        }
        Assert.Equal("WORKER_PASSIVE_RECORDING_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => PassiveRecordingBundle.Decode(bytes.ToArray(), job)).Code);
    }

    [Fact]
    public async Task RecordingCannotPersistWithoutItsRequiredArchiveFinalizer()
    {
        var (job, bundle) = DeclaredBundle(); var root = Path.Combine(Path.GetTempPath(), "hsr-no-recording-write-" + Guid.NewGuid().ToString("N"));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => new ObservationStore(root).PersistAsync(bundle.Encode(job), job, default));
        Assert.Equal("WORKER_RECORDING_ARCHIVE_REQUIRED", error.Code); Assert.False(Directory.Exists(root));
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task ActualSanitizedPassiveRecordingRetainsMediaAndOneStateWithoutInputOrGoal()
    {
        var job = FixtureWorkerTests.Job() with { RecordObservation = true };
        var encoded = await FixtureWorker.RecordAsync(job);
        var bundle = PassiveRecordingBundle.Decode(encoded, job); var capture = bundle.Validate(job);
        var observation = ObservationStore.Validate(bundle.Observation, job);
        Assert.Equal(FixtureObservation.RecordingContentSha256, observation.FixtureSha256);
        Assert.DoesNotContain(observation.Snapshot.Nodes, node => node.Role is "textbox" or "code");
        Assert.Equal(0, observation.DispatchedInputs); Assert.False(observation.GoalVerified);
        Assert.True(observation.Shutdown.ProcessExitConfirmed); Assert.True(observation.Shutdown.CollectorStopAcknowledged);
        Assert.Equal(CapturePhase.Closed, capture.Capture.Phase); Assert.True(capture.DecodedVideoFrames > 0);
        Assert.True(capture.ObservedSourceFrames > 0); Assert.Single(capture.States); Assert.Empty(capture.Edges);
        Assert.Equal("NotAlignedToSnapshot", capture.Timing); Assert.Equal(TraversalArchive.Hash(bundle.Video), capture.VideoSha256);
        var objects = new TraversalArchiveTests.Objects(); var archive = new TraversalArchive(objects);
        var producer = await new WorkerEvidenceFinalizer(archive).FinalizeAsync(job, Guid.NewGuid(), bundle.Observation,
            TraversalArchive.Hash(bundle.Observation), default, bundle);
        Assert.Equal("PassiveObservation", producer.Recording); Assert.Equal(4, producer.Receipt.Files);
        Assert.True((await new TraversalArchive(objects).VerifyAsync(producer.Receipt.Identity)).IntegrityMatches);
        using var restored = new MemoryStream(); long? offset = 0;
        do
        {
            var page = await archive.ReadFileAsync(producer.Receipt.Identity, PassiveObservationRecording.VideoFile, offset.Value);
            await restored.WriteAsync(page.Data); offset = page.NextOffset;
        } while (offset is not null);
        Assert.Equal(bundle.Video, restored.ToArray());
        var manifest = await archive.ReadFileAsync(producer.Receipt.Identity, TraversalArchive.SourceManifest, 0);
        using var json = JsonDocument.Parse(manifest.Data);
        Assert.Equal("PassiveObservation", json.RootElement.GetProperty("Recording").GetString());
    }
}
