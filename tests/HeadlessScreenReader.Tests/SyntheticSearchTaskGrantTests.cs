using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchTaskGrantTests
{
    [Fact]
    public async Task DurableSearchGrantsRetainEveryIntentBeforeReturningItsBoundResponse()
    {
        var start = Start();
        var journal = await SyntheticSearchTaskGrants.BeginAsync(NewRoot(), start);
        var count = 0;
        foreach (var intent in Intents(start))
        {
            var checks = 0;
            var intentPath = Path.Combine(journal.DirectoryPath, $"intent-{intent.Sequence:D4}.json");
            var ack = await journal.PrepareAsync(intent, _ =>
            {
                checks++;
                Assert.Equal(checks == 2, File.Exists(intentPath));
                return Task.FromResult(true);
            });
            Assert.Equal(2, checks);
            Assert.Equal(SyntheticSearchInputDecision.Granted, ack.Decision);
            Assert.Equal(start.ExecutionId, ack.ExecutionId);
            Assert.Equal(++count, ack.Sequence);
            Assert.Equal(TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(intent, ReaderWorkerWire.Json)), ack.IntentSha256);
            var original = await ReadAsync<SyntheticSearchTaskGrant>(intentPath);
            Assert.Equal(intent, original.Intent);
            Assert.Equal(ack.IntentSha256, original.IntentSha256);
            Assert.Equal(intent.Input is not null, original.InputMayHaveOccurred);
            Assert.Equal(SyntheticSearchInputDecision.Granted, original.DecisionBeforeWrite);
            Assert.NotEqual(default, original.PreparedAtUtc);
            Assert.Equal(ack, await ReadAsync<SyntheticSearchTaskAck>(Path.Combine(journal.DirectoryPath, $"response-{intent.Sequence:D4}.json")));
        }
        Assert.Equal(85, journal.PreparedResponses);
        Assert.False(journal.CanAdvance);
        journal.RequireComplete();
        Assert.Equal(171, Directory.GetFiles(journal.DirectoryPath).Length);
        Assert.Equal(start, await ReadAsync<SyntheticSearchTaskStart>(Path.Combine(journal.DirectoryPath, "start.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchAuthorityLossBeforeOrAfterTheIntentWriteStopsAllLaterGrants(bool afterWrite)
    {
        var start = Start();
        var journal = await SyntheticSearchTaskGrants.BeginAsync(NewRoot(), start);
        var intents = Intents(start);
        await journal.PrepareAsync(intents[0], Granted);
        var calls = 0;
        var ack = await journal.PrepareAsync(intents[1], _ => Task.FromResult(afterWrite && ++calls == 1));
        Assert.Equal(SyntheticSearchInputDecision.Revoked, ack.Decision);
        var originalPath = Path.Combine(journal.DirectoryPath, "intent-0002.json");
        var originalBytes = await File.ReadAllBytesAsync(originalPath);
        var original = JsonSerializer.Deserialize<SyntheticSearchTaskGrant>(originalBytes, ReaderWorkerWire.Json)!;
        Assert.Equal(afterWrite, original.InputMayHaveOccurred);
        Assert.Equal(afterWrite ? SyntheticSearchInputDecision.Granted : SyntheticSearchInputDecision.Revoked, original.DecisionBeforeWrite);
        Assert.Equal("SEARCH_TASK_GRANTS_STOPPED", (await Assert.ThrowsAsync<BrowserOperationException>(() => journal.PrepareAsync(intents[2], Granted))).Code);
        Assert.False(journal.CanAdvance);
        Assert.Equal(2, journal.PreparedResponses);
        Assert.False(File.Exists(Path.Combine(journal.DirectoryPath, "intent-0003.json")));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath));
        Assert.Equal("SEARCH_TASK_GRANTS_INCOMPLETE", Assert.Throws<BrowserOperationException>(journal.RequireComplete).Code);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("field")]
    [InlineData("activation")]
    [InlineData("reused-document")]
    public async Task SearchGrantLedgerRejectsChangedOrReusedNativeScope(string change)
    {
        var start = Start();
        var journal = await SyntheticSearchTaskGrants.BeginAsync(NewRoot(), start);
        var intents = Intents(start);
        var position = change switch { "document" => 2, "field" => 3, "activation" => 15, _ => 17 };
        foreach (var intent in intents.Take(position)) await journal.PrepareAsync(intent, Granted);
        var next = intents[position];
        var input = next.Input!;
        var changed = next with
        {
            Input = input with
            {
                DocumentId = change == "document" ? "foreign-document" : change == "reused-document" ? "document-1" : input.DocumentId,
                NodeId = change is "field" or "activation" ? "different-field" : input.NodeId
            }
        };
        var code = change is "document" or "reused-document" ? "SEARCH_TASK_DOCUMENT_CHANGED_OR_REUSED"
            : change == "field" ? "SEARCH_TASK_FIELD_CHANGED" : "SEARCH_TASK_ACTIVATION_SCOPE_INVALID";
        var checks = 0;
        Assert.Equal(code, (await Assert.ThrowsAsync<BrowserOperationException>(() => journal.PrepareAsync(changed, _ => { checks++; return Task.FromResult(true); }))).Code);
        Assert.Equal(0, checks);
        Assert.False(File.Exists(Path.Combine(journal.DirectoryPath, $"intent-{next.Sequence:D4}.json")));
        Assert.False(journal.CanAdvance);
    }

    [Fact]
    public async Task ANewSearchWorkerCannotReplayTheSameGenerationFence()
    {
        var start = Start();
        var root = NewRoot();
        var journal = await SyntheticSearchTaskGrants.BeginAsync(root, start);
        await journal.PrepareAsync(Intents(start)[0], Granted);
        var header = await File.ReadAllBytesAsync(Path.Combine(journal.DirectoryPath, "start.json"));
        var changed = start with { WorkerId = Guid.NewGuid(), ExecutionId = Guid.NewGuid() };
        Assert.Equal("SEARCH_TASK_REPLAY_OR_FENCE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => SyntheticSearchTaskGrants.BeginAsync(root, changed))).Code);
        Assert.Equal(header, await File.ReadAllBytesAsync(Path.Combine(journal.DirectoryPath, "start.json")));
        Assert.Equal(1, journal.PreparedResponses);
    }

    [Fact]
    public async Task FailedSearchAuthorityRetainsOnlyAClosedUnconfirmedDecision()
    {
        var start = Start();
        var journal = await SyntheticSearchTaskGrants.BeginAsync(NewRoot(), start);
        var ack = await journal.PrepareAsync(Intents(start)[0], _ => throw new IOException("never-retain-authority-secret"));
        Assert.Equal(SyntheticSearchInputDecision.Unconfirmed, ack.Decision);
        var intent = await ReadAsync<SyntheticSearchTaskGrant>(Path.Combine(journal.DirectoryPath, "intent-0001.json"));
        Assert.False(intent.InputMayHaveOccurred);
        Assert.Equal(SyntheticSearchInputDecision.Unconfirmed, intent.DecisionBeforeWrite);
        foreach (var path in Directory.GetFiles(journal.DirectoryPath))
            Assert.DoesNotContain("never-retain-authority-secret", await File.ReadAllTextAsync(path));
        Assert.False(journal.CanAdvance);
    }

    [Fact]
    public async Task CancelledPendingSearchAuthorityCannotPrepareALaterGrantOrReplay()
    {
        var start = Start();
        var root = NewRoot();
        var journal = await SyntheticSearchTaskGrants.BeginAsync(root, start);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        var operation = journal.PrepareAsync(Intents(start)[0], async token =>
        {
            asked.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }, stop.Token);
        await asked.Task;
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.False(journal.CanAdvance);
        Assert.Equal(0, journal.PreparedResponses);
        Assert.Single(Directory.GetFiles(journal.DirectoryPath));
        Assert.Equal("SEARCH_TASK_REPLAY_OR_FENCE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => SyntheticSearchTaskGrants.BeginAsync(root, start))).Code);
    }

    [Fact]
    public async Task FailedSearchIntentWriteCannotReturnAGrantOrOverwriteTheCollision()
    {
        var start = Start();
        var journal = await SyntheticSearchTaskGrants.BeginAsync(NewRoot(), start);
        var collision = Path.Combine(journal.DirectoryPath, "intent-0001.json");
        await File.WriteAllTextAsync(collision, "existing-original");
        Assert.Equal("SEARCH_TASK_GRANT_WRITE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => journal.PrepareAsync(Intents(start)[0], Granted))).Code);
        Assert.Equal("existing-original", await File.ReadAllTextAsync(collision));
        Assert.False(File.Exists(Path.Combine(journal.DirectoryPath, "response-0001.json")));
        Assert.False(journal.CanAdvance);
        Assert.Equal(0, journal.PreparedResponses);
    }

    private static Task<bool> Granted(CancellationToken token) => Task.FromResult(true);
    private static async Task<T> ReadAsync<T>(string path) => JsonSerializer.Deserialize<T>(await File.ReadAllBytesAsync(path), ReaderWorkerWire.Json)!;
    private static string NewRoot() => Path.GetFullPath(Path.Combine("artifacts", "search-task-grants", Guid.NewGuid().ToString("N")));
    private static SyntheticSearchTaskStart Start() => new(SyntheticSearchTaskControl.Schema,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId("search-working")), Guid.NewGuid(), Guid.NewGuid());

    private static SyntheticSearchTaskIntent[] Intents(SyntheticSearchTaskStart start) => SyntheticSearchTaskControl.Boundaries.Select((boundary, index) =>
    {
        SyntheticSearchInputIntent? input = boundary.Action == "Native" ? new(boundary.NativeSequence, boundary.Kind!.Value, boundary.NativeKey,
            boundary.CharacterIndex, start.Job.Source.Sha256, "document-" + boundary.Trial, 1,
            boundary.Kind is SyntheticSearchInputKind.Character or SyntheticSearchInputKind.MouseFocus || boundary.NativeSequence == 15 && boundary.NativeKey == "Tab"
                || boundary.NativeSequence == 15 && boundary.Trial <= 2 ? "search" : "control") : null;
        return new SyntheticSearchTaskIntent(SyntheticSearchTaskControl.Schema, start, index + 1, boundary, input);
    }).ToArray();
}
