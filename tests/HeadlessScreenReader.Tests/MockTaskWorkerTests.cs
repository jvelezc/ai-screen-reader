using System.IO.Compression;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed partial class MockTaskWorkerTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "artifacts", "mock-worker-tests");
    private static MockTaskStart Start(string source = "checkout-working") => new(MockTaskControl.Schema,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, MockTaskJob.CurrentProfile, MockTaskSource.FromId(source)), Guid.NewGuid(), Guid.NewGuid());
    private static string Folder() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

    [Fact]
    public void ClosedMockSourcesMatchExactBrowserBytesAndDoNotAcquirePublicOrShopIdentity()
    {
        foreach (var fixture in MockSiteTasks.Fixtures)
        {
            var actual = MockSiteTasks.Describe(fixture); var source = MockTaskSource.FromId(actual.Id);
            source.Validate(); Start(actual.Id).Validate();
            Assert.Equal(actual.SourceSha256, source.Sha256);
            Assert.Equal(actual.Heading, source.ExpectedHeading()); Assert.Equal(actual.GoalName, source.ExpectedGoal());
            var encoded = JsonSerializer.SerializeToUtf8Bytes(Start(actual.Id), ReaderWorkerWire.Json);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FixtureJob>(encoded, ReaderWorkerWire.Json));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PublicWebsiteJob>(encoded, ReaderWorkerWire.Json));
        }
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("synthetic-shop")]
    [InlineData("CHECKOUT-WORKING")]
    [InlineData("")]
    public void MockSourceCannotAcceptAnArbitraryWebsiteOrAnotherFixture(string id) =>
        Assert.Equal("MOCK_TASK_SOURCE_INVALID", Assert.Throws<BrowserOperationException>(() => MockTaskSource.FromId(id)).Code);

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation-zero")]
    [InlineData("generation-two")]
    [InlineData("profile")]
    [InlineData("source")]
    [InlineData("hash")]
    public void MockJobRejectsMissingScopeChangedSourceAndReplayGeneration(string mutation)
    {
        var job = Start().Job;
        var changed = mutation switch
        {
            "organization" => job with { OrganizationId = Guid.Empty },
            "scan" => job with { ScanId = Guid.Empty },
            "job" => job with { JobId = Guid.Empty },
            "generation-zero" => job with { Generation = 0 },
            "generation-two" => job with { Generation = 2 },
            "profile" => job with { Profile = FixtureObservation.Profile },
            "source" => job with { Source = null! },
            _ => job with { Source = job.Source with { Sha256 = new string('a', 64) } }
        };
        job.Validate(); Assert.Throws<BrowserOperationException>(changed.Validate);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("worker")]
    [InlineData("execution")]
    [InlineData("organization")]
    [InlineData("sequence-zero")]
    [InlineData("sequence-next")]
    [InlineData("trial")]
    [InlineData("action")]
    public void MockControlCannotAcknowledgeForeignOrOutOfOrderIntent(string mutation)
    {
        var start = Start(); var intent = new MockTaskIntent(MockTaskControl.Schema, start, 1, MockTaskControl.Boundaries[0]);
        MockTaskControl.Validate(intent, start, 1);
        var changed = mutation switch
        {
            "schema" => intent with { Schema = "hsr-fixture-progress-v1" },
            "worker" => intent with { Start = start with { WorkerId = Guid.NewGuid() } },
            "execution" => intent with { Start = start with { ExecutionId = Guid.NewGuid() } },
            "organization" => intent with { Start = start with { Job = start.Job with { OrganizationId = Guid.NewGuid() } } },
            "sequence-zero" => intent with { Sequence = 0 },
            "sequence-next" => intent with { Sequence = 2 },
            "trial" => intent with { Boundary = new(2, "Open") },
            _ => intent with { Boundary = new(1, "Pointer") }
        };
        Assert.Equal("MOCK_TASK_INTENT_INVALID", Assert.Throws<BrowserOperationException>(() => MockTaskControl.Validate(changed, start, 1)).Code);
    }

    private static async Task<MemoryStream> Intents(MockTaskStart start, int count)
    {
        var stream = new MemoryStream();
        for (var i = 0; i < count; i++)
            await ReaderWorkerWire.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(
                new MockTaskIntent(MockTaskControl.Schema, start, i + 1, MockTaskControl.Boundaries[i]), ReaderWorkerWire.Json), MockTaskControl.MaximumFrameBytes);
        stream.Position = 0; return stream;
    }

    [Fact]
    public async Task ParentPersistsEachUncertainIntentAndRechecksAuthorityBeforeAcknowledging()
    {
        var start = Start(); var folder = Folder(); var checks = 0;
        using var stop = new CancellationTokenSource(); using var input = await Intents(start, 13); using var acks = new MemoryStream();
        var exchange = new MockTaskProgressExchange(start, folder, _ =>
        {
            checks++;
            var saved = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Length : 0;
            Assert.Equal(checks / 2, saved); return Task.FromResult(true);
        });
        await exchange.ReadAsync(input, acks, stop); exchange.RequireComplete();
        Assert.Equal(26, checks); Assert.Equal(13, exchange.AuthorizedBoundaries); Assert.False(stop.IsCancellationRequested);
        var records = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p => JsonSerializer.Deserialize<MockTaskGrantIntent>(File.ReadAllBytes(p), ReaderWorkerWire.Json)!).ToArray();
        Assert.Equal(13, records.Length); Assert.Equal(7, records.Count(r => r.InputMayHaveOccurred));
        acks.Position = 0;
        foreach (var saved in records)
        {
            Assert.NotEqual(default, saved.PreparedAtUtc);
            var ack = JsonSerializer.Deserialize<MockTaskAck>((await ReaderWorkerWire.ReadAsync(acks, 4096))!, ReaderWorkerWire.Json)!;
            Assert.Equal(start.ExecutionId, ack.ExecutionId); Assert.Equal(saved.Intent.Sequence, ack.Sequence);
            Assert.Equal(saved.IntentSha256, ack.IntentSha256);
            Assert.Equal(TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(saved.Intent, ReaderWorkerWire.Json)), ack.IntentSha256);
        }
        Assert.Null(await ReaderWorkerWire.ReadAsync(acks, 4096));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 1, 1)]
    [InlineData(4, 2, 1)]
    public async Task AuthorityLossNeverAcknowledgesTheDeniedBoundary(int rejectCheck, int retained, int acknowledged)
    {
        var start = Start(); var folder = Folder(); var checks = 0;
        using var stop = new CancellationTokenSource(); using var input = await Intents(start, 13); using var acks = new MemoryStream();
        var exchange = new MockTaskProgressExchange(start, folder, _ => Task.FromResult(++checks != rejectCheck));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(input, acks, stop));
        Assert.Equal("MOCK_TASK_AUTHORITY_UNCONFIRMED", error.Code); Assert.True(stop.IsCancellationRequested);
        Assert.Equal(retained, Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Length : 0);
        acks.Position = 0; var count = 0;
        while (await ReaderWorkerWire.ReadAsync(acks, 4096) is not null) count++;
        Assert.Equal(acknowledged, count); Assert.Throws<BrowserOperationException>(exchange.RequireComplete);
    }

    [Fact]
    public async Task FailedIntentPersistenceCannotReleaseAnyAcknowledgement()
    {
        var start = Start(); var folder = Folder(); Directory.CreateDirectory(Root); await File.WriteAllTextAsync(folder, "occupied");
        using var stop = new CancellationTokenSource(); using var input = await Intents(start, 1); using var acks = new MemoryStream();
        var exchange = new MockTaskProgressExchange(start, folder, _ => Task.FromResult(true));
        Assert.Equal("MOCK_TASK_INTENT_WRITE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => exchange.ReadAsync(input, acks, stop))).Code);
        Assert.Equal(0, acks.Length); Assert.True(stop.IsCancellationRequested);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public async Task EofAfterAPartialSequenceCannotBecomeACompletedCollection(int count)
    {
        var start = Start(); using var stop = new CancellationTokenSource(); using var input = await Intents(start, count); using var acks = new MemoryStream();
        var exchange = new MockTaskProgressExchange(start, Folder(), _ => Task.FromResult(true));
        await exchange.ReadAsync(input, acks, stop);
        Assert.Equal(count, exchange.AuthorizedBoundaries);
        Assert.Equal("MOCK_TASK_AUTHORIZATION_INCOMPLETE", Assert.Throws<BrowserOperationException>(exchange.RequireComplete).Code);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("execution")]
    [InlineData("sequence")]
    [InlineData("digest")]
    [InlineData("eof")]
    public async Task ChildRejectsMissingForeignOrChangedAckWithoutPublishingABundle(string mutation)
    {
        var start = Start(); using var input = new MemoryStream(); using var output = new MemoryStream(); using var progress = new MemoryStream();
        await ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(start, ReaderWorkerWire.Json), 4096);
        var first = new MockTaskIntent(MockTaskControl.Schema, start, 1, MockTaskControl.Boundaries[0]);
        var good = new MockTaskAck(MockTaskControl.Schema, start.ExecutionId, 1, TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(first, ReaderWorkerWire.Json)));
        var ack = mutation switch
        {
            "schema" => good with { Schema = "wrong" },
            "execution" => good with { ExecutionId = Guid.NewGuid() },
            "sequence" => good with { Sequence = 2 },
            "digest" => good with { IntentSha256 = new string('a', 64) },
            _ => good
        };
        if (mutation != "eof") await ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(ack, ReaderWorkerWire.Json), 4096);
        input.Position = 0;
        Assert.Equal(3, await MockTaskChildWorker.RunAsync(input, output, progress)); Assert.Equal(0, output.Length);
        progress.Position = 0;
        var actual = JsonSerializer.Deserialize<MockTaskIntent>((await ReaderWorkerWire.ReadAsync(progress, 4096))!, ReaderWorkerWire.Json);
        Assert.Equal(first, actual); Assert.Null(await ReaderWorkerWire.ReadAsync(progress, 4096));
    }

    private static async Task<(MockTaskStart Start, byte[] Output, string Journal)> Child(string source, string? deny = null)
    {
        var start = Start(source); var journal = Folder(); var toChild = new Pipe(); var fromChild = new Pipe();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40)); using var output = new MemoryStream();
        var checks = 0;
        var exchange = new MockTaskProgressExchange(start, journal, _ =>
            Task.FromResult(deny is null || MockTaskControl.Boundaries[(checks++ / 2)].Action != deny));
        var parent = exchange.ReadAsync(fromChild.Reader.AsStream(), toChild.Writer.AsStream(), stop);
        var child = MockTaskChildWorker.RunAsync(toChild.Reader.AsStream(), output, fromChild.Writer.AsStream(), stop.Token);
        await exchange.StartAsync(toChild.Writer.AsStream(), stop.Token);
        var exit = await child; await fromChild.Writer.CompleteAsync();
        if (deny is null) { await parent; Assert.Equal(0, exit); exchange.RequireComplete(); }
        else { Assert.Equal("MOCK_TASK_AUTHORITY_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => parent)).Code); Assert.Equal(3, exit); }
        await toChild.Writer.CompleteAsync(); await toChild.Reader.CompleteAsync(); await fromChild.Reader.CompleteAsync();
        return (start, output.ToArray(), journal);
    }

    [Theory]
    [InlineData("checkout-working")]
    [InlineData("checkout-keyboard-blocked")]
    [InlineData("control-named")]
    [InlineData("control-unnamed")]
    public async Task RealMockChildReturnsBoundOriginalsGraphsAndIndependentAttemptsAfterThirteenAuthorityGates(string source)
    {
        var result = await Child(source); var decoded = MockTaskCollectionBundle.Decode(result.Output, result.Start);
        Assert.Equal(result.Start, decoded.Receipt.Start); Assert.Equal(13, decoded.Receipt.AuthorizedBoundaries);
        Assert.Equal(TraversalArchive.Hash(decoded.Originals), decoded.Receipt.ArchiveSha256);
        Assert.Equal(13, Directory.GetFiles(result.Journal, "*.json", SearchOption.AllDirectories).Length);
        using var stream = new MemoryStream(decoded.Originals); using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("route-1.svg")); Assert.NotNull(zip.GetEntry("route-2.html")); Assert.NotNull(zip.GetEntry("route-3.json"));
        Assert.NotNull(zip.GetEntry("pointer-media/original.webm")); Assert.NotNull(zip.GetEntry("fixture-source.txt"));
        Assert.Throws<BrowserOperationException>(() => MockTaskCollectionBundle.Decode(result.Output, result.Start with { ExecutionId = Guid.NewGuid() }));
    }

    [Theory]
    [InlineData("Tab", 1, 0)]
    [InlineData("Enter", 2, 1)]
    [InlineData("Pointer", 11, 6)]
    public async Task RealMockChildLosesAuthorityBeforeKeyboardOrPointerAndNeverPublishesPartialOriginals(string action, int expectedIntents, int possibleInputs)
    {
        var result = await Child("checkout-working", action);
        Assert.Empty(result.Output); Assert.Equal(expectedIntents, Directory.GetFiles(result.Journal, "*.json", SearchOption.AllDirectories).Length);
        Assert.Equal(possibleInputs, Directory.GetFiles(result.Journal, "*.json", SearchOption.AllDirectories)
            .Count(p => JsonSerializer.Deserialize<MockTaskGrantIntent>(File.ReadAllBytes(p), ReaderWorkerWire.Json)!.InputMayHaveOccurred));
    }

    [Fact]
    public async Task ParentCanValidateActualOriginalPackageWithoutConvertingUnknownGoalsIntoABarrier()
    {
        var result = await Child("checkout-keyboard-blocked");
        var bundle = MockTaskCollectionBundle.Decode(result.Output, result.Start);
        bundle.Validate(result.Start);
        var files = Unzip(bundle.Originals);
        Assert.Equal(result.Start, JsonSerializer.Deserialize<MockTaskStart>(files["mock-task-execution.json"], ReaderWorkerWire.Json));
        using var manifest = JsonDocument.Parse(files["mock-task-manifest.json"]);
        Assert.Equal(bundle.Receipt.ManifestSha256, TraversalArchive.Hash(files["mock-task-manifest.json"]));
        Assert.Equal("RepeatedKeyboardGoalNotObservedWithPointerGoalObserved", manifest.RootElement.GetProperty("Observation").GetString());
    }
}
