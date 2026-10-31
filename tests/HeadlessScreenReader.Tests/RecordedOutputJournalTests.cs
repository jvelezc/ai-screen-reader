using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class RecordedOutputJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-output-test-" + Guid.NewGuid().ToString("N"));
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly Lazy<Task<(FixtureJob Job, byte[] Bytes)>> Original = new(async () =>
    {
        var job = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 };
        return (job, await KeyboardFixtureWorker.RecordAsync(job));
    });
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private string Outputs => Path.Combine(_root, "outputs");
    private string DirectoryFor(FixtureJob job) => Path.Combine(Outputs, $"{job.OrganizationId:N}-{job.JobId:N}-{job.Generation}");
    private async Task<(FixtureJob Job, byte[] Bytes, Guid Worker, OwnershipJournal Native, NativeOwnership Intent, RecordedOutputJournal Output)> Started()
    {
        var (job, bytes) = await Original.Value; var worker = Guid.NewGuid(); var native = new OwnershipJournal(Path.Combine(_root, "native"));
        var intent = await native.BeginAsync(job with { RecordObservation = false, TraversalRoute = 0 }, "podman", Image, default);
        var output = await RecordedOutputJournal.BeginAsync(Outputs, intent, job, worker, default);
        return (job, bytes, worker, native, intent, output);
    }
    private static Task<bool> Allow(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }

    [Fact]
    public async Task ActualChromiumOriginalOutputSurvivesReopenBeforePublicationWithoutReplayOrGoalAuthority()
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default);
        var read = RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default);
        Assert.Equal(state.Bytes, read.Bundle); Assert.Equal(state.Job, read.Header.Job); Assert.Equal(state.Worker, read.Header.WorkerId);
        Assert.Equal(state.Intent, read.Header.Native); Assert.Equal(state.Bytes.Length, read.Seal.Bytes);
        Assert.Equal(TraversalArchive.Hash(state.Bytes), read.Seal.BundleSha256);
        Assert.Null(read.NativeRemoval); Assert.False(read.IsCompletionReceipt); Assert.False(read.CanReplay); Assert.Equal("NotAssessed", read.GoalVerification);
        var route = KeyboardRecordingBundle.Decode(read.Bundle, state.Job).Validate(state.Job).Receipt;
        Assert.Equal(new[] { "Tab", "Activate" }, route.Attempts.Select(a => a.Instruction.Command.ToString()));
        Assert.Equal(2, route.CompletedKeystrokes);
        Assert.Equal(state.Intent, Assert.Single(state.Native.ReadPending()));
    }
    [Fact]
    public async Task RemovalAcknowledgementComesFromMatchingNativeJournalAndDoesNotPublishAnObservation()
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default);
        await Assert.ThrowsAsync<FileNotFoundException>(() => state.Output.AcknowledgeRemovalAsync(state.Native, default));
        Assert.Null(RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default).NativeRemoval);
        await state.Native.RecordRemovalAsync(state.Intent, true, default);
        await state.Output.AcknowledgeRemovalAsync(state.Native, default);
        var read = RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default);
        Assert.Equal(state.Bytes, read.Bundle); Assert.Equal(state.Intent, read.NativeRemoval!.Intent);
        Assert.True(read.NativeRemoval.ContainerAbsenceConfirmed); Assert.True(read.NativeRemoval.RecoveredAfterRestart);
        Assert.False(read.IsCompletionReceipt); Assert.False(read.CanReplay); Assert.Empty(state.Native.ReadPending());
    }
    [Theory]
    [InlineData("tenant")]
    [InlineData("job")]
    [InlineData("generation")]
    public async Task DifferentStorageScopeCannotSelectAnotherOriginal(string field)
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default);
        var other = field switch { "tenant" => state.Job with { OrganizationId = Guid.NewGuid() }, "job" => state.Job with { JobId = Guid.NewGuid() }, _ => state.Job with { Generation = 2 } };
        Assert.Throws<FileNotFoundException>(() => RecordedOutputJournal.Read(Outputs, other, state.Worker, default));
        Assert.Equal(state.Bytes, RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default).Bundle);
    }
    [Theory]
    [InlineData("scan")]
    [InlineData("worker")]
    [InlineData("route")]
    [InlineData("recording")]
    public async Task HeaderScopeCannotBeChangedEvenWithTheCorrectStoragePrefix(string field)
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default);
        var expected = field switch { "scan" => state.Job with { ScanId = Guid.NewGuid() }, "route" => state.Job with { TraversalRoute = 2 }, "recording" => state.Job with { RecordObservation = false, TraversalRoute = 0 }, _ => state.Job };
        var worker = field == "worker" ? Guid.NewGuid() : state.Worker;
        Assert.Equal("WORKER_OUTPUT_INVALID", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, expected, worker, default)).Code);
    }
    [Fact]
    public async Task AHeaderOrUncommittedBundleCannotBecomeASealedRecording()
    {
        var state = await Started(); var directory = DirectoryFor(state.Job);
        Assert.Equal("WORKER_OUTPUT_NOT_SEALED", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
        await File.WriteAllBytesAsync(Path.Combine(directory, "bundle.bin.pending"), state.Bytes);
        Assert.Equal("WORKER_OUTPUT_NOT_SEALED", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
        Assert.False(File.Exists(Path.Combine(directory, "bundle.bin"))); Assert.False(File.Exists(Path.Combine(directory, "seal.json")));
    }
    [Theory]
    [InlineData("schema")]
    [InlineData("header-hash")]
    [InlineData("bundle-hash")]
    [InlineData("length")]
    [InlineData("oversize")]
    public async Task SealedMetadataCannotBlessDifferentOrUnboundedBytes(string field)
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default); var path = Path.Combine(DirectoryFor(state.Job), "seal.json");
        var seal = JsonSerializer.Deserialize<RecordedOutputSeal>(await File.ReadAllBytesAsync(path), ReaderWorkerWire.Json)!;
        seal = field switch { "schema" => seal with { Schema = "other" }, "header-hash" => seal with { HeaderSha256 = new('a', 64) }, "bundle-hash" => seal with { BundleSha256 = new('b', 64) }, "length" => seal with { Bytes = seal.Bytes + 1 }, _ => seal with { Bytes = PassiveObservationRecording.MaximumBundleBytes + 1 } };
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(seal, ReaderWorkerWire.Json));
        Assert.Equal("WORKER_OUTPUT_INVALID", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
    }
    [Theory]
    [InlineData("changed")]
    [InlineData("truncated")]
    [InlineData("extra")]
    public async Task ActualOutputCorruptionFailsBeforeRecovery(string field)
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default); var path = Path.Combine(DirectoryFor(state.Job), "bundle.bin");
        var bytes = state.Bytes.ToArray();
        if (field == "changed") bytes[bytes.Length / 2] ^= 1;
        else if (field == "truncated") bytes = bytes[..^1];
        else bytes = [.. bytes, 0];
        await File.WriteAllBytesAsync(path, bytes);
        Assert.Equal("WORKER_OUTPUT_INVALID", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
    }
    [Fact]
    public async Task DuplicateBeginOrSealCannotReplaceTheOriginalAttemptOrBytes()
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default);
        await Assert.ThrowsAsync<IOException>(() => RecordedOutputJournal.BeginAsync(Outputs, state.Intent, state.Job, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<IOException>(() => state.Output.SealAsync(state.Bytes, Allow, default));
        var read = RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default);
        Assert.Equal(state.Worker, read.Header.WorkerId); Assert.Equal(state.Bytes, read.Bundle); Assert.False(read.CanReplay);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AuthorityLossAtEverySealBoundaryPreventsSuccessAndNeverAuthorizesPublication(int deniedAt)
    {
        var state = await Started(); var calls = 0;
        var failure = await Assert.ThrowsAsync<BrowserOperationException>(() => state.Output.SealAsync(state.Bytes, _ => Task.FromResult(++calls != deniedAt), default));
        Assert.Equal("WORKER_OUTPUT_AUTHORITY_UNCONFIRMED", failure.Code); Assert.Equal(deniedAt, calls);
        if (deniedAt < 3) Assert.Equal("WORKER_OUTPUT_NOT_SEALED", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
        else
        {
            var read = RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default);
            Assert.Equal(state.Bytes, read.Bundle); Assert.False(read.IsCompletionReceipt); Assert.False(read.CanReplay); Assert.Null(read.NativeRemoval);
        }
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationCannotSealPartiallyStagedBytes(int cancelledAt)
    {
        var state = await Started(); var calls = 0; using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.Output.SealAsync(state.Bytes, _ => { if (++calls == cancelledAt) stop.Cancel(); return Task.FromResult(true); }, stop.Token));
        Assert.Equal("WORKER_OUTPUT_NOT_SEALED", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
        Assert.Equal(cancelledAt == 2, File.Exists(Path.Combine(DirectoryFor(state.Job), "bundle.bin")));
    }
    [Theory]
    [InlineData("foreign")]
    [InlineData("unconfirmed")]
    public async Task AForeignOrUnconfirmedNativeRemovalCannotBlessStagedOutput(string field)
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default);
        await state.Native.RecordRemovalAsync(state.Intent, false, default);
        var path = Path.Combine(state.Native.RemovalsDirectory, state.Intent.Owner.ToString("N") + ".json");
        var removal = JsonSerializer.Deserialize<NativeRemoval>(await File.ReadAllBytesAsync(path), ReaderWorkerWire.Json)!;
        removal = field == "foreign" ? removal with { Intent = removal.Intent with { Owner = Guid.NewGuid() } } : removal with { ContainerAbsenceConfirmed = false };
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(removal, ReaderWorkerWire.Json));
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => state.Output.AcknowledgeRemovalAsync(state.Native, default))).Code);
        Assert.Null(RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default).NativeRemoval);
    }
    [Fact]
    public async Task UnknownInventoryAndOversizedPendingWritesAreNotSilentlyIgnored()
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default); var directory = DirectoryFor(state.Job);
        var unknown = Path.Combine(directory, "other.json"); await File.WriteAllTextAsync(unknown, "{}");
        Assert.Equal("WORKER_OUTPUT_INVALID", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
        File.Delete(unknown);
        await File.WriteAllBytesAsync(Path.Combine(directory, "seal.json.pending"), new byte[8193]);
        Assert.Equal("WORKER_OUTPUT_INVALID", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
    }
    [Theory]
    [InlineData("worker")]
    [InlineData("native-job")]
    [InlineData("not-recorded")]
    public async Task InvalidProducerIdentityIsRejectedBeforeCreatingAStage(string field)
    {
        var job = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 };
        var native = new NativeOwnership(NativeOwnership.CurrentSchema, Guid.NewGuid(), job with { RecordObservation = false, TraversalRoute = 0 }, "podman", Image);
        if (field == "native-job") native = native with { Job = native.Job with { ScanId = Guid.NewGuid() } };
        if (field == "not-recorded") job = job with { RecordObservation = false, TraversalRoute = 0 };
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => RecordedOutputJournal.BeginAsync(Outputs, native, job, field == "worker" ? Guid.Empty : Guid.NewGuid(), default))).Code);
        Assert.False(Directory.Exists(Outputs));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(PassiveObservationRecording.MaximumBundleBytes + 1)]
    public async Task InvalidPayloadSizeIsRejectedBeforeAuthorizationOrWriting(int size)
    {
        var state = await Started(); var calls = 0;
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => state.Output.SealAsync(new byte[size], _ => { calls++; return Task.FromResult(true); }, default))).Code);
        Assert.Equal(0, calls); Assert.Equal(new[] { "start.json" }, Directory.GetFiles(DirectoryFor(state.Job)).Select(Path.GetFileName));
    }
    [Fact]
    public async Task CancelledBeginDoesNotAllocateARecordingStage()
    {
        var job = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 };
        var native = new NativeOwnership(NativeOwnership.CurrentSchema, Guid.NewGuid(), job with { RecordObservation = false, TraversalRoute = 0 }, "podman", Image);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecordedOutputJournal.BeginAsync(Outputs, native, job, Guid.NewGuid(), stop.Token));
        Assert.False(Directory.Exists(Outputs));
    }
    [Fact]
    public async Task AFailedSealWriteRetainsOnlyUnpublishedBytesAndDoesNotOverwriteTheFailedWrite()
    {
        var state = await Started(); var directory = DirectoryFor(state.Job); var failed = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(Path.Combine(directory, "seal.json.pending"), failed);
        await Assert.ThrowsAsync<IOException>(() => state.Output.SealAsync(state.Bytes, Allow, default));
        Assert.Equal(failed, await File.ReadAllBytesAsync(Path.Combine(directory, "seal.json.pending")));
        Assert.Equal(state.Bytes, await File.ReadAllBytesAsync(Path.Combine(directory, "bundle.bin")));
        Assert.Equal("WORKER_OUTPUT_NOT_SEALED", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
    }
    [Fact]
    public async Task ALinkedAttemptDirectoryCannotExposeBytesFromAnotherLocation()
    {
        var state = await Started(); await state.Output.SealAsync(state.Bytes, Allow, default);
        var original = DirectoryFor(state.Job); var outside = Path.Combine(_root, "outside");
        Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, Path.GetFullPath(original));
        Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, Path.GetFullPath(outside));
        Directory.Move(original, outside);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in new[] { "/c", "mklink", "/J", original, outside }) start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!; await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(original, outside);
            Assert.Equal("WORKER_OUTPUT_INVALID", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, state.Job, state.Worker, default)).Code);
            Assert.Equal(state.Bytes, await File.ReadAllBytesAsync(Path.Combine(outside, "bundle.bin")));
        }
        finally { if (Directory.Exists(original)) Directory.Delete(original); Directory.Move(outside, original); }
    }
    [Theory]
    [InlineData("WORKER_OUTPUT_WRITE_UNCONFIRMED")]
    [InlineData("WORKER_OUTPUT_AUTHORITY_UNCONFIRMED")]
    [InlineData("WORKER_OUTPUT_REMOVAL_WRITE_UNCONFIRMED")]
    public async Task CoordinatorKeepsOutputFailureVisibleWithoutCompletionOrAutomaticReplay(string code)
    {
        var lease = new WorkerLease(FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 }, Guid.NewGuid(), Guid.NewGuid());
        var leases = new Leases(lease); var executor = new FailedOutput(code);
        var result = await new ObservationCoordinator(leases, executor, new ObservationStore(Path.Combine(_root, "observations"))).RunOnceAsync(lease.WorkerId, default);
        Assert.Equal("Blocked", result.State); Assert.Equal("FIXTURE_OBSERVATION_UNCONFIRMED", result.ErrorCode); Assert.Equal(code, result.EvidenceErrorCode);
        Assert.Equal(1, executor.Calls); Assert.Equal(1, leases.Blocks); Assert.Equal(0, leases.Completions); Assert.False(Directory.Exists(Path.Combine(_root, "observations")));
    }
    private sealed class FailedOutput(string code) : IFixtureExecutor
    {
        public int Calls { get; private set; }
        public Task<byte[]> ExecuteAsync(FixtureJob job, CancellationToken token) { Calls++; throw new BrowserOperationException(code); }
    }
    private sealed class Leases(WorkerLease lease) : IWorkerLeases
    {
        public int Blocks { get; private set; }
        public int Completions { get; private set; }
        public Task<WorkerClaim> ClaimAsync(Guid worker, CancellationToken token) => Task.FromResult(new WorkerClaim("Claimed", lease));
        public Task<bool> RenewAsync(WorkerLease actual, CancellationToken token) => Task.FromResult(true);
        public Task<bool> CompleteAsync(WorkerLease actual, StoredObservation stored, CancellationToken token) { Completions++; return Task.FromResult(true); }
        public Task<bool> BlockAsync(WorkerLease actual, string code, CancellationToken token) { Blocks++; return Task.FromResult(true); }
    }
}
