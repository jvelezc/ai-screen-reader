using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchTaskProgressTests
{
    [Fact]
    public async Task SearchParentExchangeBindsAllEightyFiveResponsesAndSeparateOriginalAcknowledgements()
    {
        var start = Start(); var journal = await Journal(start);
        var exchange = new SyntheticSearchTaskProgressExchange(start, journal, Granted);
        using var input = new MemoryStream(); using var responses = new MemoryStream(); using var stop = new CancellationTokenSource();
        var sources = 0;
        for (var index = 0; index < SyntheticSearchTaskControl.Boundaries.Length; index++)
        {
            var intent = Intent(start, index);
            await Write(input, intent);
            if (intent.Boundary.Action == "Open") await Write(input, Batch(start, intent.Boundary.Trial, ++sources));
        }
        input.Position = 0;
        await exchange.StartAsync(responses, stop.Token);
        await exchange.ReadAsync(input, responses, stop);
        exchange.RequireComplete();
        Assert.False(stop.IsCancellationRequested);
        responses.Position = 0;
        Assert.Equal(start, await Read<SyntheticSearchTaskStart>(responses));
        sources = 0;
        for (var index = 0; index < 85; index++)
        {
            var intent = Intent(start, index);
            var response = await Read<SyntheticSearchTaskAck>(responses);
            Assert.Equal(index + 1, response.Sequence);
            Assert.Equal(start.ExecutionId, response.ExecutionId);
            Assert.Equal(SyntheticSearchInputDecision.Granted, response.Decision);
            Assert.Equal(TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(intent, ReaderWorkerWire.Json)), response.IntentSha256);
            if (intent.Boundary.Action == "Open")
            {
                var ack = await Read<SyntheticSearchSourceAck>(responses);
                var batch = Batch(start, intent.Boundary.Trial, ++sources);
                Assert.Equal(intent.Boundary.Trial, ack.Trial);
                Assert.Equal(sources, ack.Sequence);
                Assert.Equal(TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(batch, ReaderWorkerWire.Json)), ack.BatchSha256);
            }
        }
        Assert.Null(await ReaderWorkerWire.ReadAsync(responses, SyntheticSearchSourceProtocol.MaximumFrameBytes));
        var retained = journal.ReadRetainedSources();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, retained.Files.Select(file => file.Trial));
        Assert.False(retained.IsCompletedRecording);
    }

    [Fact]
    public async Task SearchParentCannotGrantNativeInputBeforeItsBoundOriginalsAreDurable()
    {
        var start = Start(); var journal = await Journal(start);
        var exchange = new SyntheticSearchTaskProgressExchange(start, journal, Granted);
        using var input = new MemoryStream(); using var responses = new MemoryStream(); using var stop = new CancellationTokenSource();
        await Write(input, Intent(start, 0)); await Write(input, Intent(start, 1)); input.Position = 0;
        Assert.Equal("SEARCH_TASK_ORIGINALS_REQUIRED_BEFORE_INPUT", (await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(input, responses, stop))).Code);
        Assert.True(stop.IsCancellationRequested);
        Assert.Equal(1, journal.PreparedResponses);
        Assert.False(journal.CanAdvance);
        Assert.False(File.Exists(Path.Combine(journal.DirectoryPath, "intent-0002.json")));
    }

    [Fact]
    public async Task SearchParentRejectsAChangedComputedWireSchemaInsteadOfRewritingTheIntent()
    {
        var start = Start(); var journal = await Journal(start);
        var exchange = new SyntheticSearchTaskProgressExchange(start, journal, Granted);
        using var input = new MemoryStream(); using var responses = new MemoryStream(); using var stop = new CancellationTokenSource();
        await Write(input, Intent(start, 0)); await Write(input, Batch(start, 1, 1));
        var changed = JsonSerializer.Serialize(Intent(start, 1), ReaderWorkerWire.Json)
            .Replace("hsr-synthetic-search-input-intent-v1", "hsr-synthetic-search-input-intent-v0", StringComparison.Ordinal);
        await ReaderWorkerWire.WriteAsync(input, Encoding.UTF8.GetBytes(changed), SyntheticSearchSourceProtocol.MaximumFrameBytes);
        input.Position = 0;
        Assert.Equal("SEARCH_TASK_PROGRESS_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(input, responses, stop))).Code);
        Assert.Equal(1, journal.PreparedResponses);
        Assert.False(File.Exists(Path.Combine(journal.DirectoryPath, "intent-0002.json")));
        Assert.Single(journal.ReadRetainedSources().Files);
    }

    [Fact]
    public async Task SearchParentSendsOnlyTheDurableDenialAndNeverAcknowledgesTheFollowingInput()
    {
        var start = Start(); var journal = await Journal(start); var calls = 0;
        var exchange = new SyntheticSearchTaskProgressExchange(start, journal, _ => Task.FromResult(++calls <= 4));
        using var input = new MemoryStream(); using var responses = new MemoryStream(); using var stop = new CancellationTokenSource();
        await Write(input, Intent(start, 0)); await Write(input, Batch(start, 1, 1));
        await Write(input, Intent(start, 1)); await Write(input, Intent(start, 2)); input.Position = 0;
        Assert.Equal("SEARCH_TASK_AUTHORITY_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(input, responses, stop))).Code);
        responses.Position = 0;
        Assert.Equal(SyntheticSearchInputDecision.Granted, (await Read<SyntheticSearchTaskAck>(responses)).Decision);
        await Read<SyntheticSearchSourceAck>(responses);
        Assert.Equal(SyntheticSearchInputDecision.Revoked, (await Read<SyntheticSearchTaskAck>(responses)).Decision);
        Assert.Null(await ReaderWorkerWire.ReadAsync(responses, SyntheticSearchSourceProtocol.MaximumFrameBytes));
        Assert.Equal(2, journal.PreparedResponses);
        Assert.False(File.Exists(Path.Combine(journal.DirectoryPath, "intent-0003.json")));
        Assert.Single(journal.ReadRetainedSources().Files);
        Assert.True(stop.IsCancellationRequested);
    }

    [Fact]
    public async Task SearchParentCannotSubstituteAnotherWorkerExecutionForItsDurableJournal()
    {
        var start = Start(); var journal = await Journal(start);
        Assert.Equal("SEARCH_TASK_START_INVALID", Assert.Throws<BrowserOperationException>(() =>
            new SyntheticSearchTaskProgressExchange(start with { ExecutionId = Guid.NewGuid() }, journal, Granted)).Code);
        Assert.Equal(0, journal.PreparedResponses);
    }

    private static Task<bool> Granted(CancellationToken token) => Task.FromResult(true);
    private static Task Write<T>(Stream stream, T value) => ReaderWorkerWire.WriteAsync(stream,
        JsonSerializer.SerializeToUtf8Bytes(value, ReaderWorkerWire.Json), SyntheticSearchSourceProtocol.MaximumFrameBytes);
    private static async Task<T> Read<T>(Stream stream) => JsonSerializer.Deserialize<T>((await ReaderWorkerWire.ReadAsync(stream,
        SyntheticSearchSourceProtocol.MaximumFrameBytes))!, ReaderWorkerWire.Json)!;
    private static Task<SyntheticSearchTaskGrants> Journal(SyntheticSearchTaskStart start) => SyntheticSearchTaskGrants.BeginAsync(
        Path.GetFullPath(Path.Combine("artifacts", "search-task-progress", Guid.NewGuid().ToString("N"))), start);
    private static SyntheticSearchTaskStart Start() => new(SyntheticSearchTaskControl.Schema,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId("search-working")), Guid.NewGuid(), Guid.NewGuid());
    private static SyntheticSearchTaskIntent Intent(SyntheticSearchTaskStart start, int index)
    {
        var boundary = SyntheticSearchTaskControl.Boundaries[index];
        SyntheticSearchInputIntent? input = boundary.Action == "Native" ? new(boundary.NativeSequence, boundary.Kind!.Value, boundary.NativeKey,
            boundary.CharacterIndex, start.Job.Source.Sha256, "document-" + boundary.Trial, 1,
            boundary.Kind is SyntheticSearchInputKind.Character or SyntheticSearchInputKind.MouseFocus || boundary.NativeSequence == 15 && boundary.NativeKey == "Tab"
                || boundary.NativeSequence == 15 && boundary.Trial <= 2 ? "search" : "control") : null;
        return new(SyntheticSearchTaskControl.Schema, start, index + 1, boundary, input);
    }
    private static SyntheticSearchSourceBatch Batch(SyntheticSearchTaskStart start, int trial, int sequence)
    {
        byte[] bytes = [0xff, 0xd8, 0xff, 42];
        return new(SyntheticSearchSourceProtocol.Schema, start.ExecutionId, trial, sequence, "document-" + trial, 1,
            [new(1, "source-0001.jpg", 123456, TraversalArchive.Hash(bytes), bytes)]);
    }
}
