using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class AdmittedKeyboardRecordingTests
{
    private static readonly Lazy<Task<(FixtureJob Job, KeyboardRecordingBundle Bundle)>> Original = new(async () =>
    {
        var job = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 };
        return (job, KeyboardRecordingBundle.Decode(await KeyboardFixtureWorker.RecordAsync(job), job));
    });

    [Theory]
    [InlineData("tenant")]
    [InlineData("generation")]
    [InlineData("route")]
    [InlineData("plan")]
    [InlineData("video")]
    [InlineData("source-hash")]
    [InlineData("source-path")]
    [InlineData("empty-frames")]
    [InlineData("graph-edge")]
    [InlineData("duplicate-edge")]
    [InlineData("invented-unresolved")]
    [InlineData("graph-node")]
    [InlineData("key-completion")]
    [InlineData("capture-open")]
    [InlineData("decode")]
    [InlineData("alignment")]
    [InlineData("collector")]
    [InlineData("source-order-missing")]
    [InlineData("source-order-duplicate")]
    [InlineData("source-order-outside")]
    [InlineData("source-frame-duplicate")]
    public async Task BoundKeyboardOriginalsRejectChangedScopeMediaGraphAndTiming(string change)
    {
        var (job, bundle) = await Original.Value;
        var recording = JsonSerializer.Deserialize<AdmittedKeyboardRecording>(bundle.Traversal, ObservationStore.Json)!;
        recording = change switch
        {
            "tenant" => recording with { Job = job with { OrganizationId = Guid.NewGuid() } },
            "generation" => recording with { Job = job with { Generation = 2 } },
            "route" => recording with { Job = job with { TraversalRoute = 2 } },
            "plan" => recording with { Receipt = recording.Receipt with { Plan = recording.Receipt.Plan with { Instructions = [] } } },
            "video" => recording with { VideoSha256 = new('f', 64) },
            "source-hash" => recording with { SourceFramesSha256 = new('f', 64) },
            "source-path" => recording with { SourceFiles = [recording.SourceFiles[0] with { Path = "../outside.jpg" }] },
            "empty-frames" => recording with { SourceFrames = [] },
            "graph-edge" => recording with { Graph = recording.Graph with { Edges = [] } },
            "duplicate-edge" => recording with { Graph = recording.Graph with { Edges = [recording.Graph.Edges[0], recording.Graph.Edges[0]] } },
            "invented-unresolved" => recording with { Graph = recording.Graph with { UnresolvedAttempts = [recording.Receipt.Attempts[0]] } },
            "graph-node" => recording with { Graph = recording.Graph with { Nodes = [recording.Graph.Nodes[0] with { DocumentId = "foreign" }] } },
            "key-completion" => recording with { Graph = recording.Graph with { Edges = [recording.Graph.Edges[0] with { KeyboardInputs = [new("Tab", 99999, 99999)] }, recording.Graph.Edges[1]] } },
            "capture-open" => recording with { Capture = recording.Capture with { Phase = CapturePhase.Permitted } },
            "decode" => recording with { DecodedVideoFrames = 0 },
            "alignment" => recording with { Alignment = recording.Alignment with { VideoSha256 = new('f', 64) } },
            "collector" => recording with { CollectorSha256 = "unknown" },
            "source-order-missing" => recording with { SourcePresentationOrder = [] },
            "source-order-duplicate" => recording with { SourcePresentationOrder = recording.SourcePresentationOrder.SetItem(1, recording.SourcePresentationOrder[0]) },
            "source-order-outside" => recording with { SourcePresentationOrder = recording.SourcePresentationOrder.SetItem(0, -1) },
            "source-frame-duplicate" => recording with { SourceFrames = recording.SourceFrames.SetItem(1, recording.SourceFrames[0]) },
            _ => throw new InvalidOperationException()
        };
        var changed = bundle with { Traversal = JsonSerializer.SerializeToUtf8Bytes(recording, ObservationStore.Json) };
        Assert.Throws<BrowserOperationException>(() => changed.Encode(job));
    }

    [Theory]
    [InlineData(1, TraversalReachability.Reached, 2)]
    [InlineData(2, TraversalReachability.NotReached, 0)]
    [InlineData(3, TraversalReachability.Unknown, 3)]
    [InlineData(4, TraversalReachability.Unknown, 4)]
    [Trait("Category", "Browser")]
    public async Task ActualAuthoredKeyboardRouteRetainsOriginalMediaGraphAndIndependentOutcome(int route, TraversalReachability reachability, int keys)
    {
        var job = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = route };
        var bytes = await KeyboardFixtureWorker.RecordAsync(job);
        var bundle = KeyboardRecordingBundle.Decode(bytes, job); var recording = bundle.Validate(job);
        Assert.Equal(reachability, recording.Receipt.Reachability); Assert.Equal(keys, recording.Receipt.CompletedKeystrokes);
        Assert.Equal(keys, recording.Receipt.AttemptedKeystrokes);
        Assert.Equal(recording.Receipt.Observations.Length, recording.Graph.Nodes.Length);
        Assert.NotEmpty(recording.Graph.Edges); Assert.True(recording.DecodedVideoFrames > 0); Assert.NotEmpty(recording.SourceFiles);
        Assert.Equal(CapturePhase.Closed, recording.Capture.Phase); Assert.Contains(recording.Capture.Events, e => e.Phase == CapturePhase.Excluded);
        Assert.Equal(recording.Receipt.RunId, recording.Alignment.RunId); Assert.Equal(TraversalArchive.Hash(bundle.Video), recording.Alignment.VideoSha256);
        Assert.Equal(0, ObservationStore.Validate(bundle.Observation, job).DispatchedInputs);
        Assert.False(ObservationStore.Validate(bundle.Observation, job).GoalVerified);
        foreach (var snapshot in recording.Receipt.Observations.Select(o => o.Snapshot))
            Assert.DoesNotContain(snapshot.Nodes, n => n.Role is "textbox" or "code");
        var root = Path.Combine(Path.GetTempPath(), "hsr-keyboard-originals-" + Guid.NewGuid().ToString("N"));
        try
        {
            var objects = new FileTraversalObjectStore(root); var archive = new TraversalArchive(objects);
            var worker = Guid.NewGuid(); var stored = await new ObservationStore(Path.Combine(root, "baseline"), producer: new(archive)).PersistAsync(bytes, job, default, worker);
            Assert.Equal("QueueWorkerTraversal", stored.WorkerArchive!.Origin); Assert.Equal("KeyboardTraversal", stored.WorkerArchive.Recording);
            Assert.Equal(worker, stored.WorkerArchive.WorkerId); Assert.Equal(5, stored.WorkerArchive.Receipt.Files);
            stored.WorkerArchive.Validate(job, worker);
            var verify = await new TraversalArchive(new FileTraversalObjectStore(root)).VerifyAsync(stored.WorkerArchive.Receipt.Identity, default);
            Assert.True(verify.IntegrityMatches); Assert.Equal(5, verify.Files);
            if (route == 2) Assert.Equal(TraversalBarrier.Observed, recording.Receipt.Barrier);
            if (route == 3) Assert.Contains(recording.Receipt.Observations[^1].Snapshot.Nodes, n => n.Role == "dialog" && n.Name == "Address required");
            if (route == 4) Assert.Equal(VerificationStatus.TimedOut, recording.Receipt.Attempts[^1].Step!.Verification);
        }
        finally
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(root)));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
