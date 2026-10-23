using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class IsolatedReaderSupervisorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-reader-supervisor-" + Guid.NewGuid().ToString("N"));
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task SeparateReaderJournalRetainsItsOwnExclusiveLockWithoutAcquiringQueueOwnership()
    {
        var queue = new OwnershipJournal(Path.Combine(_root, ".runtime"));
        using var queueLock = queue.AcquireExclusive();
        var queueIntent = await queue.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        await using (var reader = await IsolatedReaderSupervisor.StartAsync(_root, "podman", Image))
        {
            Assert.Equal(0, reader.Recovered);
            Assert.Equal(Path.Combine(_root, ".reader-runtime"), reader.JournalRoot);
            Assert.Equal(queueIntent, Assert.Single(queue.ReadPending()));
            await Assert.ThrowsAsync<IOException>(() => IsolatedReaderSupervisor.StartAsync(_root, "podman", Image));
        }
        await using var replacement = await IsolatedReaderSupervisor.StartAsync(_root, "podman", Image);
        Assert.Equal(0, replacement.Recovered);
        Assert.Equal(queueIntent, Assert.Single(queue.ReadPending()));
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("mutable-image")]
    [InlineData("docker-without-seccomp")]
    public async Task InvalidConfigurationReleasesReaderLockAndCannotCreateNativeIntent(string scenario)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => IsolatedReaderSupervisor.StartAsync(_root,
            scenario == "runtime" ? "shell" : scenario == "docker-without-seccomp" ? "docker" : "podman",
            scenario == "mutable-image" ? "latest" : Image));
        await using var valid = await IsolatedReaderSupervisor.StartAsync(_root, "podman", Image);
        Assert.Empty(new OwnershipJournal(valid.JournalRoot).ReadPending());
    }

    [Fact]
    public async Task CanceledAndInvalidAdmissionNeverLaunchOrCreateIntentAndClosedSupervisorRejectsOpen()
    {
        var supervisor = await IsolatedReaderSupervisor.StartAsync(_root, "podman", Image);
        try
        {
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => supervisor.OpenAsync(Guid.NewGuid(), Guid.NewGuid(), canceled.Token));
            await Assert.ThrowsAsync<ArgumentException>(() => supervisor.OpenAsync(Guid.Empty, Guid.NewGuid()));
            await Assert.ThrowsAsync<ArgumentException>(() => supervisor.OpenAsync(Guid.NewGuid(), Guid.Empty));
            Assert.Empty(new OwnershipJournal(supervisor.JournalRoot).ReadPending());
            await supervisor.DisposeAsync();
            Assert.Equal("READER_SUPERVISOR_CLOSED",
                (await Assert.ThrowsAsync<BrowserOperationException>(() => supervisor.OpenAsync(Guid.NewGuid(), Guid.NewGuid()))).Code);
        }
        finally { await supervisor.DisposeAsync(); }
    }

    [Fact]
    public async Task InvalidRecoveryInventoryFailsBeforeRuntimeUseAndReleasesTheReaderLock()
    {
        var journal = new OwnershipJournal(Path.Combine(_root, ".reader-runtime"));
        var intent = await journal.BeginAsync(FixtureWorkerTests.Job(), "podman", Image, CancellationToken.None);
        var path = Path.Combine(journal.IntentsDirectory, intent.Owner.ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(intent with { Runtime = "shell" }, ObservationStore.Json));
        await Assert.ThrowsAsync<InvalidDataException>(() => IsolatedReaderSupervisor.StartAsync(_root, "podman", Image));
        Assert.True(File.Exists(path));
        using var exclusive = journal.AcquireExclusive();
        Assert.True(exclusive.CanWrite);
    }

    private static (JsonObject Item, NativeOwnership Intent) Inspection()
    {
        var job = FixtureWorkerTests.Job() with { Generation = 1 };
        var intent = new NativeOwnership(NativeOwnership.CurrentSchema, Guid.NewGuid(), job, "podman", Image);
        var item = new JsonObject
        {
            ["Image"] = Image,
            ["Config"] = new JsonObject
            {
                ["Entrypoint"] = new JsonArray("dotnet"),
                ["Cmd"] = new JsonArray("/app/cli/HeadlessScreenReader.Cli.dll", "--reader-fixture"),
                ["Labels"] = new JsonObject
                {
                    ["hsr.observation.organization"] = job.OrganizationId.ToString("N"),
                    ["hsr.observation.scan"] = job.ScanId.ToString("N"),
                    ["hsr.observation.job"] = job.JobId.ToString("N"),
                    ["hsr.observation.generation"] = "1"
                }
            }
        };
        return (item, intent);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("valid-string-entrypoint")]
    [InlineData("string-extra-entrypoint")]
    [InlineData("null-entrypoint")]
    [InlineData("image")]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation")]
    [InlineData("entrypoint")]
    [InlineData("extra-entrypoint")]
    [InlineData("command")]
    [InlineData("extra-command")]
    public void ReaderInspectionPinsExactImmutableImageOwnershipAndFixedEntryPoint(string scenario)
    {
        var (item, intent) = Inspection();
        var config = item["Config"]!;
        if (scenario == "valid-string-entrypoint") config["Entrypoint"] = "dotnet";
        else if (scenario == "string-extra-entrypoint") config["Entrypoint"] = "dotnet --extra";
        else if (scenario == "null-entrypoint") config["Entrypoint"] = null;
        else if (scenario == "image") item["Image"] = "sha256:" + new string('b', 64);
        else if (scenario == "entrypoint") config["Entrypoint"] = new JsonArray("/bin/sh");
        else if (scenario == "extra-entrypoint") config["Entrypoint"] = new JsonArray("dotnet", "--extra");
        else if (scenario == "command") config["Cmd"]![1] = "--worker-fixture";
        else if (scenario == "extra-command") config["Cmd"]!.AsArray().Add("https://example.invalid");
        else if (scenario != "valid") config["Labels"]!["hsr.observation." + scenario] = "other";
        var element = JsonSerializer.SerializeToElement(item);
        if (scenario is "valid" or "valid-string-entrypoint") IsolatedFixtureExecutor.ValidateReaderContainer(element, intent);
        else Assert.Equal("READER_CONTAINER_IDENTITY_UNCONFIRMED",
            Assert.Throws<InvalidDataException>(() => IsolatedFixtureExecutor.ValidateReaderContainer(element, intent)).Message);
    }
}
