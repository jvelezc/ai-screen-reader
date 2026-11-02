using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicWebsiteCoordinatorTests
{
    private sealed class Leases : IPublicWebsiteLeases
    {
        public PublicWebsiteLease Lease { get; } = new(PublicWebsiteObservationTests.Job(), PublicWebsiteObservationTests.Worker, Guid.NewGuid());
        public string ClaimStatus { get; set; } = "Claimed";
        public bool RenewAllowed { get; set; } = true;
        public bool CompleteAllowed { get; set; } = true;
        public bool BlockAllowed { get; set; } = true;
        public Exception? CompleteFailure { get; set; }
        public Exception? BlockFailure { get; set; }
        public int Renewals { get; private set; }
        public PublicWebsiteObservation? Completed { get; private set; }
        public PublicWebsiteLease? Blocked { get; private set; }
        public string? BlockCode { get; private set; }
        public Task<PublicWebsiteClaim> ClaimAsync(Guid worker, CancellationToken token) => Task.FromResult(new PublicWebsiteClaim(ClaimStatus, ClaimStatus == "Claimed" ? Lease : null));
        public Task<bool> RenewAsync(PublicWebsiteLease lease, CancellationToken token) { Assert.Same(Lease, lease); Renewals++; return Task.FromResult(RenewAllowed); }
        public Task<bool> CompleteAsync(PublicWebsiteLease lease, PublicWebsiteObservation value, CancellationToken token)
        {
            Assert.Same(Lease, lease); if (CompleteFailure is not null) throw CompleteFailure;
            Completed = value; return Task.FromResult(CompleteAllowed);
        }
        public Task<bool> BlockAsync(PublicWebsiteLease lease, string code, CancellationToken token)
        {
            Blocked = lease; BlockCode = code; if (BlockFailure is not null) throw BlockFailure; return Task.FromResult(BlockAllowed);
        }
    }
    private sealed class Executor : IPublicWebsiteExecutor
    {
        public int Calls { get; private set; }
        public PublicWebsiteObservation Result { get; set; } = PublicWebsiteObservationTests.Observation();
        public Exception? Failure { get; set; }
        public Task<PublicWebsiteObservation> ExecuteAsync(PublicWebsiteLease lease, Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
        {
            Calls++; if (Failure is not null) throw Failure; return Task.FromResult(Result);
        }
    }

    [Theory]
    [InlineData("Empty")]
    [InlineData("AtCapacity")]
    [InlineData("WorkspaceBusy")]
    [InlineData("AuthorityLost")]
    [InlineData("Deferred")]
    public async Task NoClaimDoesNotExecuteOrPublishAWebsite(string status)
    {
        var leases = new Leases { ClaimStatus = status }; var executor = new Executor();
        var result = await new PublicWebsiteCoordinator(leases, executor).RunOnceAsync(PublicWebsiteObservationTests.Worker, CancellationToken.None);
        Assert.Equal(status, result.State); Assert.Null(result.JobId); Assert.Equal(0, executor.Calls);
        Assert.Equal(0, leases.Renewals); Assert.Null(leases.Completed); Assert.Null(leases.Blocked);
    }

    [Fact]
    public async Task FreshAuthorityAndExactPassiveFactsPrecedeDatabaseCompletion()
    {
        var leases = new Leases(); var executor = new Executor();
        var result = await new PublicWebsiteCoordinator(leases, executor).RunOnceAsync(PublicWebsiteObservationTests.Worker, CancellationToken.None);
        Assert.Equal("Observed", result.State); Assert.Equal(leases.Lease.Job.JobId, result.JobId); Assert.Null(result.ErrorCode);
        Assert.Equal(1, executor.Calls); Assert.Equal(1, leases.Renewals); Assert.Same(executor.Result, leases.Completed); Assert.Null(leases.Blocked);
        var completed = Assert.IsType<PublicWebsiteObservation>(leases.Completed);
        Assert.False(completed.GoalVerified); Assert.Equal(0, completed.DispatchedInputs);
    }

    [Fact]
    public async Task AuthorityLostAfterCollectionCannotPublishLateSnapshots()
    {
        var leases = new Leases { RenewAllowed = false }; var executor = new Executor();
        var result = await new PublicWebsiteCoordinator(leases, executor).RunOnceAsync(PublicWebsiteObservationTests.Worker, CancellationToken.None);
        Assert.Equal("Blocked", result.State); Assert.Equal("PUBLIC_WEBSITE_AUTHORITY_LOST", result.ErrorCode);
        Assert.Equal(1, executor.Calls); Assert.Null(leases.Completed); Assert.Same(leases.Lease, leases.Blocked);
    }

    [Theory]
    [InlineData("source", "PUBLIC_WEBSITE_SOURCE_UNCONFIRMED")]
    [InlineData("cleanup", "PUBLIC_WEBSITE_CLEANUP_UNCONFIRMED")]
    [InlineData("authority", "PUBLIC_WEBSITE_AUTHORITY_LOST")]
    public async Task SourceAndCleanupFailuresRemainDistinctFromWebsiteBarriers(string scenario, string expected)
    {
        var leases = new Leases(); var executor = new Executor
        {
            Failure = new BrowserOperationException(scenario switch
            {
                "cleanup" => "PUBLIC_WEBSITE_CLEANUP_UNCONFIRMED",
                "authority" => "PUBLIC_WEBSITE_AUTHORITY_LOST",
                _ => "PUBLIC_READER_OPEN_UNCONFIRMED"
            })
        };
        var result = await new PublicWebsiteCoordinator(leases, executor).RunOnceAsync(PublicWebsiteObservationTests.Worker, CancellationToken.None);
        Assert.Equal("Blocked", result.State); Assert.Equal(expected, result.ErrorCode); Assert.Equal(expected, leases.BlockCode);
        Assert.Null(leases.Completed); Assert.Equal(1, executor.Calls);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("worker")]
    [InlineData("goal")]
    [InlineData("resources")]
    public async Task ChangedOrUnconfirmedExecutorFactsCannotReachPublication(string scenario)
    {
        var leases = new Leases(); var value = PublicWebsiteObservationTests.Observation();
        var executor = new Executor
        {
            Result = scenario switch
            {
                "tenant" => value with { Job = value.Job with { OrganizationId = Guid.NewGuid() } },
                "worker" => value with { WorkerId = Guid.NewGuid() },
                "goal" => value with { GoalVerified = true },
                _ => value with { ResourcesAbsenceConfirmed = false }
            }
        };
        var result = await new PublicWebsiteCoordinator(leases, executor).RunOnceAsync(PublicWebsiteObservationTests.Worker, CancellationToken.None);
        Assert.Equal("Blocked", result.State); Assert.Equal("PUBLIC_WEBSITE_SOURCE_UNCONFIRMED", result.ErrorCode);
        Assert.Null(leases.Completed); Assert.Same(leases.Lease, leases.Blocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrUnacknowledgedDatabaseCompletionDoesNotBecomeObserved(bool throws)
    {
        var leases = new Leases { CompleteAllowed = false, CompleteFailure = throws ? new IOException("simulated transport failure") : null };
        var result = await new PublicWebsiteCoordinator(leases, new Executor()).RunOnceAsync(PublicWebsiteObservationTests.Worker, CancellationToken.None);
        Assert.Equal("Blocked", result.State); Assert.Equal("PUBLIC_WEBSITE_PUBLICATION_UNCONFIRMED", result.ErrorCode);
        Assert.Equal("PUBLIC_WEBSITE_PUBLICATION_UNCONFIRMED", leases.BlockCode); Assert.Same(leases.Lease, leases.Blocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnacknowledgedFailureReconciliationRemainsUnconfirmed(bool throws)
    {
        var leases = new Leases { BlockAllowed = false, BlockFailure = throws ? new IOException("simulated transport failure") : null };
        var executor = new Executor { Failure = new IOException("simulated source failure") };
        var result = await new PublicWebsiteCoordinator(leases, executor).RunOnceAsync(PublicWebsiteObservationTests.Worker, CancellationToken.None);
        Assert.Equal("Unconfirmed", result.State); Assert.Equal("PUBLIC_WEBSITE_SOURCE_UNCONFIRMED", result.ErrorCode);
        Assert.Null(leases.Completed); Assert.Equal(1, executor.Calls);
    }
}
