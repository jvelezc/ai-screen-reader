using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchTaskSourceTests
{
    [Theory]
    [InlineData("execution")]
    [InlineData("sequence")]
    [InlineData("trial")]
    [InlineData("document")]
    [InlineData("epoch")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("path")]
    [InlineData("hash")]
    [InlineData("time")]
    [InlineData("jpeg")]
    [InlineData("oversized")]
    public void SearchSourceProtocolRejectsForeignOrMalformedOriginals(string change)
    {
        var start = Start(); var batch = Batch(start);
        var frame = batch.Frames[0];
        var changed = change switch
        {
            "execution" => batch with { ExecutionId = Guid.Empty },
            "sequence" => batch with { Sequence = 2 },
            "trial" => batch with { Trial = 6 },
            "document" => batch with { DocumentId = "" },
            "epoch" => batch with { CaptureEpoch = 2 },
            "empty" => batch with { Frames = [] },
            "duplicate" => batch with { Frames = [frame, frame] },
            "path" => batch with { Frames = [frame with { File = "../source-0001.jpg" }] },
            "hash" => batch with { Frames = [frame with { Sha256 = new('a', 64) }] },
            "time" => batch with { Frames = [frame with { PresentationUnixMilliseconds = double.NaN }] },
            "jpeg" => batch with { Frames = [frame with { Content = new byte[] { 1, 2, 3, 4 } }] },
            _ => batch with { Frames = [frame with { Content = new byte[512 * 1024 + 1] }] }
        };
        SyntheticSearchSourceProtocol.Validate(batch, start.ExecutionId, 1);
        Assert.Equal("SEARCH_TASK_SOURCE_FRAMES_INVALID", Assert.Throws<BrowserOperationException>(() => SyntheticSearchSourceProtocol.Validate(changed, start.ExecutionId, 1)).Code);
    }

    [Fact]
    public async Task SearchSourceJournalSnapshotsBytesAndRetainsAnExplicitOriginalSubset()
    {
        var start = Start(); var journal = await OpenAsync(start); var batch = Batch(start);
        var expectedHash = batch.Frames[0].Sha256;
        var ack = await journal.RetainSourcesAsync(batch, _ => { batch.Frames[0].Content[3] = 99; return Task.FromResult(true); });
        Assert.Equal(start.ExecutionId, ack.ExecutionId);
        Assert.Equal(1, ack.Sequence);
        var retained = journal.ReadRetainedSources();
        Assert.Equal(expectedHash, Assert.Single(retained.Files).Image.Sha256);
        Assert.Equal(4, retained.Bytes);
        Assert.False(retained.HasUncommittedFiles);
        Assert.False(retained.CanReplay);
        Assert.False(retained.IsCompletedRecording);
        Assert.Equal("NotAssessed", retained.Completeness);
        Assert.Equal("RetainedSyntheticSearchOriginalSubset", retained.Scope);
        Assert.Equal(start, retained.Start);
        Assert.Equal(new byte[] { 0xff, 0xd8, 0xff, 42 }, await File.ReadAllBytesAsync(Path.Combine(journal.DirectoryPath, "trial-1", "source-0001.jpg")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchSourceAuthorityLossNeverAcknowledgesOrDiscardsThePermittedSubset(bool afterWrite)
    {
        var start = Start(); var journal = await OpenAsync(start); var calls = 0;
        Assert.Equal("SEARCH_TASK_SOURCE_AUTHORITY_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            journal.RetainSourcesAsync(Batch(start), _ => Task.FromResult(afterWrite && ++calls == 1)))).Code);
        var retained = journal.ReadRetainedSources();
        Assert.Equal(afterWrite ? 1 : 0, retained.Files.Length);
        Assert.Equal(afterWrite ? 4 : 0, retained.Bytes);
        Assert.False(retained.IsCompletedRecording);
        Assert.False(journal.CanAdvance);
        Assert.Equal(1, journal.PreparedResponses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchSourceFramesCannotPrecedeOrEscapeTheirGrantedContext(bool unopened)
    {
        var start = Start();
        var journal = unopened ? await SyntheticSearchTaskGrants.BeginAsync(NewRoot(), start) : await OpenAsync(start);
        var batch = unopened ? Batch(start) : Batch(start) with { Trial = 2 };
        Assert.Equal("SEARCH_TASK_SOURCE_FRAMES_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => journal.RetainSourcesAsync(batch, Granted))).Code);
        Assert.Empty(journal.ReadRetainedSources().Files);
        Assert.False(journal.CanAdvance);
    }

    [Fact]
    public async Task InterruptedSearchSourceCommitKeepsUncommittedOriginalsWithoutInventingACommit()
    {
        var start = Start(); var journal = await OpenAsync(start); var batch = Batch(start);
        var digest = TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(batch, ReaderWorkerWire.Json));
        Directory.CreateDirectory(Path.Combine(journal.DirectoryPath, $"sources-0001-{digest}.json"));
        Assert.Equal("SEARCH_TASK_SOURCE_WRITE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => journal.RetainSourcesAsync(batch, Granted))).Code);
        var retained = journal.ReadRetainedSources();
        Assert.Empty(retained.Files);
        Assert.True(retained.HasUncommittedFiles);
        Assert.False(retained.IsCompletedRecording);
        Assert.Equal(new byte[] { 0xff, 0xd8, 0xff, 42 }, await File.ReadAllBytesAsync(Path.Combine(journal.DirectoryPath, "trial-1", "source-0001.jpg")));
        Assert.Single(Directory.GetFiles(journal.DirectoryPath, "pending-sources-*.json"));
    }

    [Fact]
    public async Task ChangedSearchOriginalCannotPassReadbackAndDuplicateDeliveryCannotOverwriteIt()
    {
        var start = Start(); var journal = await OpenAsync(start); var batch = Batch(start);
        await journal.RetainSourcesAsync(batch, Granted);
        var original = Path.Combine(journal.DirectoryPath, "trial-1", "source-0001.jpg");
        Assert.Equal("SEARCH_TASK_SOURCE_FRAMES_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => journal.RetainSourcesAsync(batch with { Sequence = 2 }, Granted))).Code);
        Assert.Equal(batch.Frames[0].Content, await File.ReadAllBytesAsync(original));
        await File.WriteAllBytesAsync(original, new byte[] { 0xff, 0xd8, 0xff, 43 });
        Assert.Equal("SEARCH_TASK_SOURCE_FRAMES_INVALID", Assert.Throws<BrowserOperationException>(() => journal.ReadRetainedSources()).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedSearchSourceAndNativeIntentsMustUseTheSameDocument(bool changeNative)
    {
        var start = Start(); var journal = await OpenAsync(start);
        await journal.RetainSourcesAsync(Batch(start), Granted);
        if (changeNative)
        {
            var input = new SyntheticSearchInputIntent(1, SyntheticSearchInputKind.Key, "Tab", 0, start.Job.Source.Sha256, "other-document", 1, null);
            var intent = new SyntheticSearchTaskIntent(SyntheticSearchTaskControl.Schema, start, 2, SyntheticSearchTaskControl.Boundaries[1], input);
            Assert.Equal("SEARCH_TASK_DOCUMENT_CHANGED_OR_REUSED", (await Assert.ThrowsAsync<BrowserOperationException>(() => journal.PrepareAsync(intent, Granted))).Code);
        }
        else
        {
            var frame = Batch(start).Frames[0] with { SourceSequence = 2, File = "source-0002.jpg" };
            Assert.Equal("SEARCH_TASK_SOURCE_FRAMES_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
                journal.RetainSourcesAsync(Batch(start) with { Sequence = 2, DocumentId = "other-document", Frames = [frame] }, Granted))).Code);
        }
        Assert.Equal("document-1", Assert.Single(journal.ReadRetainedSources().Files).DocumentId);
        Assert.False(journal.CanAdvance);
        Assert.False(File.Exists(Path.Combine(journal.DirectoryPath, "trial-1", "source-0002.jpg")));
    }

    [Fact]
    public async Task SearchOriginalBudgetAcceptsExactlyTwentyFiveMiBAndRetainsItWhenTheNextBatchIsDenied()
    {
        var start = Start(); var journal = await OpenAsync(start);
        var bytes = new byte[512 * 1024];
        bytes[0] = 0xff; bytes[1] = 0xd8; bytes[2] = 0xff;
        var digest = TraversalArchive.Hash(bytes);
        for (var index = 1; index <= 50; index++)
        {
            var frame = new FixtureSourceFrame(index, $"source-{index:D4}.jpg", 123456 + index, digest, bytes);
            var ack = await journal.RetainSourcesAsync(Batch(start) with { Sequence = index, Frames = [frame] }, Granted);
            Assert.Equal(index, ack.Sequence);
        }
        var next = new FixtureSourceFrame(51, "source-0051.jpg", 123507, digest, bytes);
        Assert.Equal("SEARCH_TASK_SOURCE_FRAMES_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            journal.RetainSourcesAsync(Batch(start) with { Sequence = 51, Frames = [next] }, Granted))).Code);
        var retained = journal.ReadRetainedSources();
        Assert.Equal(25 * 1024 * 1024, retained.Bytes);
        Assert.Equal(50, retained.Files.Length);
        Assert.False(retained.HasUncommittedFiles);
        Assert.False(retained.IsCompletedRecording);
        Assert.False(File.Exists(Path.Combine(journal.DirectoryPath, "trial-1", next.File)));
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task ActualSearchRevocationRetainsEarlierNativeOriginalsAndNeverGrantsTheRemainingQuery()
    {
        var start = Start(); var journal = await OpenAsync(start);
        ChromiumRecordingProbe? owned = null;
        var sourceSequence = 0;
        var sourceDocument = "";
        long sourceEpoch = 0;
        async Task Sources(ImmutableArray<CompletedCaptureSource> originals, CancellationToken token)
        {
            if (originals.IsDefaultOrEmpty) return;
            var batch = new SyntheticSearchSourceBatch(SyntheticSearchSourceProtocol.Schema, start.ExecutionId, 1, ++sourceSequence, sourceDocument, sourceEpoch,
                [.. originals.Select(original => new FixtureSourceFrame(original.Frame.Sequence, original.Frame.File,
                    original.Frame.PresentationUnixMilliseconds, original.Frame.Sha256, original.Content))]);
            await journal.RetainSourcesAsync(batch, Granted, token);
        }
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(NewRoot(), BundledFixture.MockSearchWorkingV1,
            async (input, token) =>
            {
                sourceDocument = input.DocumentId;
                sourceEpoch = input.CaptureEpoch;
                await owned!.CopyCompletedSourcesAsync(Sources, token);
                var boundary = SyntheticSearchTaskControl.Boundaries[journal.PreparedResponses];
                var ack = await journal.PrepareAsync(new(SyntheticSearchTaskControl.Schema, start, journal.PreparedResponses + 1, boundary, input),
                    _ => Task.FromResult(input.CharacterIndex != 7), token);
                return ack.Decision;
            });
        owned = probe;
        var browser = (ChromiumFixtureBrowser)probe.Browser;
        using var reader = new ScreenReaderSession(probe.Browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await browser.DiagnosticPage.EvaluateAsync("() => { window.nativeKeys = 0; document.addEventListener('keydown', () => window.nativeKeys++); }");
        var entry = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, "sample course");
        Assert.Equal("SYNTHETIC_SEARCH_AUTHORITY_REVOKED", entry.ErrorCode);
        Assert.Equal(6, entry.BrowserKeystrokes);
        Assert.Equal(6, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.nativeKeys"));
        Assert.Equal("sample", await browser.DiagnosticPage.Locator("#sample-search").InputValueAsync());
        Assert.Null(entry.BarrierCode);
        await browser.ExcludeCaptureAsync();
        var denied = await reader.ExecuteAsync(ReaderCommand.Tab);
        Assert.Equal(0, denied.BrowserKeystrokes);
        Assert.Equal(9, journal.PreparedResponses);
        Assert.False(journal.CanAdvance);
        var retained = journal.ReadRetainedSources();
        Assert.NotEmpty(retained.Files);
        Assert.True(retained.Bytes > 512);
        Assert.False(retained.IsCompletedRecording);
        Assert.False(retained.CanReplay);
        Assert.All(retained.Files, file => Assert.Equal(1, file.Trial));
        await probe.DisposeAsync();
        Assert.True(probe.ShutdownStatus.ProcessExitConfirmed);
    }

    private static Task<bool> Granted(CancellationToken token) => Task.FromResult(true);
    private static string NewRoot() => Path.GetFullPath(Path.Combine("artifacts", "search-task-sources", Guid.NewGuid().ToString("N")));
    private static SyntheticSearchTaskStart Start() => new(SyntheticSearchTaskControl.Schema,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId("search-working")), Guid.NewGuid(), Guid.NewGuid());
    private static SyntheticSearchSourceBatch Batch(SyntheticSearchTaskStart start)
    {
        byte[] bytes = [0xff, 0xd8, 0xff, 42];
        return new(SyntheticSearchSourceProtocol.Schema, start.ExecutionId, 1, 1, "document-1", 1,
            [new(1, "source-0001.jpg", 123456, TraversalArchive.Hash(bytes), bytes)]);
    }
    private static async Task<SyntheticSearchTaskGrants> OpenAsync(SyntheticSearchTaskStart start)
    {
        var journal = await SyntheticSearchTaskGrants.BeginAsync(NewRoot(), start);
        await journal.PrepareAsync(new(SyntheticSearchTaskControl.Schema, start, 1, SyntheticSearchTaskControl.Boundaries[0], null), Granted);
        return journal;
    }
}
