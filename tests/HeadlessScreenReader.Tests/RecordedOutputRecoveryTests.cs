using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class RecordedOutputRecoveryTests : IDisposable
{
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly FixtureJob Job = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 };
    private static readonly Lazy<Task<byte[]>> Original = new(() => KeyboardFixtureWorker.RecordAsync(Job));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-output-recovery-" + Guid.NewGuid().ToString("N"));
    private string Outputs => Path.Combine(_root, "recorded-output");
    private string Stage => Path.Combine(Outputs, $"{Job.OrganizationId:N}-{Job.JobId:N}-{Job.Generation}");
    private string Marker => Path.Combine(Stage, "native-removal.json");
    private Guid Worker { get; } = Guid.NewGuid();
    public void Dispose()
    {
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(_root)));
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private async Task<(OwnershipJournal Native, NativeOwnership Intent, RecordedOutputJournal Output)> Started(bool seal = false, bool removed = true)
    {
        var native = new OwnershipJournal(_root);
        var intent = await native.BeginAsync(Job with { RecordObservation = false, TraversalRoute = 0 }, "podman", Image, default);
        var output = await RecordedOutputJournal.BeginAsync(Outputs, intent, Job, Worker, default);
        if (seal) await output.SealAsync(await Original.Value, _ => Task.FromResult(true), default);
        if (removed) await native.RecordRemovalAsync(intent, true, default);
        return (native, intent, output);
    }
    [Fact]
    public async Task RetiredIntentRecoveryPreservesActualChromiumBytesAndOriginalProducerWithoutPublication()
    {
        var state = await Started(seal: true); Assert.Empty(state.Native.ReadPending());
        var before = RecordedOutputJournal.Read(Outputs, Job, Worker, default); Assert.Null(before.NativeRemoval);
        var result = await RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default);
        Assert.Equal(new(1, 1, 0, 0), result); Assert.False(result.IsCompletionReceipt); Assert.False(result.CanReplay);
        var after = RecordedOutputJournal.Read(Outputs, Job, Worker, default);
        Assert.Equal(before.Header, after.Header); Assert.Equal(before.Seal, after.Seal); Assert.Equal(await Original.Value, after.Bundle);
        Assert.Equal(state.Intent, after.NativeRemoval!.Intent); Assert.True(after.NativeRemoval.RecoveredAfterRestart);
        Assert.True(after.NativeRemoval.ContainerAbsenceConfirmed); Assert.False(after.IsCompletionReceipt); Assert.False(after.CanReplay);
        Assert.Equal("NotAssessed", after.GoalVerification); Assert.Equal(2, KeyboardRecordingBundle.Decode(after.Bundle, Job).Validate(Job).Receipt.CompletedKeystrokes);
        Assert.Equal(new[] { "intents", "recorded-output", "removals" }, Directory.GetDirectories(_root).Select(Path.GetFileName).Order());
    }
    [Fact]
    public async Task ExecutorStartupRepairsRetiredIntentWithoutCallingTheRuntimeOrPublishing()
    {
        var state = await Started();
        var executor = new IsolatedFixtureExecutor("podman", Image, journal: state.Native);
        Assert.Equal(0, await executor.RecoverAsync());
        var marker = JsonSerializer.Deserialize<RecordedOutputRemoval>(await File.ReadAllBytesAsync(Marker), ReaderWorkerWire.Json)!;
        Assert.Equal(state.Intent, marker.Removal.Intent); Assert.True(marker.Removal.ContainerAbsenceConfirmed);
        Assert.Equal("WORKER_OUTPUT_NOT_SEALED", Assert.Throws<BrowserOperationException>(() => RecordedOutputJournal.Read(Outputs, Job, Worker, default)).Code);
        Assert.False(File.Exists(Path.Combine(Stage, "bundle.bin"))); Assert.False(File.Exists(Path.Combine(Stage, "seal.json")));
    }
    [Fact]
    public async Task RepeatedRecoveryAndParentAcknowledgementPreserveTheExactOriginalMarker()
    {
        var state = await Started(); await state.Output.AcknowledgeRemovalAsync(state.Native, default);
        var original = await File.ReadAllBytesAsync(Marker); var written = File.GetLastWriteTimeUtc(Marker);
        Assert.Equal(new(1, 0, 1, 0), await RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default));
        await state.Output.AcknowledgeRemovalAsync(state.Native, default);
        Assert.Equal(original, await File.ReadAllBytesAsync(Marker)); Assert.Equal(written, File.GetLastWriteTimeUtc(Marker));
        Assert.False(File.Exists(Marker + ".pending"));
    }
    [Fact]
    public async Task ExactCompletedPendingAcknowledgementResumesTheRenameWithoutSealingMedia()
    {
        var state = await Started(); await state.Output.AcknowledgeRemovalAsync(state.Native, default);
        var original = await File.ReadAllBytesAsync(Marker); File.Move(Marker, Marker + ".pending");
        Assert.Equal(new(1, 1, 0, 0), await RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default));
        Assert.Equal(original, await File.ReadAllBytesAsync(Marker)); Assert.False(File.Exists(Marker + ".pending"));
        Assert.False(File.Exists(Path.Combine(Stage, "seal.json")));
    }
    [Fact]
    public async Task MissingNativeRemovalRemainsAwaitingAndDoesNotRetireOrAcknowledgeTheIntent()
    {
        var state = await Started(removed: false);
        Assert.Equal(new(1, 0, 0, 1), await RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default));
        Assert.Equal(state.Intent, Assert.Single(state.Native.ReadPending())); Assert.False(File.Exists(Marker));
    }
    [Fact]
    public async Task LegacyRootWithoutRecordedStagesIsUnchanged()
    {
        var native = new OwnershipJournal(_root);
        Assert.Equal(new(0, 0, 0, 0), await RecordedOutputJournal.RecoverRemovalsAsync(Outputs, native, default));
        Assert.False(Directory.Exists(Outputs));
    }
    [Theory]
    [InlineData("native-removal.json")]
    [InlineData("native-removal.json.pending")]
    public async Task CorruptAcknowledgementIsNeverOverwrittenOrBlessed(string file)
    {
        var state = await Started(); var path = Path.Combine(Stage, file); byte[] bad = [1, 2, 3]; await File.WriteAllBytesAsync(path, bad);
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default))).Code);
        Assert.Equal(bad, await File.ReadAllBytesAsync(path));
        if (file.EndsWith(".pending", StringComparison.Ordinal)) Assert.False(File.Exists(Marker));
    }
    [Theory]
    [InlineData("owner")]
    [InlineData("tenant")]
    [InlineData("generation")]
    [InlineData("unconfirmed")]
    [InlineData("schema")]
    public async Task ForeignOrUnconfirmedDurableRemovalCannotRepairAStage(string field)
    {
        var state = await Started(); var path = Path.Combine(state.Native.RemovalsDirectory, state.Intent.Owner.ToString("N") + ".json");
        var removal = JsonSerializer.Deserialize<NativeRemoval>(await File.ReadAllBytesAsync(path), ReaderWorkerWire.Json)!;
        removal = field switch
        {
            "owner" => removal with { Intent = removal.Intent with { Owner = Guid.NewGuid() } },
            "tenant" => removal with { Intent = removal.Intent with { Job = removal.Intent.Job with { OrganizationId = Guid.NewGuid() } } },
            "generation" => removal with { Intent = removal.Intent with { Job = removal.Intent.Job with { Generation = 2 } } },
            "unconfirmed" => removal with { ContainerAbsenceConfirmed = false },
            _ => removal with { Schema = "other" }
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(removal, ReaderWorkerWire.Json); await File.WriteAllBytesAsync(path, bytes);
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default))).Code);
        Assert.False(File.Exists(Marker)); Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task ChangedScopeDirectoryCannotSelectAHeaderFromAnotherAttempt()
    {
        var state = await Started(); var wrong = Stage + "0"; Directory.Move(Stage, wrong);
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default))).Code);
        Assert.False(File.Exists(Path.Combine(wrong, "native-removal.json")));
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("oversized")]
    [InlineData("directory")]
    public async Task InvalidStageInventoryCannotBeAcknowledged(string kind)
    {
        var state = await Started();
        if (kind == "directory") Directory.CreateDirectory(Marker);
        else await File.WriteAllBytesAsync(Path.Combine(Stage, kind == "unknown" ? "other.json" : "native-removal.json.pending"), new byte[kind == "unknown" ? 1 : 8193]);
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default))).Code);
        Assert.False(File.Exists(Marker));
    }
    [Fact]
    public async Task CancelledRecoveryCannotCopyAnAcknowledgement()
    {
        var state = await Started(); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, stop.Token));
        Assert.False(File.Exists(Marker));
    }
    [Fact]
    public async Task LocalMarkerCannotSubstituteForAMissingOwnershipJournalRecord()
    {
        var state = await Started(); await state.Output.AcknowledgeRemovalAsync(state.Native, default);
        var marker = await File.ReadAllBytesAsync(Marker); File.Delete(Path.Combine(state.Native.RemovalsDirectory, state.Intent.Owner.ToString("N") + ".json"));
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default))).Code);
        Assert.Equal(marker, await File.ReadAllBytesAsync(Marker));
    }
    [Fact]
    public async Task AnEmptyRecordedOutputRootDoesNotManufactureRecoveryWork()
    {
        var native = new OwnershipJournal(_root); Directory.CreateDirectory(Outputs);
        Assert.Equal(new(0, 0, 0, 0), await RecordedOutputJournal.RecoverRemovalsAsync(Outputs, native, default));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Outputs));
    }
    [Fact]
    public async Task EveryAttemptIsReconciledIndependentlyWithoutRetiringAnUnconfirmedSibling()
    {
        var state = await Started(); await state.Output.AcknowledgeRemovalAsync(state.Native, default);
        var second = Job with { JobId = Guid.NewGuid() }; var third = Job with { JobId = Guid.NewGuid() };
        var secondNative = await state.Native.BeginAsync(second with { RecordObservation = false, TraversalRoute = 0 }, "podman", Image, default);
        await RecordedOutputJournal.BeginAsync(Outputs, secondNative, second, Guid.NewGuid(), default);
        await state.Native.RecordRemovalAsync(secondNative, false, default);
        var thirdNative = await state.Native.BeginAsync(third with { RecordObservation = false, TraversalRoute = 0 }, "podman", Image, default);
        await RecordedOutputJournal.BeginAsync(Outputs, thirdNative, third, Guid.NewGuid(), default);
        Assert.Equal(new(3, 1, 1, 1), await RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default));
        Assert.Equal(thirdNative, Assert.Single(state.Native.ReadPending()));
        Assert.False(File.Exists(Path.Combine(Outputs, $"{third.OrganizationId:N}-{third.JobId:N}-1", "native-removal.json")));
    }
    [Fact]
    public async Task ParentCannotAcknowledgeAChangedHeaderEvenWhenItsDecodedFieldsAreEqual()
    {
        var state = await Started(); var path = Path.Combine(Stage, "start.json");
        await File.AppendAllTextAsync(path, " ");
        Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => state.Output.AcknowledgeRemovalAsync(state.Native, default))).Code);
        Assert.False(File.Exists(Marker));
    }
    [Theory]
    [InlineData("stage")]
    [InlineData("output-root")]
    [InlineData("native-removals")]
    public async Task LinkedDirectoriesCannotSupplyOrReceiveARecoveryAcknowledgement(string kind)
    {
        var state = await Started();
        var original = kind switch { "stage" => Stage, "output-root" => Outputs, _ => state.Native.RemovalsDirectory };
        var held = Path.Combine(_root, "held");
        foreach (var path in new[] { original, held }) Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, Path.GetFullPath(path));
        Directory.Move(original, held);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in new[] { "/c", "mklink", "/J", original, held }) start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!; await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(original, held);
            Assert.Equal("WORKER_OUTPUT_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => RecordedOutputJournal.RecoverRemovalsAsync(Outputs, state.Native, default))).Code);
            Assert.False(File.Exists(Marker));
        }
        finally { if (Directory.Exists(original)) Directory.Delete(original); Directory.Move(held, original); }
    }
}
