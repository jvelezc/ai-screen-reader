using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class RecordedPublicationRecoveryTests : IDisposable
{
    private static readonly FixtureJob Job = FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 };
    private static readonly Lazy<Task<byte[]>> Original = new(() => KeyboardFixtureWorker.RecordAsync(Job));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-publication-test-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _worker = Guid.NewGuid();
    private string Outputs => Path.Combine(_root, ".runtime", "recorded-output");
    private string Observation => Path.Combine(_root, Job.Reference.Replace('/', Path.DirectorySeparatorChar));
    public void Dispose()
    {
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(_root)));
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private async Task<RecordedOutputSnapshot> Snapshot(bool acknowledge = true)
    {
        var native = new OwnershipJournal(Path.Combine(_root, ".runtime"));
        var intent = await native.BeginAsync(Job with { RecordObservation = false, TraversalRoute = 0 }, "podman", "sha256:" + new string('a', 64), default);
        var output = await RecordedOutputJournal.BeginAsync(Outputs, intent, Job, _worker, default);
        await output.SealAsync(await Original.Value, _ => Task.FromResult(true), default);
        if (acknowledge) { await native.RecordRemovalAsync(intent, true, default); await output.AcknowledgeRemovalAsync(native, default); }
        return RecordedOutputJournal.ReadCandidate(Outputs, Job.OrganizationId, Job.JobId, Job.Generation, default);
    }
    private sealed class Publications : IRecordedPublications
    {
        public string State { get; init; } = "Claimed";
        public RetainedRecordingOutput? Substitute { get; init; }
        public Func<int, CancellationToken, Task<bool>>? OnRenew { get; init; }
        public bool Completion { get; init; } = true;
        public bool ThrowCompletion { get; init; }
        public bool ThrowClaim { get; init; }
        public Action? DuringClaim { get; init; }
        public int Claims { get; private set; }
        public int Renews { get; private set; }
        public int Completions { get; private set; }
        public int Failures { get; private set; }
        public StoredObservation? Stored { get; private set; }
        public string? Failure { get; private set; }
        public Task<RecordedPublicationClaim> ClaimAsync(RetainedRecordingOutput output, CancellationToken token)
        {
            Claims++; if (ThrowClaim) throw new IOException("private-database-detail"); DuringClaim?.Invoke();
            return Task.FromResult(new RecordedPublicationClaim(State, State == "Claimed" ? new(Substitute ?? output, Guid.NewGuid()) : null));
        }
        public Task<bool> RenewAsync(RecordedPublicationLease lease, CancellationToken token)
        { Renews++; return OnRenew?.Invoke(Renews, token) ?? Task.FromResult(true); }
        public Task<bool> CompleteAsync(RecordedPublicationLease lease, StoredObservation stored, CancellationToken token)
        {
            Completions++; Stored = stored; stored.WorkerArchive!.Validate(lease.Output.Job, lease.Output.WorkerId);
            if (ThrowCompletion) throw new IOException("private-database-detail"); return Task.FromResult(Completion);
        }
        public Task<bool> FailAsync(RecordedPublicationLease lease, string code, CancellationToken token)
        { Failures++; Failure = code; return Task.FromResult(true); }
    }
    private sealed class Objects : ITraversalObjectStore
    {
        public Func<CancellationToken, Task>? BeforeWrite { get; init; }
        public bool Fail { get; init; }
        public int Writes { get; private set; }
        private readonly Dictionary<string, byte[]> _bytes = [];
        public Task<byte[]?> ReadAsync(TraversalArchiveIdentity identity, string? part, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(_bytes.TryGetValue(identity.IndexSha256 + part, out var bytes) ? bytes.ToArray() : null); }
        public async Task WriteAsync(TraversalArchiveIdentity identity, string? part, byte[] bytes, CancellationToken token)
        {
            Writes++; if (BeforeWrite is not null) await BeforeWrite(token);
            if (Fail) throw new TraversalArchiveException(TraversalArchiveFailure.Unavailable);
            _bytes[identity.IndexSha256 + part] = bytes.ToArray();
        }
    }
    private ObservationStore Store(ITraversalObjectStore objects) => new(_root, producer: new(new TraversalArchive(objects)));
    private RecordedPublicationRecovery Recovery(Publications publications, Objects objects, TimeProvider? time = null) =>
        new(publications, Store(objects), time ?? TimeProvider.System);

    [Fact]
    public async Task ActualChromiumOriginalCanPublishThroughStorageOnlyAuthorityWithoutNewInputOrChangedProducer()
    {
        var snapshot = await Snapshot(); var publications = new Publications(); var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("Published", result.State); Assert.True(result.IsCompletionReceipt); Assert.False(result.CanReplay);
        Assert.Equal(0, result.InputsDispatchedByRecovery); Assert.Equal("NotAssessed", result.GoalVerification);
        Assert.Equal(Job, result.Job); Assert.Equal(_worker, result.Producer); Assert.Equal(snapshot.Seal.BundleSha256, result.BundleSha256);
        Assert.Equal(KeyboardRecordingBundle.Decode(snapshot.Bundle, Job).Observation, await File.ReadAllBytesAsync(Observation));
        Assert.Equal(Job.Reference, result.Reference); Assert.Equal(1, publications.Completions); Assert.Equal(0, publications.Failures);
        Assert.True(objects.Writes > 0); Assert.Equal(await Original.Value, RecordedOutputJournal.Read(Outputs, Job, _worker, default).Bundle);
        Assert.Equal(_worker, publications.Stored!.WorkerArchive!.WorkerId);
    }
    [Theory]
    [InlineData("AlreadyCompleted")]
    [InlineData("Rejected")]
    [InlineData("Busy")]
    public async Task NonclaimsDoNotWriteStorageOrRenewInput(string state)
    {
        var snapshot = await Snapshot(); var publications = new Publications { State = state }; var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal(state, result.State); Assert.False(result.IsCompletionReceipt); Assert.False(result.CanReplay);
        Assert.Equal(0, publications.Renews); Assert.Equal(0, publications.Completions); Assert.Equal(0, objects.Writes); Assert.False(File.Exists(Observation));
    }
    [Fact]
    public async Task UnconfirmedCleanupCannotAcquirePublicationAuthority()
    {
        var snapshot = await Snapshot(false); var publications = new Publications(); var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("WORKER_PUBLICATION_CLEANUP_UNCONFIRMED", result.ErrorCode); Assert.Equal(0, publications.Claims);
        Assert.Equal(0, objects.Writes); Assert.False(File.Exists(Observation));
    }
    [Fact]
    public async Task ClaimCannotSubstituteTheOriginalProducer()
    {
        var snapshot = await Snapshot(); var descriptor = RecordedOutputProjection.Manifest(snapshot, Job, _worker)!;
        var publications = new Publications { Substitute = descriptor with { WorkerId = Guid.NewGuid() } }; var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("WORKER_PUBLICATION_SCOPE_UNCONFIRMED", result.ErrorCode); Assert.Equal(0, publications.Renews); Assert.Equal(0, objects.Writes);
    }
    [Fact]
    public async Task DeniedInitialFenceDoesNotCreateLocalOrRemotePublication()
    {
        var snapshot = await Snapshot(); var publications = new Publications { OnRenew = (_, _) => Task.FromResult(false) }; var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("WORKER_PUBLICATION_FENCE_UNCONFIRMED", result.ErrorCode); Assert.Equal(0, publications.Completions);
        Assert.Equal(0, objects.Writes); Assert.False(File.Exists(Observation));
    }
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task AuthorityIsRequiredBeforeAndAfterEveryStorageFinalizationBoundary(int denied)
    {
        var snapshot = await Snapshot(); var publications = new Publications { OnRenew = (call, _) => Task.FromResult(call != denied) }; var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("Failed", result.State); Assert.Equal("WORKER_PUBLICATION_FENCE_UNCONFIRMED", result.ErrorCode);
        Assert.Equal(1, publications.Failures); Assert.Equal(0, publications.Completions); Assert.False(result.IsCompletionReceipt);
        Assert.Equal(denied >= 3, File.Exists(Observation)); Assert.Equal(denied == 5, objects.Writes > 0);
    }
    [Fact]
    public async Task ExactExistingObservationAndArchiveCanBeReusedButNormalPersistenceStillCannotReplaceThem()
    {
        var snapshot = await Snapshot(); var objects = new Objects(); var store = Store(objects);
        var first = await store.PersistAsync(snapshot.Bundle, Job, default, _worker); var original = await File.ReadAllBytesAsync(Observation);
        await Assert.ThrowsAsync<IOException>(() => store.PersistAsync(snapshot.Bundle, Job, default, _worker));
        var recovered = await store.RecoverAsync(snapshot, _ => Task.FromResult(true), default);
        Assert.Equal(first, recovered); Assert.Equal(original, await File.ReadAllBytesAsync(Observation));
        Assert.Equal(first.WorkerArchive, recovered.WorkerArchive); Assert.Equal(snapshot.Bundle, RecordedOutputJournal.Read(Outputs, Job, _worker, default).Bundle);
    }
    [Theory]
    [InlineData("changed")]
    [InlineData("length")]
    [InlineData("directory")]
    public async Task RecoveryCannotReplaceDifferentExistingObservationData(string kind)
    {
        var snapshot = await Snapshot(); Directory.CreateDirectory(Path.GetDirectoryName(Observation)!);
        var bytes = KeyboardRecordingBundle.Decode(snapshot.Bundle, Job).Observation.ToArray(); bytes[0] ^= 1;
        if (kind == "length") bytes = bytes[..^1];
        if (kind == "directory") Directory.CreateDirectory(Observation); else await File.WriteAllBytesAsync(Observation, bytes);
        var publications = new Publications(); var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("Failed", result.State); Assert.Equal(0, publications.Completions); Assert.Equal(0, objects.Writes);
        if (kind != "directory") Assert.Equal(bytes, await File.ReadAllBytesAsync(Observation)); else Assert.True(Directory.Exists(Observation));
    }
    [Fact]
    public async Task StorageFailureRetainsOriginalsAndVisibleFailureWithoutCompletion()
    {
        var snapshot = await Snapshot(); var publications = new Publications(); var objects = new Objects { Fail = true };
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("Failed", result.State); Assert.Equal("WORKER_PUBLICATION_STORAGE_UNCONFIRMED", publications.Failure);
        Assert.Equal(0, publications.Completions); Assert.False(result.IsCompletionReceipt); Assert.Equal(await Original.Value, snapshot.Bundle);
        Assert.Equal(KeyboardRecordingBundle.Decode(snapshot.Bundle, Job).Observation, await File.ReadAllBytesAsync(Observation));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrittenOriginalsCannotSubstituteForAnAcceptedDatabaseCompletion(bool throws)
    {
        var snapshot = await Snapshot(); var publications = new Publications { Completion = false, ThrowCompletion = throws }; var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("Unconfirmed", result.State); Assert.Equal("WORKER_PUBLICATION_COMPLETION_UNCONFIRMED", result.ErrorCode);
        Assert.False(result.IsCompletionReceipt); Assert.Equal(Job.Reference, result.Reference); Assert.True(objects.Writes > 0);
        Assert.DoesNotContain("private-database-detail", JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task ClaimFailureCannotExposePrivateDatabaseDetailsOrWriteStorage()
    {
        var snapshot = await Snapshot(); var publications = new Publications { ThrowClaim = true }; var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("WORKER_PUBLICATION_CLAIM_UNCONFIRMED", result.ErrorCode); Assert.Equal(0, objects.Writes);
        Assert.DoesNotContain("private-database-detail", JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task CancellationJoinsActualStorageBeforeRecordingSafeFailure()
    {
        var snapshot = await Snapshot(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var objects = new Objects { BeforeWrite = _ => { entered.TrySetResult(); return release.Task; } };
        var publications = new Publications(); using var stop = new CancellationTokenSource();
        var actual = Recovery(publications, objects).RunAsync(snapshot, stop.Token); await entered.Task;
        await stop.CancelAsync(); Assert.False(actual.IsCompleted); Assert.Equal(0, publications.Failures); Assert.Equal(0, publications.Completions);
        release.SetResult(); var result = await actual;
        Assert.Equal("Failed", result.State); Assert.Equal("WORKER_PUBLICATION_STOP_REQUESTED", result.ErrorCode);
        Assert.Equal(1, publications.Failures); Assert.Equal(0, publications.Completions);
    }
    [Fact]
    public async Task FiveSecondHeartbeatMustSettleBeforePublicationEvenWhenStorageReturns()
    {
        var snapshot = await Snapshot(); var time = new FakeTimeProvider();
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRenew = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var objects = new Objects { BeforeWrite = _ => { writing.TrySetResult(); return releaseWrite.Task; } };
        var publications = new Publications { OnRenew = (call, _) => { if (call == 6) finalized.SetResult(); if (call != 5) return Task.FromResult(true); renewing.SetResult(); return releaseRenew.Task; } };
        var actual = Recovery(publications, objects, time).RunAsync(snapshot, default); await writing.Task;
        Assert.Equal(4, publications.Renews); time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.Equal(4, publications.Renews); Assert.False(actual.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1)); await renewing.Task; releaseWrite.SetResult(); await finalized.Task;
        Assert.False(actual.IsCompleted); Assert.Equal(0, publications.Completions);
        releaseRenew.SetResult(true); Assert.Equal("Published", (await actual).State); Assert.Equal(1, publications.Completions);
    }
    [Theory]
    [InlineData("org")]
    [InlineData("job")]
    [InlineData("generation")]
    public async Task PrivateCandidateSelectorsCannotSelectAnotherAttempt(string field)
    {
        await Snapshot();
        Assert.ThrowsAny<IOException>(() => RecordedOutputJournal.ReadCandidate(Outputs, field == "org" ? Guid.NewGuid() : Job.OrganizationId,
            field == "job" ? Guid.NewGuid() : Job.JobId, field == "generation" ? 2 : 1, default));
        Assert.Equal(_worker, RecordedOutputJournal.ReadCandidate(Outputs, Job.OrganizationId, Job.JobId, 1, default).Header.WorkerId);
    }
    [Fact]
    public async Task PublicationCapabilityAndResultDoNotSerializeASecretOrReplayAuthority()
    {
        var snapshot = await Snapshot(); var token = Guid.NewGuid();
        var lease = new RecordedPublicationLease(RecordedOutputProjection.Manifest(snapshot, Job, _worker)!, token);
        Assert.DoesNotContain(token.ToString(), JsonSerializer.Serialize(lease)); Assert.DoesNotContain(token.ToString(), lease.ToString());
        Assert.Throws<ArgumentException>(() => new RecordedPublicationLease(lease.Output, Guid.Empty));
        Assert.Throws<ArgumentNullException>(() => { _ = Store(new Objects()).RecoverAsync(snapshot, null!, default); });
    }
    [Fact]
    public async Task SourceChangedAfterClaimCannotReachLocalOrRemoteFinalization()
    {
        var snapshot = await Snapshot(); var publications = new Publications { DuringClaim = () => snapshot.Bundle[^1] ^= 1 }; var objects = new Objects();
        var result = await Recovery(publications, objects).RunAsync(snapshot, default);
        Assert.Equal("Failed", result.State); Assert.Equal(1, publications.Failures); Assert.Equal(0, publications.Completions);
        Assert.False(File.Exists(Observation)); Assert.Equal(0, objects.Writes);
        Assert.Equal(await Original.Value, RecordedOutputJournal.Read(Outputs, Job, _worker, default).Bundle);
    }
    [Fact]
    public async Task LostHeartbeatDoesNotReleaseOwnershipBeforeActualStorageSettles()
    {
        var snapshot = await Snapshot(); var time = new FakeTimeProvider();
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publications = new Publications { OnRenew = (call, _) => { if (call != 5) return Task.FromResult(true); lost.SetResult(); return Task.FromResult(false); } };
        var objects = new Objects { BeforeWrite = _ => { writing.TrySetResult(); return release.Task; } };
        var actual = Recovery(publications, objects, time).RunAsync(snapshot, default); await writing.Task;
        time.Advance(TimeSpan.FromSeconds(5)); await lost.Task;
        Assert.False(actual.IsCompleted); Assert.Equal(0, publications.Completions); Assert.Equal(0, publications.Failures);
        release.SetResult(); var result = await actual;
        Assert.Equal("Unconfirmed", result.State); Assert.Equal("WORKER_PUBLICATION_FENCE_UNCONFIRMED", result.ErrorCode);
        Assert.Equal(0, publications.Completions); Assert.Equal(0, publications.Failures);
    }
    [Fact]
    public async Task RecoveryDirectoryCannotFollowALinkOutsideTheOriginalObservationPath()
    {
        var snapshot = await Snapshot(); var parent = Path.GetDirectoryName(Observation)!; var held = Path.Combine(_root, "held");
        Directory.CreateDirectory(Path.GetDirectoryName(parent)!); Directory.CreateDirectory(held);
        foreach (var path in new[] { parent, held }) Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, Path.GetFullPath(path));
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in new[] { "/c", "mklink", "/J", parent, held }) start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!; await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(parent, held);
            var publications = new Publications(); var objects = new Objects(); var result = await Recovery(publications, objects).RunAsync(snapshot, default);
            Assert.Equal("Failed", result.State); Assert.Equal("WORKER_PUBLICATION_PATH_UNCONFIRMED", result.ErrorCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(held)); Assert.Equal(0, objects.Writes); Assert.Equal(0, publications.Completions);
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent); }
    }
    [Theory]
    [InlineData("Host=example.invalid;Username=disposable;Password=disposable;SSL Mode=Prefer")]
    [InlineData("Host=127.0.0.1;Username=disposable")]
    [InlineData("Host=127.0.0.1;Password=disposable")]
    public void PublicationDatabaseRequiresCredentialsAndVerifiedExternalTls(string connection)
    {
        Assert.Equal("WORKER_DATABASE_CONFIGURATION_INVALID", Assert.Throws<ArgumentException>(() => new PostgresRecordedPublications(connection)).Message);
    }
}
