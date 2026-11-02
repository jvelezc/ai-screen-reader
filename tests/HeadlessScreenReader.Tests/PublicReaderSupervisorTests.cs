using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReaderSupervisorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-public-supervisor-" + Guid.NewGuid().ToString("N"));
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private string Scripts => Path.Combine(_root, "scripts");
    private string Store => Path.Combine(_root, "store");
    private PublicReaderOwnershipJournal Journal => new(Path.Combine(Store, ".public-reader-runtime"));
    private void ScriptsPresent()
    {
        Directory.CreateDirectory(Scripts);
        foreach (var file in new[] { "Start-IsolatedPublicReader.ps1", "Stop-IsolatedPublicReader.ps1", "ReaderIsolation.psm1" })
            File.WriteAllText(Path.Combine(Scripts, file), "throw 'UNIT_TEST_MUST_NOT_START_NATIVE_PROCESS'");
    }

    public void Dispose()
    {
        const string prefix = "hsr-public-supervisor-";
        var resolved = Path.GetFullPath(_root); var name = Path.GetFileName(resolved);
        if (resolved != _root || !name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new InvalidOperationException("TEST_DIRECTORY_SCOPE_INVALID");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }

    [Theory]
    [InlineData("nerdctl", Image)]
    [InlineData("PODMAN", Image)]
    [InlineData("podman", "latest")]
    [InlineData("podman", "sha256:abc")]
    [InlineData("docker", "sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task InvalidRuntimeOrMutableImageCannotCreateAJournal(string runtime, string image)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => IsolatedPublicReaderSupervisor.StartAsync(Store, Scripts, runtime, image));
        Assert.False(Directory.Exists(Store));
    }

    [Theory]
    [InlineData("Start-IsolatedPublicReader.ps1")]
    [InlineData("Stop-IsolatedPublicReader.ps1")]
    [InlineData("ReaderIsolation.psm1")]
    public async Task EachTrustedLauncherFileIsRequiredBeforeOwnershipIsCreated(string missing)
    {
        ScriptsPresent(); File.Delete(Path.Combine(Scripts, missing));
        Assert.Equal("PUBLIC_READER_LAUNCHER_REQUIRED", (await Assert.ThrowsAsync<ArgumentException>(() =>
            IsolatedPublicReaderSupervisor.StartAsync(Store, Scripts, "podman", Image))).Message);
        Assert.False(Directory.Exists(Store));
    }

    [Fact]
    public async Task EmptySupervisorRetainsExclusiveOwnershipUntilDisposeAndRejectsUseAfterClose()
    {
        ScriptsPresent(); var supervisor = await IsolatedPublicReaderSupervisor.StartAsync(Store, Scripts, "podman", Image);
        try
        {
            Assert.Equal(0, supervisor.Recovered); Assert.Empty(Journal.ReadPending());
            await Assert.ThrowsAsync<IOException>(() => IsolatedPublicReaderSupervisor.StartAsync(Store, Scripts, "podman", Image));
            Assert.Null(await supervisor.RetireAsync(Guid.NewGuid(), Guid.NewGuid()));
        }
        finally { await supervisor.DisposeAsync(); }
        Assert.Equal("PUBLIC_READER_SUPERVISOR_CLOSED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            supervisor.OpenAsync(Guid.NewGuid(), Guid.NewGuid(), PublicReaderWorkerContractTests.Source()))).Code);
        Assert.Equal("PUBLIC_READER_SUPERVISOR_CLOSED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            supervisor.RetireAsync(Guid.NewGuid(), Guid.NewGuid()))).Code);
        await using var restart = await IsolatedPublicReaderSupervisor.StartAsync(Store, Scripts, "podman", Image);
        Assert.Equal(0, restart.Recovered);
    }

    [Fact]
    public async Task CancelledAdmissionCreatesNoIntentAndInvalidIdentityIsRejected()
    {
        ScriptsPresent(); await using var supervisor = await IsolatedPublicReaderSupervisor.StartAsync(Store, Scripts, "podman", Image);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => supervisor.OpenAsync(Guid.NewGuid(), Guid.NewGuid(), PublicReaderWorkerContractTests.Source(), cancelled.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => supervisor.OpenAsync(Guid.Empty, Guid.NewGuid(), PublicReaderWorkerContractTests.Source()));
        await Assert.ThrowsAsync<ArgumentException>(() => supervisor.OpenAsync(Guid.NewGuid(), Guid.Empty, PublicReaderWorkerContractTests.Source()));
        await Assert.ThrowsAsync<ArgumentException>(() => supervisor.RetireAsync(Guid.Empty, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => supervisor.RetireAsync(Guid.NewGuid(), Guid.Empty));
        Assert.Empty(Journal.ReadPending()); Assert.Empty(Directory.GetFiles(Journal.RemovalsDirectory));
    }

    [Fact]
    public async Task ForeignTenantCannotRetireAnExistingDurableIntent()
    {
        ScriptsPresent(); await using var supervisor = await IsolatedPublicReaderSupervisor.StartAsync(Store, Scripts, "podman", Image);
        var intent = await Journal.BeginAsync(Guid.NewGuid(), Guid.NewGuid(), PublicReaderWorkerContractTests.Source(), "podman", Image);
        var path = Path.Combine(Journal.IntentsDirectory, intent.Owner.ToString("N") + ".json");
        var before = await File.ReadAllBytesAsync(path);
        try
        {
            Assert.Equal("PUBLIC_READER_SCOPE_DENIED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
                supervisor.RetireAsync(Guid.NewGuid(), intent.SessionId))).Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Empty(Directory.GetFiles(Journal.RemovalsDirectory));
        }
        finally { File.Delete(path); } // Test authored only metadata; no native process was launched.
    }

    [Theory]
    [InlineData("foreign-runtime")]
    [InlineData("partial-entry")]
    public async Task CompleteInventoryAndRuntimeAreValidatedBeforeAnyRecovery(string scenario)
    {
        ScriptsPresent(); var journal = Journal;
        var good = await journal.BeginAsync(Guid.NewGuid(), Guid.NewGuid(), PublicReaderWorkerContractTests.Source(), "podman", Image);
        var path = Path.Combine(journal.IntentsDirectory, good.Owner.ToString("N") + ".json"); var before = await File.ReadAllBytesAsync(path);
        if (scenario == "partial-entry") await File.WriteAllTextAsync(Path.Combine(journal.IntentsDirectory, Guid.NewGuid().ToString("N") + ".json"), "{");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => IsolatedPublicReaderSupervisor.StartAsync(Store, Scripts,
            scenario == "foreign-runtime" ? "docker" : "podman", Image));
        Assert.Equal(scenario == "foreign-runtime" ? "PUBLIC_READER_RECOVERY_RUNTIME_MISMATCH" : "PUBLIC_READER_OWNERSHIP_INVALID", error.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
        using var unlocked = journal.AcquireExclusive();
    }
}
