using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicWebsiteReadinessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-public-ready-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task StandaloneFixtureHostCannotAdvertisePublicWebsiteProcessing()
    {
        await using var host = await ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image);
        var client = new ReaderHostClient(_root, ReaderHostProtocolTests.Image);
        Assert.True(await client.ReadyAsync());
        Assert.False(await client.PublicJobsReadyAsync());
        var response = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.PublicWebsiteReady, Guid.Empty, Guid.Empty, Guid.Empty);
        Assert.Equal("READER_HOST_PUBLIC_JOBS_UNAVAILABLE", response.ErrorCode);
        Assert.Null(response.Snapshot); Assert.Null(response.DocumentId); Assert.Null(response.Retirement);
        Assert.Equal(0, response.DispatchedInputs); Assert.False(response.GoalVerified);
        Assert.Empty(new OwnershipJournal(Path.Combine(_root, ".reader-runtime")).ReadPending());
    }

    [Fact]
    public async Task TrustedPublicProcessorReadinessEndsWithItsOwnedHost()
    {
        var host = await ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image, null, publicJobsReady: true);
        var client = new ReaderHostClient(_root, ReaderHostProtocolTests.Image);
        try
        {
            Assert.True(await client.PublicJobsReadyAsync()); Assert.True(await client.ReadyAsync());
            var reply = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.PublicWebsiteReady, Guid.Empty, Guid.Empty, Guid.Empty);
            Assert.Null(reply.ErrorCode); Assert.Null(reply.Snapshot); Assert.Null(reply.DocumentId); Assert.Null(reply.Retirement);
            Assert.Equal(0, reply.DispatchedInputs); Assert.False(reply.GoalVerified);
        }
        finally { await host.DisposeAsync(); }
        Assert.False(await client.PublicJobsReadyAsync());
        Assert.False(File.Exists(ReaderHostEndpoint.DescriptorPath(_root)));
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("organization")]
    [InlineData("session")]
    public void PublicReadinessCannotCarryCallerOrReaderScope(string field)
    {
        var endpoint = ReaderHostProtocolTests.Endpoint();
        var request = new ReaderHostRequest(endpoint.Schema, endpoint.Instance, Guid.NewGuid(), endpoint.Capability,
            ReaderHostOperation.PublicWebsiteReady, Guid.Empty, Guid.Empty, Guid.Empty);
        request.Validate(endpoint);
        var changed = field switch
        {
            "subject" => request with { Subject = Guid.NewGuid() },
            "organization" => request with { Organization = Guid.NewGuid() },
            _ => request with { Session = Guid.NewGuid() }
        };
        Assert.Equal("READER_HOST_REQUEST_DENIED", Assert.Throws<BrowserOperationException>(() => changed.Validate(endpoint)).Code);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("snapshot")]
    [InlineData("retirement")]
    [InlineData("inputs")]
    [InlineData("goal")]
    [InlineData("operation")]
    public void PublicReadinessCannotImpersonateAnObservationOrOutcome(string field)
    {
        var endpoint = ReaderHostProtocolTests.Endpoint();
        var request = new ReaderHostRequest(endpoint.Schema, endpoint.Instance, Guid.NewGuid(), endpoint.Capability,
            ReaderHostOperation.PublicWebsiteReady, Guid.Empty, Guid.Empty, Guid.Empty);
        var reply = new ReaderHostResponse(endpoint.Schema, endpoint.Instance, request.RequestId, request.Operation,
            Guid.Empty, Guid.Empty, Guid.Empty, null, null, null, null, 0, false);
        var changed = field switch
        {
            "document" => reply with { DocumentId = "invented" },
            "snapshot" => reply with { Snapshot = new("invented", [new("n", "button", "invented")], []) },
            "retirement" => reply with { Retirement = new(true, true) },
            "inputs" => reply with { DispatchedInputs = 1 },
            "goal" => reply with { GoalVerified = true },
            _ => reply with { Operation = ReaderHostOperation.Ready }
        };
        Assert.Equal("READER_HOST_RESPONSE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => changed.Validate(request)).Code);
    }
}
