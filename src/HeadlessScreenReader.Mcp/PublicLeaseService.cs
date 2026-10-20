using System.Net.Sockets;
using Microsoft.Extensions.Hosting;

namespace HeadlessScreenReader.Mcp;

public sealed class PublicLeaseService(VerifiedPublicAccess access, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                await access.PulseAsync(false, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            Console.Error.WriteLine("ISOLATION_LEASE_LOST");
            lifetime.StopApplication();
        }
        finally
        {
            try { await access.PulseAsync(true, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException) { }
        }
    }
}
