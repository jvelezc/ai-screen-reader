using System.IO.Pipelines;
using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchTaskChildTests
{
    [Theory]
    [InlineData("search-working")]
    [InlineData("search-keyboard-blocked")]
    [Trait("Category", "Browser")]
    public async Task ActualSearchChildUsesTheParentWireAndRetainsFiveVerifiedRecordings(string source)
    {
        var result = await RunChild(source, false);
        Assert.Equal(0, result.Exit);
        Assert.Equal(85, result.Grants.PreparedResponses);
        var bundle = SyntheticSearchTaskBundle.Decode(result.Output, result.Start);
        var sources = result.Grants.ReadRetainedSources();
        bundle.MatchRetainedSources(sources);
        Assert.Equal(5, sources.Files.Select(file => file.Trial).Distinct().Count());
        Assert.False(sources.HasUncommittedFiles);
        Assert.False(sources.IsCompletedRecording);
        Assert.False(result.Grants.CanAdvance);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task ActualSearchChildStopsMidQueryAndKeepsOnlyThePermittedParentOriginalSubset()
    {
        var result = await RunChild("search-working", true);
        Assert.Equal(3, result.Exit);
        Assert.Empty(result.Output);
        Assert.Equal(9, result.Grants.PreparedResponses);
        var retained = result.Grants.ReadRetainedSources();
        Assert.NotEmpty(retained.Files);
        Assert.All(retained.Files, file => Assert.Equal(1, file.Trial));
        Assert.False(retained.IsCompletedRecording);
        Assert.False(retained.CanReplay);
        Assert.False(result.Grants.CanAdvance);
        Assert.Single(Directory.GetFiles(result.Grants.DirectoryPath, "intent-*.json")
            .Select(path => JsonSerializer.Deserialize<SyntheticSearchTaskGrant>(File.ReadAllBytes(path), ReaderWorkerWire.Json)!),
            grant => grant.Intent.Boundary.Action == "Open");
        var denied = JsonSerializer.Deserialize<SyntheticSearchTaskAck>(await File.ReadAllBytesAsync(Path.Combine(result.Grants.DirectoryPath, "response-0009.json")), ReaderWorkerWire.Json)!;
        Assert.Equal(SyntheticSearchInputDecision.Revoked, denied.Decision);
        Assert.False(File.Exists(Path.Combine(result.Grants.DirectoryPath, "intent-0010.json")));
        Assert.Equal("SEARCH_TASK_REPLAY_OR_FENCE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            SyntheticSearchTaskGrants.BeginAsync(Path.GetDirectoryName(result.Grants.DirectoryPath)!, result.Start with { WorkerId = Guid.NewGuid() }))).Code);
    }

    [Theory]
    [InlineData("execution")]
    [InlineData("sequence")]
    [InlineData("hash")]
    [InlineData("denial")]
    public async Task SearchChildCannotAdvanceFromAForeignOrDeniedAcknowledgement(string change)
    {
        var start = Start("search-working");
        var intent = new SyntheticSearchTaskIntent(SyntheticSearchTaskControl.Schema, start, 1, SyntheticSearchTaskControl.Boundaries[0], null);
        var ack = new SyntheticSearchTaskAck(SyntheticSearchTaskControl.Schema, start.ExecutionId, 1,
            TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(intent, ReaderWorkerWire.Json)), SyntheticSearchInputDecision.Granted);
        ack = change switch
        {
            "execution" => ack with { ExecutionId = Guid.NewGuid() },
            "sequence" => ack with { Sequence = 2 },
            "hash" => ack with { IntentSha256 = new('f', 64) },
            _ => ack with { Decision = SyntheticSearchInputDecision.Revoked }
        };
        using var input = new MemoryStream(); using var progress = new MemoryStream(); using var stop = new CancellationTokenSource();
        await ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(ack, ReaderWorkerWire.Json), SyntheticSearchTaskControl.MaximumFrameBytes);
        input.Position = 0;
        var control = new SyntheticSearchTaskChildControl(start, input, progress, stop);
        Assert.Equal("SEARCH_TASK_GRANT_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => control.BoundaryAsync(1, "Open", stop.Token))).Code);
        Assert.Equal(0, control.PreparedResponses);
        Assert.True(stop.IsCancellationRequested);
        progress.Position = 0;
        Assert.NotNull(await ReaderWorkerWire.ReadAsync(progress, SyntheticSearchTaskControl.MaximumFrameBytes));
        Assert.Null(await ReaderWorkerWire.ReadAsync(progress, SyntheticSearchTaskControl.MaximumFrameBytes));
    }

    [Fact]
    public async Task SearchChildRejectsAForeignProfileBeforeProducingProgressOrAnOutput()
    {
        var start = Start("search-working");
        using var input = new MemoryStream(); using var output = new MemoryStream(); using var progress = new MemoryStream();
        await ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(start with { Job = start.Job with { Profile = MockTaskJob.CurrentProfile } }, ReaderWorkerWire.Json),
            SyntheticSearchTaskControl.MaximumFrameBytes);
        input.Position = 0;
        Assert.Equal(3, await SyntheticSearchTaskChildWorker.RunAsync(input, output, progress));
        Assert.Equal(0, progress.Length); Assert.Equal(0, output.Length);
    }

    private static async Task<(SyntheticSearchTaskStart Start, SyntheticSearchTaskGrants Grants, byte[] Output, int Exit)> RunChild(string source, bool deny)
    {
        var start = Start(source);
        var root = Path.GetFullPath(Path.Combine("artifacts", "search-task-child", Guid.NewGuid().ToString("N")));
        var grants = await SyntheticSearchTaskGrants.BeginAsync(root, start);
        var exchange = new SyntheticSearchTaskProgressExchange(start, grants,
            _ => Task.FromResult(!deny || !File.Exists(Path.Combine(grants.DirectoryPath, "intent-0009.json"))));
        var toChild = new Pipe(); var fromChild = new Pipe();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(120)); using var output = new MemoryStream();
        var parent = exchange.ReadAsync(fromChild.Reader.AsStream(), toChild.Writer.AsStream(), stop);
        var child = SyntheticSearchTaskChildWorker.RunAsync(toChild.Reader.AsStream(), output, fromChild.Writer.AsStream(), stop.Token);
        await exchange.StartAsync(toChild.Writer.AsStream(), stop.Token);
        var exit = await child;
        await fromChild.Writer.CompleteAsync();
        if (!deny) { await parent; exchange.RequireComplete(); }
        else Assert.Equal("SEARCH_TASK_AUTHORITY_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => parent)).Code);
        await toChild.Writer.CompleteAsync(); await toChild.Reader.CompleteAsync(); await fromChild.Reader.CompleteAsync();
        return (start, grants, output.ToArray(), exit);
    }

    private static SyntheticSearchTaskStart Start(string source) => new(SyntheticSearchTaskControl.Schema,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId(source)), Guid.NewGuid(), Guid.NewGuid());
}
