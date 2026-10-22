using System.Text.Json;
using System.Net.NetworkInformation;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class PortalRuntimeTests
{
    [Fact]
    public async Task ActualRuntimeProbeChecksHttpGuardAndJoinsStopWithoutTenantIdentityClaim()
    {
        var options = new PortalOptions(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/runtime-unused"), new string('a', 40));
        var result = await HostedControlDemo.CheckAsync(options);
        Assert.Equal("hsr-control-runtime-proof-v1", result.Schema); Assert.Equal("ControlRuntimeReady", result.State);
        Assert.Equal(options.ReleaseCommit, result.ReleaseCommit);
        Assert.True(result.PublicConfigurationRead); Assert.True(result.UnauthenticatedSessionDenied); Assert.True(result.ServerCloseConfirmed);
        Assert.False(result.AppJwtValidated); Assert.False(result.PublicMcpEnabled);
        Assert.DoesNotContain(options.PublishableKey, JsonSerializer.Serialize(result));
        Assert.NotNull(result.ClosedListener);
        Assert.DoesNotContain(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners(),
            endpoint => endpoint.Port == result.ClosedListener.Port);
        var second = await HostedControlDemo.CheckAsync(options);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task CancelledRuntimeProbeNeverReturnsReady()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostedControlDemo.CheckAsync(
            new(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/runtime-unused"), new string('a', 40)), cancellation.Token));
    }
}
