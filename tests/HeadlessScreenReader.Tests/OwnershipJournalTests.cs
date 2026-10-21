using System.Text.Json;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class OwnershipJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-ownership-" + Guid.NewGuid().ToString("N"));
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private OwnershipJournal Create() => new(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task DurableIntentBindsExactOwnerJobRuntimeAndImmutableImageWithoutCapabilities()
    {
        var journal = Create(); var job = FixtureWorkerTests.Job();
        var intent = await journal.BeginAsync(job, "podman", Image, CancellationToken.None);
        Assert.Equal(new[] { intent }, journal.ReadPending()); Assert.Equal(job, intent.Job);
        Assert.Equal("hsr-observation-" + intent.Owner.ToString("N"), intent.ContainerName);
        var path = Assert.Single(Directory.GetFiles(journal.IntentsDirectory));
        Assert.Equal(intent.Owner.ToString("N") + ".json", Path.GetFileName(path));
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"Runtime\":\"podman\"", json); Assert.Contains("\"Image\":\"" + Image + "\"", json);
        Assert.DoesNotContain("Token", json); Assert.DoesNotContain("Password", json); Assert.DoesNotContain("HSR_WORKER_DATABASE", json);
    }
    [Fact]
    public void ExclusiveWorkerLockRemainsHeldUntilActualOwnerDisposes()
    {
        var journal = Create();
        var owner = journal.AcquireExclusive();
        Assert.Throws<IOException>(() => journal.AcquireExclusive());
        owner.Dispose();
        using var replacement = journal.AcquireExclusive();
        Assert.True(replacement.CanWrite);
    }
    [Fact]
    public async Task ConfirmedRemovalIsDurableBeforeIntentRetirementAndExactRetryPreservesOriginalFact()
    {
        var journal = Create(); var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        await journal.RecordRemovalAsync(intent, true, CancellationToken.None);
        Assert.Empty(journal.ReadPending());
        var path = Assert.Single(Directory.GetFiles(journal.RemovalsDirectory)); var original = await File.ReadAllBytesAsync(path);
        var removal = JsonSerializer.Deserialize<NativeRemoval>(original, ObservationStore.Json)!;
        Assert.Equal(intent, removal.Intent); Assert.True(removal.ContainerAbsenceConfirmed); Assert.True(removal.RecoveredAfterRestart);
        Assert.Equal("hsr-native-removal-v1", removal.Schema);
        Assert.DoesNotContain("GoalVerified", System.Text.Encoding.UTF8.GetString(original));
        await journal.RecordRemovalAsync(intent, false, CancellationToken.None);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }
    [Theory]
    [InlineData("schema")]
    [InlineData("owner")]
    [InlineData("runtime")]
    [InlineData("image")]
    [InlineData("job")]
    public async Task InvalidJournalIdentityCannotAuthorizeAnyRecovery(string field)
    {
        var journal = Create(); var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        var invalid = field switch
        {
            "schema" => intent with { Schema = "other" },
            "owner" => intent with { Owner = Guid.Empty },
            "runtime" => intent with { Runtime = "shell" },
            "image" => intent with { Image = "latest" },
            _ => intent with { Job = intent.Job with { Generation = 4 } }
        };
        await File.WriteAllBytesAsync(Path.Combine(journal.IntentsDirectory, intent.Owner.ToString("N") + ".json"), JsonSerializer.SerializeToUtf8Bytes(invalid));
        if (field == "job") Assert.Throws<HeadlessScreenReader.Domain.BrowserOperationException>(() => journal.ReadPending());
        else Assert.Equal("WORKER_NATIVE_INTENT_INVALID", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
        Assert.Single(Directory.GetFiles(journal.IntentsDirectory)); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(8193)]
    public void InvalidJournalSizeFailsBeforeAnyRuntimeOperation(int size)
    {
        var journal = Create(); File.WriteAllBytes(Path.Combine(journal.IntentsDirectory, Guid.NewGuid().ToString("N") + ".json"), new byte[size]);
        Assert.Equal("WORKER_NATIVE_RECORD_SIZE_INVALID", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
        Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }
    [Theory]
    [InlineData("malformed")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    public async Task IncompleteOrAmbiguousIntentRemainsUnconfirmed(string kind)
    {
        var journal = Create(); var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        var path = Path.Combine(journal.IntentsDirectory, intent.Owner.ToString("N") + ".json");
        var original = await File.ReadAllTextAsync(path);
        var changed = kind switch
        {
            "malformed" => "{\"Owner\":",
            "unknown-field" => original.Replace("{", "{\"Token\":\"not-a-capability\",", StringComparison.Ordinal),
            _ => original.Replace("\"Runtime\":\"podman\"", "\"Runtime\":\"docker\",\"Runtime\":\"podman\"", StringComparison.Ordinal)
        };
        await File.WriteAllTextAsync(path, changed);
        Assert.Throws<JsonException>(() => journal.ReadPending()); Assert.True(File.Exists(path));
    }
    [Fact]
    public async Task FileNonceMustMatchRetainedOwnerRatherThanAuthorizingAChangedContainerName()
    {
        var journal = Create(); var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        File.Move(Path.Combine(journal.IntentsDirectory, intent.Owner.ToString("N") + ".json"), Path.Combine(journal.IntentsDirectory, Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal("WORKER_NATIVE_INTENT_IDENTITY_MISMATCH", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
    }
    [Theory]
    [InlineData("job")]
    [InlineData("schema")]
    [InlineData("absence")]
    public async Task FailedRemovalAcknowledgementRetainsIntentRatherThanAllowingSilentRetry(string field)
    {
        var journal = Create(); var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        var path = Path.Combine(journal.RemovalsDirectory, intent.Owner.ToString("N") + ".json");
        var conflicting = new NativeRemoval(field == "schema" ? "other" : "hsr-native-removal-v1",
            field == "job" ? intent with { Job = FixtureWorkerTests.Job() } : intent, DateTimeOffset.UtcNow, field != "absence", true);
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(conflicting, ObservationStore.Json));
        Assert.Equal("WORKER_NATIVE_REMOVAL_IDENTITY_MISMATCH", (await Assert.ThrowsAsync<InvalidDataException>(() => journal.RecordRemovalAsync(intent, true, CancellationToken.None))).Message);
        Assert.Equal(new[] { intent }, journal.ReadPending());
        Assert.Equal(conflicting.Intent.Job, JsonSerializer.Deserialize<NativeRemoval>(await File.ReadAllBytesAsync(path), ObservationStore.Json)!.Intent.Job);
    }
    [Fact]
    public async Task MaximumIntentSizeStillPreservesActualBinding()
    {
        var journal = Create(); var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        var path = Assert.Single(Directory.GetFiles(journal.IntentsDirectory)); var original = await File.ReadAllBytesAsync(path);
        var padded = new byte[8192]; Array.Fill(padded, (byte)' '); original.CopyTo(padded, 0);
        await File.WriteAllBytesAsync(path, padded);
        Assert.Equal(new[] { intent }, journal.ReadPending());
    }
    [Fact]
    public async Task ChangedRuntimeFailsClosedBeforeAnyResourceCanBeRetired()
    {
        var journal = Create(); var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "docker", Image, CancellationToken.None);
        var executor = new IsolatedFixtureExecutor("podman", Image, journal: journal);
        Assert.Equal("WORKER_RECOVERY_RUNTIME_MISMATCH", (await Assert.ThrowsAsync<HeadlessScreenReader.Domain.BrowserOperationException>(() => executor.RecoverAsync())).Code);
        Assert.Equal(new[] { intent }, journal.ReadPending()); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }
    [Fact]
    public async Task UnexpectedFileTypeCannotBecomeAResourceAuthorization()
    {
        var journal = Create(); var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        var path = Path.Combine(journal.IntentsDirectory, intent.Owner.ToString("N") + ".json");
        File.Move(path, Path.ChangeExtension(path, ".txt"));
        Assert.Equal("WORKER_NATIVE_INTENT_INVALID", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
        Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }
    [Fact]
    public void NullIntentCannotSilentlyBecomeAnEmptyRecoveryInventory()
    {
        var journal = Create(); File.WriteAllText(Path.Combine(journal.IntentsDirectory, Guid.NewGuid().ToString("N") + ".json"), "null");
        Assert.Equal("WORKER_NATIVE_RECORD_INVALID", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
        Assert.Single(Directory.GetFiles(journal.IntentsDirectory)); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }
}
