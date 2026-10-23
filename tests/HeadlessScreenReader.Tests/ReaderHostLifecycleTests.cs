using System.Net.Sockets;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using HeadlessScreenReader.Cli.HostedControl;
using System.Net;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderHostLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-reader-host-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task ActualLoopbackBrokerPublishesPrivateAuthorityAndRemovesItOnlyAfterDraining()
    {
        var host = await ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image);
        var client = new ReaderHostClient(_root, ReaderHostProtocolTests.Image);
        var endpoint = client.Endpoint();
        try
        {
            Assert.True(await client.ReadyAsync());
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ReaderHostEndpoint.DescriptorPath(_root)));
            Assert.False(await new ReaderHostClient(_root, "sha256:" + new string('b', 64)).ReadyAsync());
            var missing = await client.ExchangeAsync(endpoint, ReaderHostOperation.Observe, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            Assert.Equal("READER_HOST_SESSION_NOT_FOUND", missing.ErrorCode); Assert.Null(missing.Snapshot);
            var absent = await client.ExchangeAsync(endpoint, ReaderHostOperation.Retire, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            Assert.True(absent.Retirement!.ContainerAbsenceConfirmed); Assert.False(absent.Retirement.NativeShutdownConfirmed);
            Assert.Empty(new OwnershipJournal(Path.Combine(_root, ".reader-runtime")).ReadPending());
        }
        finally { await host.DisposeAsync(); }
        Assert.False(File.Exists(ReaderHostEndpoint.DescriptorPath(_root))); Assert.False(await client.ReadyAsync());
        using var exclusive = new OwnershipJournal(Path.Combine(_root, ".reader-runtime")).AcquireExclusive();
        Assert.NotEqual(Guid.Empty, endpoint.Instance);
    }

    [Fact]
    public async Task WrongProcessCapabilityCannotReachDispatchAndDoesNotPoisonTheListener()
    {
        await using var host = await ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image);
        var client = new ReaderHostClient(_root, ReaderHostProtocolTests.Image);
        var forged = client.Endpoint() with { Capability = Convert.ToBase64String(new byte[32]) };
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => client.ExchangeAsync(forged, ReaderHostOperation.Ready,
            Guid.Empty, Guid.Empty, Guid.Empty, timeout: TimeSpan.FromSeconds(5)));
        Assert.Equal("READER_HOST_EXCHANGE_UNCONFIRMED", error.Code);
        Assert.Empty(new OwnershipJournal(Path.Combine(_root, ".reader-runtime")).ReadPending());
        Assert.True(await client.ReadyAsync());
    }

    [Fact]
    public async Task BrokerDrainClosesAnActualClientWithAnIncompleteFrameAndJoinsItsWork()
    {
        var host = await ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image);
        var endpoint = new ReaderHostClient(_root, ReaderHostProtocolTests.Image).Endpoint();
        using var socket = new TcpClient();
        try
        {
            await socket.ConnectAsync(System.Net.IPAddress.Loopback, endpoint.Port);
            await socket.GetStream().WriteAsync(new byte[] { 0 });
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            try { Assert.Equal(0, await socket.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5))); }
            catch (IOException error) when (error.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
            { /* Closing a socket with an incomplete unread frame may reset rather than send FIN. Both prove this connection ended. */ }
            Assert.False(File.Exists(ReaderHostEndpoint.DescriptorPath(_root)));
        }
        finally { await host.DisposeAsync(); }
    }

    [Fact]
    public async Task RestartChangesAuthorityAndOldRequestsCannotSilentlyJoinAnotherWorker()
    {
        var first = await ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image);
        var client = new ReaderHostClient(_root, ReaderHostProtocolTests.Image); var old = client.Endpoint();
        await first.DisposeAsync();
        await using var second = await ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image);
        var current = client.Endpoint(); Assert.NotEqual(old.Instance, current.Instance); Assert.NotEqual(old.Capability, current.Capability);
        var transplanted = old with { Port = current.Port };
        Assert.Equal("READER_HOST_EXCHANGE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            client.ExchangeAsync(transplanted, ReaderHostOperation.Ready, Guid.Empty, Guid.Empty, Guid.Empty))).Code);
        Assert.True(await client.ReadyAsync());
    }

    [Fact]
    public async Task HttpReadinessBecomesUnavailableWhenTheActualReaderWorkerStops()
    {
        var host = await ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image);
        try
        {
            var client = new ReaderHostClient(_root, ReaderHostProtocolTests.Image);
            await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, _root, new string('a', 40), 0),
                new ReaderProjectStub(), new PortalObservationStub(), readerRuntimeReady: client.ReadyAsync);
            using var http = new HttpClient { BaseAddress = server.BaseAddress };
            using var ready = await http.GetAsync("/internal/ready"); Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            await host.DisposeAsync();
            using var stopped = await http.GetAsync("/internal/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, stopped.StatusCode);
            Assert.Contains("CONTROL_READER_RUNTIME_UNAVAILABLE", await stopped.Content.ReadAsStringAsync());
            Assert.DoesNotContain("Capability", await stopped.Content.ReadAsStringAsync());
        }
        finally { await host.DisposeAsync(); }
    }

    [Theory]
    [InlineData("oversize")]
    [InlineData("malformed")]
    [InlineData("unrelated")]
    public async Task StartupDoesNotOverwriteAnInvalidExistingDescriptor(string scenario)
    {
        var path = ReaderHostEndpoint.DescriptorPath(_root); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = scenario switch { "oversize" => new string('x', 4097), "malformed" => "{", _ => "{}" };
        await File.WriteAllTextAsync(path, original);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        if (scenario == "oversize")
            Assert.Equal("READER_HOST_UNAVAILABLE", (await Assert.ThrowsAsync<BrowserOperationException>(() => ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image))).Code);
        else await Assert.ThrowsAsync<JsonException>(() => ReaderHost.StartAsync(_root, "podman", ReaderHostProtocolTests.Image));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        using var exclusive = new OwnershipJournal(Path.Combine(_root, ".reader-runtime")).AcquireExclusive();
    }
}
