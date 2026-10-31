using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class RecordedSourceTests
{
    private static readonly FixtureJob Job = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 };
    private static readonly Lazy<Task<(byte[] Bundle, ImmutableArray<FixtureSourceFrame> Frames)>> Original = new(async () =>
    {
        var frames = ImmutableArray.CreateBuilder<FixtureSourceFrame>();
        var bytes = await KeyboardFixtureWorker.RecordAsync(Job, sourceSink: (sources, _) =>
        {
            frames.AddRange(sources.Select(source => new FixtureSourceFrame(source.Frame.Sequence, source.Frame.File,
                source.Frame.PresentationUnixMilliseconds, source.Frame.Sha256, source.Content.ToArray())));
            return Task.CompletedTask;
        });
        return (bytes, frames.ToImmutable());
    });
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-source-retention-" + Guid.NewGuid().ToString("N"));
        public string Sources => Path.Combine(Root, "sources");
        public string Commands => Path.Combine(Root, "commands");
        public Guid Worker { get; } = Guid.NewGuid();
        public NativeOwnership Native { get; } = new(NativeOwnership.CurrentSchema, Guid.NewGuid(),
            Job with { RecordObservation = false, TraversalRoute = 0 }, "podman", "sha256:" + new string('a', 64));
        public FixtureProgressStart Start => new(FixtureProgressProtocol.Schema, Job, Native.Owner) { RetainSourceFrames = true };
        public string Scope => Path.Combine(Sources, $"{Job.OrganizationId:N}-{Job.JobId:N}-{Job.Generation}");
        public Task<RecordedSourceJournal> Begin() => RecordedSourceJournal.BeginAsync(Sources, Native, Job, Worker, default);
        public Task<CommandProgressJournal> BeginCommands() => CommandProgressJournal.BeginAsync(Commands, Native, Job, Worker, default);
        public async Task<FixtureSourceBatch> Batch()
        {
            var frame = (await Original.Value).Frames[0];
            return new(FixtureSourceProtocol.Schema, Native.Owner, 1, [frame with { Content = frame.Content.ToArray() }]);
        }
        public void Dispose()
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(Root)));
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
    private static async Task<MemoryStream> Wire(params object[] values)
    {
        var stream = new MemoryStream();
        foreach (var value in values) await ReaderWorkerWire.WriteAsync(stream, FixtureProgressProtocol.Encode(value), FixtureProgressProtocol.MaximumFrameBytes);
        stream.Position = 0; return stream;
    }
    private static async Task<FixtureProgressMessage> Baseline(Files files)
    {
        var recording = KeyboardRecordingBundle.Decode((await Original.Value).Bundle, Job).Validate(Job);
        return new(FixtureProgressProtocol.Schema, files.Native.Owner, 1, new(TraversalCheckpointPhase.Baseline, 0, recording.Receipt.Observations[0]));
    }

    [Theory]
    [InlineData("execution")]
    [InlineData("schema")]
    [InlineData("sequence")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("path")]
    [InlineData("timestamp")]
    [InlineData("hash")]
    [InlineData("changed")]
    [InlineData("oversize")]
    [InlineData("not-admitted")]
    public async Task MalformedOrUnadmittedMediaCannotBecomeAStorageAcknowledgement(string change)
    {
        using var files = new Files(); var batch = await files.Batch(); var frame = batch.Frames[0]; var start = files.Start;
        switch (change)
        {
            case "execution": batch = batch with { ExecutionId = Guid.NewGuid() }; break;
            case "schema": batch = batch with { Schema = FixtureProgressProtocol.Schema }; break;
            case "sequence": batch = batch with { Sequence = 0 }; break;
            case "missing": batch = batch with { Frames = [] }; break;
            case "duplicate": batch = batch with { Frames = [frame, frame] }; break;
            case "path": batch = batch with { Frames = [frame with { File = "../source.jpg" }] }; break;
            case "timestamp": batch = batch with { Frames = [frame with { PresentationUnixMilliseconds = double.NaN }] }; break;
            case "hash": batch = batch with { Frames = [frame with { Sha256 = new string('a', 64) }] }; break;
            case "changed": frame.Content[^1] ^= 1; break;
            case "oversize": batch = batch with { Frames = [frame with { Content = new byte[FixtureSourceProtocol.MaximumBatchBytes + 1] }] }; break;
            case "not-admitted": start = start with { RetainSourceFrames = false }; break;
        }
        Assert.Equal("WORKER_PROGRESS_MEDIA_INVALID", Assert.Throws<BrowserOperationException>(() => FixtureSourceProtocol.Validate(batch, start, 1)).Code);
    }

    [Fact]
    public async Task SourceSnapshotCopiesActualBytesBeforeCallerMutation()
    {
        using var files = new Files(); var batch = await files.Batch();
        var expected = batch.Frames[0].Content.ToArray();
        var snapshot = FixtureSourceProtocol.Snapshot(batch, files.Start, 1);
        batch.Frames[0].Content[^1] ^= 1;
        Assert.Equal(expected, snapshot.Frames[0].Content);
        Assert.NotEqual(TraversalArchive.Hash(batch.Frames[0].Content), snapshot.Frames[0].Sha256);
    }

    [Fact]
    public async Task BatchByteLimitAllowsItsExactBoundaryAndRejectsTheNextByte()
    {
        using var files = new Files(); var batch = await files.Batch(); var frame = batch.Frames[0];
        var bytes = new byte[FixtureSourceProtocol.MaximumBatchBytes]; bytes[0] = 0xff; bytes[1] = 0xd8; bytes[2] = 0xff;
        frame = frame with { Content = bytes, Sha256 = TraversalArchive.Hash(bytes) };
        FixtureSourceProtocol.Validate(batch with { Frames = [frame] }, files.Start, 1);
        var smaller = bytes[..^3];
        var first = frame with { Content = smaller, Sha256 = TraversalArchive.Hash(smaller) };
        var second = frame with { SourceSequence = 2, File = "source-0002.jpg", Content = [0xff, 0xd8, 0xff, 0] };
        second = second with { Sha256 = TraversalArchive.Hash(second.Content) };
        Assert.Equal("WORKER_PROGRESS_MEDIA_INVALID", Assert.Throws<BrowserOperationException>(() =>
            FixtureSourceProtocol.Validate(batch with { Frames = [first, second] }, files.Start, 1)).Code);
        Assert.True(FixtureProgressProtocol.Encode(batch with { Frames = [frame] }).Length < FixtureProgressProtocol.MaximumFrameBytes);
    }

    [Fact]
    public async Task IndependentSourceAndDeliverySequencePreserveOutOfOrderCompletions()
    {
        using var files = new Files(); var journal = await files.Begin(); var originals = (await Original.Value).Frames;
        Assert.True(originals.Length >= 3);
        var selected = originals.Take(3).OrderByDescending(frame => frame.SourceSequence).ToArray();
        for (var i = 0; i < selected.Length; i++)
        {
            var batch = new FixtureSourceBatch(FixtureSourceProtocol.Schema, files.Native.Owner, i + 1, [selected[i]]);
            await journal.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), default);
        }
        var saved = journal.Read();
        Assert.Equal(selected.Select(frame => frame.SourceSequence), saved.Frames.Select(frame => frame.SourceSequence));
        Assert.Equal(selected.Select(frame => frame.PresentationUnixMilliseconds), saved.Frames.Select(frame => frame.PresentationUnixMilliseconds));
        journal.Match(KeyboardRecordingBundle.Decode((await Original.Value).Bundle, Job));
    }

    [Fact]
    public async Task DeclaredWireDigestCannotReplaceTheActualCanonicalSourcePacket()
    {
        using var files = new Files(); var journal = await files.Begin(); var batch = await files.Batch();
        await Assert.ThrowsAsync<BrowserOperationException>(() => journal.AppendAsync(batch, new string('a', 64), default));
        Assert.Empty(journal.Read().Frames); Assert.False(journal.Read().HasUncommittedFiles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalCapacityRetainsTheExactAllowedBoundaryAndRejectsAnotherOriginal(bool byteBound)
    {
        using var files = new Files(); var journal = await files.Begin();
        var content = new byte[byteBound ? FixtureSourceProtocol.MaximumBatchBytes : 4];
        content[0] = 0xff; content[1] = 0xd8; content[2] = 0xff;
        var hash = TraversalArchive.Hash(content); var count = byteBound ? 50 : FixtureSourceProtocol.MaximumFrames;
        for (var i = 1; i <= count; i++)
        {
            var batch = new FixtureSourceBatch(FixtureSourceProtocol.Schema, files.Native.Owner, i,
                [new(i, $"source-{i:D4}.jpg", 1000 + i, hash, content)]);
            await journal.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), default);
        }
        var nextBytes = new byte[] { 0xff, 0xd8, 0xff, 0 };
        var next = new FixtureSourceBatch(FixtureSourceProtocol.Schema, files.Native.Owner, count + 1,
            [new(count + 1, $"source-{count + 1:D4}.jpg", 2000, TraversalArchive.Hash(nextBytes), nextBytes)]);
        await Assert.ThrowsAsync<BrowserOperationException>(() => journal.AppendAsync(next, TraversalArchive.Hash(FixtureProgressProtocol.Encode(next)), default));
        var saved = journal.Read(); Assert.Equal(count, saved.Frames.Length);
        Assert.Equal(byteBound ? FixtureSourceProtocol.MaximumTotalBytes : 4L * FixtureSourceProtocol.MaximumFrames, saved.Bytes);
        Assert.False(saved.HasUncommittedFiles); Assert.False(saved.IsCompletedRecording);
    }

    [Fact]
    public async Task CancelledActualWriteLeavesOnlyUncommittedDiagnosticsWithoutASavedFrame()
    {
        using var files = new Files(); var journal = await files.Begin(); var batch = await files.Batch();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), stop.Token));
        var saved = journal.Read(); Assert.Empty(saved.Frames); Assert.Equal(0, saved.Bytes);
        Assert.True(saved.HasUncommittedFiles); Assert.False(saved.IsCompletedRecording); Assert.False(saved.CanReplay);
    }

    [Fact]
    public async Task RehashedManifestStillCannotInventAnOriginalWireDigest()
    {
        using var files = new Files(); var journal = await files.Begin(); var batch = await files.Batch();
        await journal.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), default);
        var path = Directory.GetFiles(files.Scope, "batch-*.json").Single();
        var metadata = JsonSerializer.Deserialize<RecordedSourceBatch>(File.ReadAllBytes(path), ReaderWorkerWire.Json)!;
        var changed = FixtureProgressProtocol.Encode(metadata with { WireSha256 = new string('a', 64) });
        File.Delete(path);
        File.WriteAllBytes(Path.Combine(files.Scope, $"batch-0001-{TraversalArchive.Hash(changed)}.json"), changed);
        Assert.Equal("WORKER_PROGRESS_MEDIA_INVALID", Assert.Throws<BrowserOperationException>(() => journal.Read()).Code);
    }

    [Fact]
    public async Task MatchingImageBytesCannotOverrideDifferentOriginalPresentationTiming()
    {
        using var files = new Files(); var journal = await files.Begin(); var batch = await files.Batch();
        var changed = batch with { Frames = [batch.Frames[0] with { PresentationUnixMilliseconds = batch.Frames[0].PresentationUnixMilliseconds + 1 }] };
        await journal.AppendAsync(changed, TraversalArchive.Hash(FixtureProgressProtocol.Encode(changed)), default);
        Assert.Single(journal.Read().Frames);
        var original = (await Original.Value).Bundle;
        Assert.Equal("WORKER_PROGRESS_MEDIA_INVALID", Assert.Throws<BrowserOperationException>(() =>
            journal.Match(KeyboardRecordingBundle.Decode(original, Job))).Code);
    }

    [Theory]
    [InlineData("missing-journal")]
    [InlineData("empty-prefix")]
    [InlineData("legacy-peer")]
    public async Task SourceAdmissionCannotSilentlyDowngradeToUnretainedMedia(string scenario)
    {
        using var files = new Files(); var sources = await files.Begin(); var commands = await files.BeginCommands();
        object message = scenario == "legacy-peer" ? await files.Batch() : await Baseline(files);
        using var wire = await Wire(message); using var ack = new MemoryStream(); using var stop = new CancellationTokenSource();
        var start = files.Start with { RetainSourceFrames = scenario != "legacy-peer" };
        var exchange = new FixtureProgressExchange(start, commands, _ => throw new InvalidOperationException("Missing-source command reached authority."),
            scenario == "missing-journal" ? null : sources);
        Assert.Equal(scenario == "legacy-peer" ? "WORKER_PROGRESS_MEDIA_NOT_ADMITTED" : "WORKER_PROGRESS_MEDIA_MISSING",
            (await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(wire, ack, stop))).Code);
        Assert.Equal(0, ack.Length); Assert.True(stop.IsCancellationRequested);
        Assert.Equal(0, CommandProgressJournal.Read(files.Commands, Job.OrganizationId, Job.JobId, Job.Generation).Records);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("execution")]
    [InlineData("sequence")]
    public void MediaAcknowledgementMustBindActualWireIdentityAndDigest(string change)
    {
        var execution = Guid.NewGuid(); var digest = new string('a', 64);
        var ack = new FixtureSourceAck(FixtureSourceProtocol.Schema, execution, 1, digest);
        ack = change switch
        {
            "digest" => ack with { BatchSha256 = new string('b', 64) },
            "execution" => ack with { ExecutionId = Guid.NewGuid() },
            _ => ack with { Sequence = 2 }
        };
        Assert.Equal("WORKER_PROGRESS_MEDIA_INVALID", Assert.Throws<BrowserOperationException>(() => FixtureSourceProtocol.Validate(ack, execution, 1, digest)).Code);
    }

    [Fact]
    public async Task ActualSourceOriginalsRemainPartialAndMatchTheirCompleteBundleWithoutBecomingOne()
    {
        using var files = new Files(); var journal = await files.Begin(); var batch = await files.Batch();
        await journal.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), default);
        var saved = journal.Read();
        Assert.Equal(files.Worker, saved.Header.WorkerId); Assert.Equal(Job, saved.Header.Job);
        Assert.Equal(batch.Frames[0].Sha256, Assert.Single(saved.Frames).Sha256);
        Assert.Equal(batch.Frames[0].Content, File.ReadAllBytes(Path.Combine(files.Scope, batch.Frames[0].File)));
        Assert.False(saved.HasUncommittedFiles); Assert.False(saved.IsCompletedRecording); Assert.False(saved.CanReplay);
        Assert.Equal("NotAssessed", saved.Completeness); Assert.Equal("NotAssessed", saved.GoalVerification);
        journal.Match(KeyboardRecordingBundle.Decode((await Original.Value).Bundle, Job));
        Assert.Equal(2, KeyboardRecordingBundle.Decode((await Original.Value).Bundle, Job).Validate(Job).Receipt.CompletedKeystrokes);
    }

    [Fact]
    public async Task RepeatedSourceIdentityCannotReplacePreviouslyRetainedBytes()
    {
        using var files = new Files(); var journal = await files.Begin(); var batch = await files.Batch();
        var hash = TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch));
        await journal.AppendAsync(batch, hash, default);
        var repeated = batch with { Sequence = 2 };
        await Assert.ThrowsAsync<BrowserOperationException>(() => journal.AppendAsync(repeated, TraversalArchive.Hash(FixtureProgressProtocol.Encode(repeated)), default));
        Assert.Equal(batch.Frames[0].Content, File.ReadAllBytes(Path.Combine(files.Scope, batch.Frames[0].File)));
        Assert.Single(journal.Read().Frames);
    }

    [Fact]
    public async Task MissingManifestDoesNotTurnAnOrphanOriginalIntoRetainedMedia()
    {
        using var files = new Files(); var journal = await files.Begin(); var frame = (await files.Batch()).Frames[0];
        File.WriteAllBytes(Path.Combine(files.Scope, frame.File), frame.Content);
        var saved = journal.Read();
        Assert.Empty(saved.Frames); Assert.True(saved.HasUncommittedFiles);
        Assert.False(saved.IsCompletedRecording); Assert.Equal("NotAssessed", saved.GoalVerification);
    }

    [Theory]
    [InlineData("producer")]
    [InlineData("bytes")]
    [InlineData("manifest")]
    public async Task ForeignOrChangedPrivateOriginalCannotBeReadAsTheAdmittedSource(string failure)
    {
        using var files = new Files(); var journal = await files.Begin(); var batch = await files.Batch();
        await journal.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), default);
        if (failure == "bytes") File.WriteAllBytes(Path.Combine(files.Scope, batch.Frames[0].File), [0xff, 0xd8, 0xff, 0]);
        if (failure == "manifest")
        {
            var path = Directory.GetFiles(files.Scope, "batch-*.json").Single();
            File.WriteAllText(path, File.ReadAllText(path).Replace(batch.Frames[0].Sha256, new string('a', 64), StringComparison.Ordinal));
        }
        Assert.Throws<BrowserOperationException>(() => RecordedSourceJournal.Read(files.Sources, Job.OrganizationId, Job.JobId,
            Job.Generation, failure == "producer" ? Guid.NewGuid() : files.Worker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaAuthorityMustRemainValidAfterActualRetentionBeforeAnyAcknowledgement(bool denyAfterWrite)
    {
        using var files = new Files(); var journal = await files.Begin(); var commands = await files.BeginCommands(); var batch = await files.Batch();
        using var wire = await Wire(batch); using var ack = new MemoryStream(); using var stop = new CancellationTokenSource();
        var calls = 0;
        var exchange = new FixtureProgressExchange(files.Start, commands, _ => Task.FromResult(denyAfterWrite && ++calls == 1), journal);
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(wire, ack, stop));
        Assert.Equal("WORKER_PROGRESS_AUTHORITY_UNCONFIRMED", error.Code);
        Assert.Equal(0, ack.Length); Assert.True(stop.IsCancellationRequested);
        Assert.Equal(denyAfterWrite ? 1 : 0, journal.Read().Frames.Length);
    }

    [Fact]
    public async Task SourceBatchesAndLegacyCommandProgressHaveDistinctDurableAcknowledgements()
    {
        using var files = new Files(); var journal = await files.Begin(); var commands = await files.BeginCommands(); var batch = await files.Batch();
        var baseline = await Baseline(files); using var wire = await Wire(batch, baseline); using var ack = new MemoryStream();
        using var stop = new CancellationTokenSource(); var calls = 0;
        var exchange = new FixtureProgressExchange(files.Start, commands, _ => Task.FromResult(++calls > 0), journal);
        await exchange.ReadAsync(wire, ack, stop); ack.Position = 0;
        FixtureSourceProtocol.Validate(await FixtureProgressProtocol.ReadAsync<FixtureSourceAck>(ack, 4096, default), files.Native.Owner, 1,
            TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)));
        FixtureProgressProtocol.Validate(await FixtureProgressProtocol.ReadAsync<FixtureProgressAck>(ack, 4096, default), files.Native.Owner, 1);
        Assert.Null(await ReaderWorkerWire.ReadAsync(ack, 4096)); Assert.Equal(4, calls);
        Assert.Single(journal.Read().Frames); Assert.Equal(1, CommandProgressJournal.Read(files.Commands, Job.OrganizationId, Job.JobId, Job.Generation).Records);
    }

    [Fact]
    public async Task LostCheckpointAuthorityAfterWriteCannotAcknowledgeAnInputBaseline()
    {
        using var files = new Files(); var commands = await files.BeginCommands();
        using var wire = await Wire(await Baseline(files)); using var ack = new MemoryStream(); using var stop = new CancellationTokenSource();
        var calls = 0;
        var exchange = new FixtureProgressExchange(files.Start with { RetainSourceFrames = false }, commands, _ => Task.FromResult(++calls == 1));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(wire, ack, stop));
        Assert.Equal("WORKER_PROGRESS_AUTHORITY_UNCONFIRMED", error.Code); Assert.Equal(0, ack.Length);
        Assert.Equal(1, CommandProgressJournal.Read(files.Commands, Job.OrganizationId, Job.JobId, Job.Generation).Records);
        Assert.True(stop.IsCancellationRequested);
    }

    [Fact]
    public async Task ActualChromiumMediaWireMatchesRetainedOriginalsAndTheFinalBundle()
    {
        using var files = new Files(); var journal = await files.Begin(); var commands = await files.BeginCommands();
        var input = new Pipe(); var progress = new Pipe(); using var output = new MemoryStream(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var exchange = new FixtureProgressExchange(files.Start, commands, _ => Task.FromResult(true), journal);
        var reading = exchange.ReadAsync(progress.Reader.AsStream(), input.Writer.AsStream(), stop);
        var child = FixtureProgressWorker.RunAsync(input.Reader.AsStream(), output, progress.Writer.AsStream());
        await exchange.StartAsync(input.Writer.AsStream(), stop.Token);
        try
        {
            Assert.Equal(0, await child.WaitAsync(stop.Token));
            await progress.Writer.CompleteAsync(); await reading;
            var bundle = KeyboardRecordingBundle.Decode(output.ToArray(), Job);
            exchange.Match(bundle.Validate(Job).Receipt); exchange.MatchSources(bundle);
            Assert.True(journal.Read().Frames.Length >= 3); Assert.False(journal.Read().IsCompletedRecording);
            Assert.Equal(2, bundle.Validate(Job).Receipt.CompletedKeystrokes);
            Assert.Equal(5, CommandProgressJournal.Read(files.Commands, Job.OrganizationId, Job.JobId, Job.Generation).Records);
        }
        finally
        {
            await input.Writer.CompleteAsync(); await progress.Writer.CompleteAsync(); await child;
            try { await reading; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    [Fact]
    public async Task ActualCaptureExclusionJoinsPendingOriginalTransferEvenWhenItsCallerIsCancelled()
    {
        using var files = new Files();
        await using var probe = await ChromiumFixtureBrowser.LaunchTraversalRecordingAsync(Path.Combine(files.Root, "probe"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        var copying = probe.CopyCompletedSourcesAsync(async (originals, _) =>
        { Assert.NotEmpty(originals); entered.SetResult(); await release.Task; }, caller.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var excluding = Assert.IsAssignableFrom<ICaptureControl>(probe.Browser).ExcludeCaptureAsync();
        try
        {
            await NativeShutdownTestSync.UntilAsync(() => probe.CaptureStatus.Phase == CapturePhase.Draining);
            caller.Cancel(); Assert.False(excluding.IsCompleted); Assert.False(copying.IsCompleted);
            release.SetResult();
            var error = await Assert.ThrowsAsync<BrowserOperationException>(() => copying);
            Assert.Equal("WORKER_PROGRESS_MEDIA_EXPORT_UNCONFIRMED", error.Code);
            await excluding.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(CapturePhase.Excluded, probe.CaptureStatus.Phase);
            Assert.Equal("WORKER_PROGRESS_MEDIA_CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
                probe.CopyCompletedSourcesAsync((_, _) => throw new InvalidOperationException("Excluded transfer ran.")))).Code);
        }
        finally { release.TrySetResult(); await excluding; }
    }

    [Fact]
    public async Task InterruptedActualKeyboardAttemptKeepsOriginalSourcesWithoutACompleteBundle()
    {
        using var files = new Files(); var sources = await files.Begin(); var commands = await files.BeginCommands();
        var batchSequence = 0; var commandSequence = 0;
        await Assert.ThrowsAsync<BrowserOperationException>(() => KeyboardFixtureWorker.RecordAsync(Job, checkpoint: async (checkpoint, token) =>
        {
            await commands.AppendAsync(new(FixtureProgressProtocol.Schema, files.Native.Owner, ++commandSequence, checkpoint), token);
            if (checkpoint.Phase == TraversalCheckpointPhase.Observed && checkpoint.CommandIndex == 1)
                throw new BrowserOperationException("TEST_RECORDING_INTERRUPTED");
        }, sourceSink: async (originals, token) =>
        {
            var batch = new FixtureSourceBatch(FixtureSourceProtocol.Schema, files.Native.Owner, ++batchSequence,
                [.. originals.Select(source => new FixtureSourceFrame(source.Frame.Sequence, source.Frame.File,
                    source.Frame.PresentationUnixMilliseconds, source.Frame.Sha256, source.Content))]);
            await sources.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), token);
        }));
        var saved = sources.Read(); var progress = CommandProgressJournal.Read(files.Commands, Job.OrganizationId, Job.JobId, Job.Generation);
        Assert.NotEmpty(saved.Frames); Assert.False(saved.HasUncommittedFiles); Assert.False(saved.IsCompletedRecording);
        Assert.False(saved.CanReplay); Assert.Equal("NotAssessed", saved.GoalVerification);
        Assert.Equal(files.Native.Owner, saved.Header.Native.Owner); Assert.Equal(files.Worker, saved.Header.WorkerId);
        Assert.Equal(1, progress.Intents); Assert.Equal(1, progress.Results); Assert.Equal(1, progress.Last!.Attempt!.Step!.BrowserKeystrokes);
        Assert.True(progress.InputMayHaveOccurred); Assert.False(progress.IsCompletionReceipt);
    }

    [Fact]
    public async Task MediaStorageFailureCannotAcknowledgeOrAdmitACommandBaseline()
    {
        using var files = new Files(); var sources = await files.Begin(); var commands = await files.BeginCommands(); var batch = await files.Batch();
        Directory.CreateDirectory(Path.Combine(files.Scope, batch.Frames[0].File + ".pending"));
        using var wire = await Wire(batch, await Baseline(files)); using var ack = new MemoryStream(); using var stop = new CancellationTokenSource();
        var exchange = new FixtureProgressExchange(files.Start, commands, _ => Task.FromResult(true), sources);
        Assert.Equal("WORKER_PROGRESS_MEDIA_WRITE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(wire, ack, stop))).Code);
        Assert.Equal(0, ack.Length); Assert.Equal(0, sources.RetainedFrames); Assert.True(stop.IsCancellationRequested);
        Assert.Equal(0, CommandProgressJournal.Read(files.Commands, Job.OrganizationId, Job.JobId, Job.Generation).Records);
    }

    [Fact]
    public async Task WrongSourceAcknowledgementStopsActualChildBeforeAnyCommandCheckpoint()
    {
        using var files = new Files(); var input = new Pipe(); var progress = new Pipe(); using var output = new MemoryStream();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var child = FixtureProgressWorker.RunAsync(input.Reader.AsStream(), output, progress.Writer.AsStream());
        await ReaderWorkerWire.WriteAsync(input.Writer.AsStream(), FixtureProgressProtocol.Encode(files.Start), 4096, stop.Token);
        var batch = await FixtureProgressProtocol.ReadAsync<FixtureSourceBatch>(progress.Reader.AsStream(), FixtureProgressProtocol.MaximumFrameBytes, stop.Token);
        FixtureSourceProtocol.Validate(batch, files.Start, 1);
        await ReaderWorkerWire.WriteAsync(input.Writer.AsStream(), FixtureProgressProtocol.Encode(
            new FixtureSourceAck(FixtureSourceProtocol.Schema, files.Native.Owner, 1, new string('a', 64))), 4096, stop.Token);
        try
        {
            Assert.Equal(3, await child.WaitAsync(stop.Token)); Assert.Equal(0, output.Length);
            await progress.Writer.CompleteAsync(); Assert.Null(await ReaderWorkerWire.ReadAsync(progress.Reader.AsStream(), FixtureProgressProtocol.MaximumFrameBytes, stop.Token));
        }
        finally { await input.Writer.CompleteAsync(); await progress.Writer.CompleteAsync(); await child; }
    }
}
