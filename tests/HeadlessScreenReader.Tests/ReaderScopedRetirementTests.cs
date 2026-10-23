using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderScopedRetirementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-reader-retirement-" + Guid.NewGuid().ToString("N"));
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private static FixtureJob ReaderJob() { var session = Guid.NewGuid(); return new(Guid.NewGuid(), session, session, 1); }

    [Fact]
    public async Task TargetedRecoveryWithoutAnIntentLeavesEveryUnrelatedIntentUntouched()
    {
        var journal = new OwnershipJournal(_root);
        using var owner = journal.AcquireExclusive();
        var first = await journal.BeginAsync(ReaderJob(), "podman", Image, default);
        var second = await journal.BeginAsync(ReaderJob(), "podman", Image, default);
        var executor = new IsolatedFixtureExecutor("podman", Image, journal: journal);
        Assert.Equal(0, await executor.RecoverReaderAsync(ReaderJob()));
        Assert.Equal(new[] { first.Owner, second.Owner }.Order(), journal.ReadPending().Select(intent => intent.Owner).Order());
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("partial-session")]
    [InlineData("generation")]
    [InlineData("runtime")]
    public async Task TargetedRecoveryRejectsAmbiguousScopeBeforeAnyNativeRemoval(string scenario)
    {
        var journal = new OwnershipJournal(_root);
        using var owner = journal.AcquireExclusive();
        var expected = ReaderJob();
        var mismatched = scenario switch
        {
            "organization" => expected with { OrganizationId = Guid.NewGuid() },
            "partial-session" => expected with { ScanId = Guid.NewGuid() },
            "generation" => expected with { Generation = 2 },
            _ => expected
        };
        var intent = await journal.BeginAsync(mismatched, scenario == "runtime" ? "docker" : "podman", Image, default);
        var executor = new IsolatedFixtureExecutor("podman", Image, journal: journal);
        var failure = await Assert.ThrowsAsync<BrowserOperationException>(() => executor.RecoverReaderAsync(expected));
        Assert.Equal("READER_RECOVERY_SCOPE_MISMATCH", failure.Code);
        Assert.Equal(intent, Assert.Single(journal.ReadPending()));
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("queue-job")]
    public async Task TargetedRecoveryCannotAcceptEmptyOrQueueStyleDescriptors(string scenario)
    {
        var job = ReaderJob();
        job = scenario switch
        {
            "organization" => job with { OrganizationId = Guid.Empty },
            "session" => job with { JobId = Guid.Empty },
            "generation" => job with { Generation = 2 },
            _ => job with { ScanId = Guid.NewGuid() }
        };
        var journal = new OwnershipJournal(_root); using var owner = journal.AcquireExclusive();
        var executor = new IsolatedFixtureExecutor("podman", Image, journal: journal);
        if (scenario is "organization" or "session")
            Assert.Equal("WORKER_DESCRIPTOR_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => executor.RecoverReaderAsync(job))).Code);
        else
            Assert.Equal("READER_RECOVERY_SCOPE_INVALID", (await Assert.ThrowsAsync<ArgumentException>(() => executor.RecoverReaderAsync(job))).Message);
        Assert.Empty(journal.ReadPending());
    }

    [Fact]
    public async Task MissingReservationConfirmsNoOwnedContainerButCannotInventAGracefulAcknowledgement()
    {
        await using var supervisor = await IsolatedReaderSupervisor.StartAsync(_root, "podman", Image);
        var result = await supervisor.RetireAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.True(result.ContainerAbsenceConfirmed); Assert.False(result.NativeShutdownConfirmed);
        Assert.Empty(new OwnershipJournal(supervisor.JournalRoot).ReadPending());
        await Assert.ThrowsAsync<ArgumentException>(() => supervisor.RetireAsync(Guid.Empty, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => supervisor.RetireAsync(Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public async Task MissingReservationCannotAcknowledgeAnUnexpectedDurableIntentOrDeleteIt()
    {
        await using var supervisor = await IsolatedReaderSupervisor.StartAsync(_root, "podman", Image);
        var journal = new OwnershipJournal(supervisor.JournalRoot); var job = ReaderJob();
        var intent = await journal.BeginAsync(job, "podman", Image, default);
        try
        {
            var failure = await Assert.ThrowsAsync<BrowserOperationException>(() => supervisor.RetireAsync(job.OrganizationId, job.JobId));
            Assert.Equal("READER_SESSION_OWNERSHIP_UNCONFIRMED", failure.Code);
            Assert.Equal(intent, Assert.Single(journal.ReadPending()));
        }
        finally { await journal.RecordRemovalAsync(intent, false, default); } // No native create occurred in this deterministic fixture.
    }
}
