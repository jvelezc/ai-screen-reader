using System.IO.Pipelines;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class CommandProgressTests
{
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-command-progress-" + Guid.NewGuid().ToString("N"));
        public FixtureJob Job { get; } = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 };
        public Guid Worker { get; } = Guid.NewGuid();
        public NativeOwnership Native { get; }
        public FixtureProgressStart Start => new(FixtureProgressProtocol.Schema, Job, Native.Owner);
        public string Scope => Path.Combine(Root, $"{Job.OrganizationId:N}-{Job.JobId:N}-{Job.Generation}");
        public Files() => Native = new(NativeOwnership.CurrentSchema, Guid.NewGuid(), Job with { RecordObservation = false, TraversalRoute = 0 }, "podman", "sha256:" + new string('a', 64));
        public Task<CommandProgressJournal> Begin() => CommandProgressJournal.BeginAsync(Root, Native, Job, Worker, default);
        public CommandProgressSummary Read() => CommandProgressJournal.Read(Root, Job.OrganizationId, Job.JobId, Job.Generation);
        public FixtureProgressMessage[] Messages()
        {
            var source = new TraversalObservation("o1", FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Snapshot with { DocumentUrl = null }, ReaderMode.Browse, null, new string('a', 64), 0);
            var instruction = AuthoredKeyboardRoutes.Plans[0].Instructions[0];
            var result = source with { Id = "o2", ElapsedMilliseconds = 2 };
            var attempt = new TraversalAttempt("a1", source.Id, result.Id, instruction, null, [], false, 1, 2);
            return [new(FixtureProgressProtocol.Schema, Native.Owner, 1, new(TraversalCheckpointPhase.Baseline, 0, source)),
                new(FixtureProgressProtocol.Schema, Native.Owner, 2, new(TraversalCheckpointPhase.Intent, 1, source, instruction)),
                new(FixtureProgressProtocol.Schema, Native.Owner, 3, new(TraversalCheckpointPhase.Observed, 1, source, instruction, attempt, result))];
        }
        public void Dispose()
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(Root)));
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
    private static async Task<MemoryStream> Frames(IEnumerable<FixtureProgressMessage> messages)
    {
        var stream = new MemoryStream();
        foreach (var message in messages) await ReaderWorkerWire.WriteAsync(stream, FixtureProgressProtocol.Encode(message), FixtureProgressProtocol.MaximumFrameBytes);
        stream.Position = 0; return stream;
    }

    [Fact]
    public async Task RetainedPrefixSurvivesReopenWithoutBecomingCompletionOrReplayAuthority()
    {
        using var files = new Files(); var journal = await files.Begin(); var messages = files.Messages();
        Assert.Equal(0, files.Read().Records);
        await journal.AppendAsync(messages[0], default); await journal.AppendAsync(messages[1], default);
        var pending = files.Read();
        Assert.Equal(files.Job, pending.Header.Job); Assert.Equal(files.Worker, pending.Header.WorkerId);
        Assert.Equal(files.Native, pending.Header.Native); Assert.Equal(2, pending.Records);
        Assert.Equal(1, pending.Intents); Assert.Equal(0, pending.Results); Assert.True(pending.InputMayHaveOccurred);
        Assert.Equal(TraversalCheckpointPhase.Intent, pending.Last!.Phase); Assert.False(pending.IsCompletionReceipt); Assert.False(pending.CanReplay);
        await journal.AppendAsync(messages[2], default);
        var observed = files.Read();
        Assert.Equal(3, observed.Records); Assert.Equal(1, observed.Results);
        Assert.Equal("o2", observed.Last!.Result!.Id); Assert.Null(observed.Last.Attempt!.Step);
        Assert.NotEqual(pending.LastSha256, observed.LastSha256); Assert.False(observed.IsCompletionReceipt);
        Assert.False(observed.CanReplay);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("changed")]
    [InlineData("gap")]
    [InlineData("renamed")]
    [InlineData("header")]
    [InlineData("directory")]
    [InlineData("excess")]
    public async Task TruncatedChangedGappedForeignAndUnexpectedInventoryCannotYieldProgress(string corruption)
    {
        using var files = new Files(); var journal = await files.Begin();
        foreach (var message in files.Messages()) await journal.AppendAsync(message, default);
        var records = Directory.GetFiles(files.Scope, "0*.json").Order(StringComparer.Ordinal).ToArray();
        if (corruption == "truncated") File.WriteAllText(records[1], "{");
        if (corruption == "changed") File.AppendAllText(records[1], " ");
        if (corruption == "gap") File.Delete(records[0]);
        if (corruption == "renamed") File.Move(records[1], Path.Combine(files.Scope, "0002-unrelated.json"));
        if (corruption == "header")
        {
            var path = Path.Combine(files.Scope, "start.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace(files.Worker.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal));
        }
        if (corruption == "directory") Directory.CreateDirectory(Path.Combine(files.Scope, "unexpected"));
        if (corruption == "excess") for (var i = 0; i < 12; i++) File.WriteAllText(Path.Combine(files.Scope, $"extra-{i}.json"), "{}");
        var error = Assert.Throws<BrowserOperationException>(files.Read);
        Assert.Equal("WORKER_PROGRESS_INVALID", error.Code);
    }

    [Fact]
    public async Task ExistingAttemptCannotBeOverwrittenAndAnotherScopeCannotInspectIt()
    {
        using var files = new Files(); var journal = await files.Begin(); await journal.AppendAsync(files.Messages()[0], default);
        var before = File.ReadAllBytes(Path.Combine(files.Scope, "start.json"));
        await Assert.ThrowsAsync<IOException>(files.Begin);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(files.Scope, "start.json")));
        Assert.Throws<BrowserOperationException>(() => CommandProgressJournal.Read(files.Root, Guid.NewGuid(), files.Job.JobId, 1));
        Assert.Throws<BrowserOperationException>(() => CommandProgressJournal.Read(files.Root, files.Job.OrganizationId, files.Job.JobId, 2));
        Assert.Equal(1, files.Read().Records);
    }

    [Fact]
    public async Task InterruptedNextWriteRetainsOnlyTheCommittedPrefix()
    {
        using var files = new Files(); var journal = await files.Begin();
        await journal.AppendAsync(files.Messages()[0], default);
        var hash = files.Read().LastSha256;
        File.WriteAllText(Path.Combine(files.Scope, "0002-" + new string('c', 64) + ".json.pending"), "{unfinished");
        var retained = files.Read();
        Assert.True(retained.HasUncommittedWrite); Assert.Equal(1, retained.Records); Assert.Equal(0, retained.Intents);
        Assert.Equal(hash, retained.LastSha256); Assert.Equal(TraversalCheckpointPhase.Baseline, retained.Last!.Phase);
        Assert.False(retained.CanReplay); Assert.False(retained.IsCompletionReceipt);
    }

    [Theory]
    [InlineData("execution")]
    [InlineData("sequence")]
    [InlineData("phase")]
    [InlineData("index")]
    [InlineData("command")]
    [InlineData("source")]
    [InlineData("url")]
    [InlineData("fingerprint")]
    public void WrongExecutionSequencePhaseCommandOrSourceIsRejectedBeforeInput(string scenario)
    {
        using var files = new Files(); var messages = files.Messages(); var value = messages[1];
        var cp = value.Checkpoint;
        value = scenario switch
        {
            "execution" => value with { ExecutionId = Guid.NewGuid() },
            "sequence" => value with { Sequence = 3 },
            "phase" => value with { Checkpoint = cp with { Phase = TraversalCheckpointPhase.Observed } },
            "index" => value with { Checkpoint = cp with { CommandIndex = 2 } },
            "command" => value with { Checkpoint = cp with { Instruction = new(ReaderCommand.Activate) } },
            "source" => value with { Checkpoint = cp with { Source = cp.Source with { Id = "unrelated" } } },
            "url" => value with { Checkpoint = cp with { Source = cp.Source with { Snapshot = cp.Source.Snapshot with { DocumentUrl = "https://private.example/?secret=never-retain" } } } },
            "fingerprint" => value with { Checkpoint = cp with { Source = cp.Source with { Fingerprint = "invalid" } } },
            _ => throw new InvalidOperationException()
        };
        var error = Assert.Throws<BrowserOperationException>(() => FixtureProgressProtocol.Validate(value, files.Start, messages[0]));
        Assert.Equal("WORKER_PROGRESS_INVALID", error.Code);
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("storage")]
    public async Task AuthorityOrStorageFailureCannotAcknowledgeTheNextCommand(string failure)
    {
        using var files = new Files(); var journal = await files.Begin(); var messages = files.Messages();
        if (failure == "storage")
        {
            var firstHash = TraversalArchive.Hash(FixtureProgressProtocol.Encode(new CommandProgressEntry("hsr-worker-command-journal-v1", files.Read().LastSha256, messages[0])));
            var blockedBytes = FixtureProgressProtocol.Encode(new CommandProgressEntry("hsr-worker-command-journal-v1", firstHash, messages[1]));
            File.WriteAllBytes(Path.Combine(files.Scope, $"0002-{TraversalArchive.Hash(blockedBytes)}.json"), []);
        }
        using var input = await Frames(messages.Take(2)); using var ack = new MemoryStream(); using var stop = new CancellationTokenSource();
        var calls = 0; var exchange = new FixtureProgressExchange(files.Start, journal, _ => Task.FromResult(++calls <= 2 || failure != "authority"));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(input, ack, stop));
        Assert.Equal(failure == "authority" ? "WORKER_PROGRESS_AUTHORITY_UNCONFIRMED" : "WORKER_PROGRESS_WRITE_UNCONFIRMED", error.Code);
        Assert.True(stop.IsCancellationRequested); Assert.Equal(3, calls);
        ack.Position = 0;
        var first = await FixtureProgressProtocol.ReadAsync<FixtureProgressAck>(ack, 4096, default);
        FixtureProgressProtocol.Validate(first, files.Native.Owner, 1);
        Assert.Null(await ReaderWorkerWire.ReadAsync(ack, 4096));
        if (failure == "authority") { Assert.Equal(1, files.Read().Records); Assert.False(files.Read().InputMayHaveOccurred); }
        else Assert.Throws<BrowserOperationException>(files.Read);
    }

    [Fact]
    public async Task RealChromiumWaitsForRetainedIntentBeforeSendingAnyKey()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.TraversalRecording);
        var initial = await browser.CaptureAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpoints = new List<TraversalCheckpoint>();
        var running = new TraversalRunner().RunAsync(browser, AuthoredKeyboardRoutes.Plans[0], checkpoint: async (cp, token) =>
        {
            checkpoints.Add(cp);
            if (cp.Phase == TraversalCheckpointPhase.Intent && cp.CommandIndex == 1)
            { entered.SetResult(); await release.Task.WaitAsync(token); }
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(running.IsCompleted);
            var held = await browser.CaptureAsync();
            Assert.Equal(initial.Nodes.Single(n => n.IsFocused).Id, held.Nodes.Single(n => n.IsFocused).Id);
            release.SetResult(); var result = await running;
            Assert.Equal(2, result.CompletedKeystrokes); Assert.Equal(TraversalReachability.Reached, result.Reachability);
            Assert.Equal(new[] { TraversalCheckpointPhase.Baseline, TraversalCheckpointPhase.Intent, TraversalCheckpointPhase.Observed,
                TraversalCheckpointPhase.Intent, TraversalCheckpointPhase.Observed }, checkpoints.Select(c => c.Phase));
            Assert.Contains(checkpoints[2].Attempt!.Events, e => e.Kind == ReaderEventKind.InputCompleted);
            Assert.All(checkpoints, cp => Assert.Null(cp.Source.Snapshot.DocumentUrl));
        }
        finally { release.TrySetResult(); await running; }
    }

    [Fact]
    public async Task RealChromiumIntentWriteFailureStopsBeforeTheFirstInput()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.TraversalRecording);
        var initial = await browser.CaptureAsync(); var phases = new List<TraversalCheckpointPhase>();
        await Assert.ThrowsAsync<IOException>(() => new TraversalRunner().RunAsync(browser, AuthoredKeyboardRoutes.Plans[0], checkpoint: (cp, _) =>
        { phases.Add(cp.Phase); return cp.Phase == TraversalCheckpointPhase.Intent ? Task.FromException(new IOException("storage unavailable")) : Task.CompletedTask; }));
        var after = await browser.CaptureAsync();
        Assert.Equal(initial.Nodes.Single(n => n.IsFocused).Id, after.Nodes.Single(n => n.IsFocused).Id);
        Assert.Equal(new[] { TraversalCheckpointPhase.Baseline, TraversalCheckpointPhase.Intent }, phases);
    }

    [Fact]
    public async Task ActualChromiumFramedWorkerAgreesWithRetainedPrefixAndCompleteOriginalBundle()
    {
        using var files = new Files(); var journal = await files.Begin();
        var input = new Pipe(); var progress = new Pipe(); using var output = new MemoryStream(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var authorizations = 0;
        var exchange = new FixtureProgressExchange(files.Start, journal, _ => Task.FromResult(++authorizations > 0));
        var reading = exchange.ReadAsync(progress.Reader.AsStream(), input.Writer.AsStream(), stop);
        var child = FixtureProgressWorker.RunAsync(input.Reader.AsStream(), output, progress.Writer.AsStream());
        await exchange.StartAsync(input.Writer.AsStream(), stop.Token);
        try
        {
            Assert.Equal(0, await child.WaitAsync(stop.Token));
            await progress.Writer.CompleteAsync(); await reading;
            var recording = KeyboardRecordingBundle.Decode(output.ToArray(), files.Job).Validate(files.Job);
            exchange.Match(recording.Receipt);
            var changedAttempt = recording.Receipt.Attempts[0] with { StartedMilliseconds = recording.Receipt.Attempts[0].StartedMilliseconds + 0.01 };
            var changedReceipt = recording.Receipt with { Attempts = recording.Receipt.Attempts.SetItem(0, changedAttempt) };
            Assert.Equal("WORKER_PROGRESS_INVALID", Assert.Throws<BrowserOperationException>(() => exchange.Match(changedReceipt)).Code);
            Assert.Equal(5, files.Read().Records); Assert.Equal(2, files.Read().Results); Assert.Equal(10, authorizations);
            Assert.Equal(2, recording.Receipt.CompletedKeystrokes); Assert.Equal(TraversalReachability.Reached, recording.Receipt.Reachability);
            Assert.False(files.Read().IsCompletionReceipt); Assert.False(files.Read().CanReplay);
            Assert.DoesNotContain("\"Token\"", JsonSerializer.Serialize(files.Read()));
            Assert.DoesNotContain("\"LeaseToken\"", JsonSerializer.Serialize(files.Start));
        }
        finally
        {
            await input.Writer.CompleteAsync(); await progress.Writer.CompleteAsync();
            await child; try { await reading; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("execution")]
    public async Task RealChromiumWrongIntentAcknowledgementCannotProduceAnObservedResultOrBundle(string mismatch)
    {
        using var files = new Files(); var input = new Pipe(); var progress = new Pipe();
        using var output = new MemoryStream(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var child = FixtureProgressWorker.RunAsync(input.Reader.AsStream(), output, progress.Writer.AsStream());
        try
        {
            await ReaderWorkerWire.WriteAsync(input.Writer.AsStream(), FixtureProgressProtocol.Encode(files.Start), 4096, timeout.Token);
            var baseline = await FixtureProgressProtocol.ReadAsync<FixtureProgressMessage>(progress.Reader.AsStream(), FixtureProgressProtocol.MaximumFrameBytes, timeout.Token);
            FixtureProgressProtocol.Validate(baseline, files.Start, null);
            await ReaderWorkerWire.WriteAsync(input.Writer.AsStream(), FixtureProgressProtocol.Encode(new FixtureProgressAck(FixtureProgressProtocol.Schema, files.Native.Owner, 1)), 4096, timeout.Token);
            var intent = await FixtureProgressProtocol.ReadAsync<FixtureProgressMessage>(progress.Reader.AsStream(), FixtureProgressProtocol.MaximumFrameBytes, timeout.Token);
            FixtureProgressProtocol.Validate(intent, files.Start, baseline);
            Assert.Equal(TraversalCheckpointPhase.Intent, intent.Checkpoint.Phase);
            var bad = new FixtureProgressAck(FixtureProgressProtocol.Schema,
                mismatch == "execution" ? Guid.NewGuid() : files.Native.Owner, mismatch == "sequence" ? 1 : 2);
            await ReaderWorkerWire.WriteAsync(input.Writer.AsStream(), FixtureProgressProtocol.Encode(bad), 4096, timeout.Token);
            Assert.Equal(3, await child.WaitAsync(timeout.Token)); Assert.Equal(0, output.Length);
            await progress.Writer.CompleteAsync();
            Assert.Null(await ReaderWorkerWire.ReadAsync(progress.Reader.AsStream(), FixtureProgressProtocol.MaximumFrameBytes, timeout.Token));
        }
        finally { await input.Writer.CompleteAsync(); await progress.Writer.CompleteAsync(); await child; }
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("execution")]
    [InlineData("route")]
    [InlineData("recording")]
    [InlineData("unknown")]
    public async Task InvalidStartProducesNeitherProgressNorRecording(string invalid)
    {
        using var files = new Files(); var start = files.Start;
        start = invalid switch
        {
            "schema" => start with { Schema = "unknown" },
            "execution" => start with { ExecutionId = Guid.Empty },
            "route" => start with { Job = start.Job with { TraversalRoute = 0 } },
            "recording" => start with { Job = start.Job with { RecordObservation = false } },
            _ => start
        };
        var bytes = FixtureProgressProtocol.Encode(start);
        if (invalid == "unknown") bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes)[..^1] + ",\"Untrusted\":true}");
        using var input = new MemoryStream(); using var output = new MemoryStream(); using var progress = new MemoryStream();
        await ReaderWorkerWire.WriteAsync(input, bytes, 4096); input.Position = 0;
        Assert.Equal(3, await FixtureProgressWorker.RunAsync(input, output, progress));
        Assert.Equal(0, output.Length); Assert.Equal(0, progress.Length);
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("source")]
    [InlineData("result")]
    [InlineData("time")]
    public async Task InconsistentObservedAttemptIsRejectedBeforeRetentionOrAcknowledgement(string invalid)
    {
        using var files = new Files(); var journal = await files.Begin(); var messages = files.Messages();
        var cp = messages[2].Checkpoint; var attempt = cp.Attempt!;
        attempt = invalid switch
        {
            "attempt" => attempt with { Id = "other-attempt" },
            "source" => attempt with { SourceObservationId = "other-source" },
            "result" => attempt with { ResultObservationId = "other-result" },
            "time" => attempt with { FinishedMilliseconds = 0 },
            _ => throw new InvalidOperationException()
        };
        messages[2] = messages[2] with { Checkpoint = cp with { Attempt = attempt } };
        using var input = await Frames(messages); using var ack = new MemoryStream(); using var stop = new CancellationTokenSource();
        var exchange = new FixtureProgressExchange(files.Start, journal, _ => Task.FromResult(true));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(input, ack, stop));
        Assert.Equal("WORKER_PROGRESS_INVALID", error.Code); Assert.True(stop.IsCancellationRequested);
        Assert.Equal(2, files.Read().Records); Assert.Equal(0, files.Read().Results);
        ack.Position = 0;
        for (var sequence = 1; sequence <= 2; sequence++)
            FixtureProgressProtocol.Validate(await FixtureProgressProtocol.ReadAsync<FixtureProgressAck>(ack, 4096, default), files.Native.Owner, sequence);
        Assert.Null(await ReaderWorkerWire.ReadAsync(ack, 4096));
    }
}
