using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static class HostedControlDemo
{
    public static async Task<int> RunAsync(bool check = false)
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
            var options = new PortalOptions(key, evidence, commit, check ? 0 : port, origin);
            options.Validate();
            if (check)
            {
                Console.WriteLine(JsonSerializer.Serialize(await CheckAsync(options, stop.Token).ConfigureAwait(false)));
                return 0;
            }
            using var handler = new SocketsHttpHandler
            { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            var project = new SupabasePortalClient(http, key);
            var archiveConfiguration = S3EvidenceConfiguration.Optional(Environment.GetEnvironmentVariable("HSR_EVIDENCE_S3_ACCESS_KEY"),
                Environment.GetEnvironmentVariable("HSR_EVIDENCE_S3_SECRET"));
            using var archive = archiveConfiguration is null ? null : new S3ObservationArchive(archiveConfiguration);
            using var traversalStore = archiveConfiguration is null ? null : new S3TraversalObjectStore(archiveConfiguration);
            var image = Environment.GetEnvironmentVariable("HSR_WORKER_IMAGE");
            if (image is null)
            {
                // The root-owned release manifest is already verified by the private launcher; the API only reads it.
                var manifestPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "manifest.json"));
                var manifestInfo = new FileInfo(manifestPath);
                if (!manifestInfo.Exists || manifestInfo.LinkTarget is not null || manifestInfo.Length is < 1 or > 2097152)
                    throw new InvalidOperationException();
                using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath, stop.Token).ConfigureAwait(false),
                    new() { AllowDuplicateProperties = false });
                if (manifest.RootElement.GetProperty("commit").GetString() != commit
                    || manifest.RootElement.GetProperty("schema").GetString() != "hsr-worker-control-release-v2") throw new InvalidOperationException();
                image = manifest.RootElement.GetProperty("image").GetString() ?? throw new InvalidOperationException();
            }
            var readerHost = new ReaderHostClient(evidence, image);
            if (!await readerHost.ReadyAsync().ConfigureAwait(false)) throw new InvalidOperationException();
            await using var readers = new PortalReaderSessions(project, TimeProvider.System,
                (subject, organization, session) => new PortalHostedReaderBrowser(readerHost, subject, organization, session));
            await using var server = await HostedControlServer.StartAsync(options, project, new PortalObservationReader(evidence, archive),
                stop.Token, readers, readerHost.ReadyAsync, traversalStore is null ? null : new TraversalArchive(traversalStore)).ConfigureAwait(false);
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
    /// <summary>Exercise this exact host closure, then join its actual stop before reporting readiness.</summary>
    internal static async Task<PortalRuntimeProof> CheckAsync(PortalOptions options, CancellationToken token = default)
    {
        options.Validate();
        using var providerHandler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var provider = new HttpClient(providerHandler) { Timeout = TimeSpan.FromSeconds(10) };
        var project = new SupabasePortalClient(provider, options.PublishableKey);
        var server = await HostedControlServer.StartAsync(options with { Port = 0 }, project,
            new PortalObservationReader(options.EvidenceRoot), token).ConfigureAwait(false);
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false };
            using var http = new HttpClient(handler) { BaseAddress = server.BaseAddress, Timeout = TimeSpan.FromSeconds(5) };
            using var configuration = await http.GetAsync("/api/config", token).ConfigureAwait(false);
            configuration.EnsureSuccessStatusCode();
            using var config = JsonDocument.Parse(await configuration.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            if (config.RootElement.GetProperty("projectUrl").GetString() != PortalOptions.Project.GetLeftPart(UriPartial.Authority)
                || config.RootElement.GetProperty("publishableKey").GetString() != options.PublishableKey)
                throw new InvalidOperationException("CONTROL_CONFIGURATION_UNCONFIRMED");
            using var listening = await http.GetAsync("/internal/ready", token).ConfigureAwait(false);
            listening.EnsureSuccessStatusCode();
            using var ready = JsonDocument.Parse(await listening.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            if (ready.RootElement.GetProperty("state").GetString() != "Listening"
                || ready.RootElement.GetProperty("releaseCommit").GetString() != options.ReleaseCommit
                || ready.RootElement.GetProperty("appJwtValidated").GetBoolean()
                || ready.RootElement.GetProperty("publicMcpEnabled").GetBoolean())
                throw new InvalidOperationException("CONTROL_READINESS_UNCONFIRMED");
            using var denied = await http.GetAsync("/api/session", token).ConfigureAwait(false);
            if (denied.StatusCode != HttpStatusCode.Unauthorized || denied.Headers.WwwAuthenticate.Single().Scheme != "Bearer")
                throw new InvalidOperationException("CONTROL_AUTHENTICATION_GUARD_UNCONFIRMED");
        }
        finally { await server.DisposeAsync().ConfigureAwait(false); }
        return new("hsr-control-runtime-proof-v1", "ControlRuntimeReady", options.ReleaseCommit,
            true, true, true, false, false)
        { ClosedListener = server.BaseAddress };
    }
}

/// <summary>Runtime/HTTP proof only. No user bearer, tenant operation or public MCP acceptance is implied.</summary>
internal sealed record PortalRuntimeProof(string Schema, string State, string ReleaseCommit,
    bool PublicConfigurationRead, bool UnauthenticatedSessionDenied, bool ServerCloseConfirmed,
    bool AppJwtValidated, bool PublicMcpEnabled)
{
    internal Uri? ClosedListener { get; init; }
}
