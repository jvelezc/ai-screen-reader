using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static class HostedControlDemo
{
    public static async Task<int> RunAsync()
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        using var termination = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
            context => { context.Cancel = true; stop.Cancel(); });
        try
        {
            var key = Environment.GetEnvironmentVariable("HSR_CONTROL_PUBLISHABLE_KEY") ?? throw new InvalidOperationException();
            var evidence = Environment.GetEnvironmentVariable("HSR_CONTROL_EVIDENCE") ?? throw new InvalidOperationException();
            var commit = Environment.GetEnvironmentVariable("HSR_RELEASE_COMMIT") ?? throw new InvalidOperationException();
            var origin = Environment.GetEnvironmentVariable("HSR_CONTROL_ORIGIN") ?? "http://localhost:6086";
            var configuredPort = Environment.GetEnvironmentVariable("HSR_CONTROL_PORT") ?? "8086";
            if (!int.TryParse(configuredPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException();
            var options = new PortalOptions(key, evidence, commit, port, origin);
            options.Validate();
            using var handler = new SocketsHttpHandler
            { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            var project = new SupabasePortalClient(http, key);
            await using var server = await HostedControlServer.StartAsync(options, project, new PortalObservationReader(evidence), stop.Token).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Schema = "hsr-control-readiness-v1",
                State = "Listening",
                ReleaseCommit = commit,
                AppJwtValidated = false,
                PublicMcpEnabled = false
            }));
            try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception)
        {
            Console.Error.WriteLine("CONTROL_HOST_STOPPED_UNCONFIRMED");
            return 3;
        }
    }
}
