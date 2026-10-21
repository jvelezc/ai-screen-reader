using System.Text.Json;
using System.Globalization;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public sealed record PrivateClientResourceLimits(long? ProcessLimitHits, long? OomKills, long? PeakTasks);
public sealed record PrivateClientProof(bool Ready, string Stage, string? ErrorCode, bool PixelsDecoded,
    bool PauseClearedWithoutRead, bool ResumeRendered, bool ExcludedCleared, bool RevokedCleared,
    bool TargetUnchanged, BrowserShutdownStatus? ViewerShutdown, BrowserShutdownStatus? TargetShutdown)
{
    public string Schema => "hsr-private-view-client-v1";
    public bool HumanAcceptancePerformed => false;
    public bool PublicListenerEnabled => false;
    public HeadedDisplayObservation? Display { get; init; }
    public HeadedKernelObservation? Kernel { get; init; }
    public PrivateClientResourceLimits? ResourceLimits { get; init; }
    public bool BrowserCloseConfirmed => ViewerShutdown is { Phase: BrowserShutdownPhase.Closed, ProcessExitConfirmed: true, ActiveCalls: 0 }
        && TargetShutdown is { Phase: BrowserShutdownPhase.Closed, ProcessExitConfirmed: true, ActiveCalls: 0 };
}
public static class PrivateClientDemo
{
    public static async Task<int> RunAsync(bool interactive, CancellationToken token = default, bool headedProof = false)
    {
        var proof = await ExecuteAsync(interactive, token, headedProof);
        Console.WriteLine(JsonSerializer.Serialize(proof, TraversalDemo.JsonOptions)); return proof.Ready ? 0 : 3;
    }
    public static async Task<PrivateClientProof> ExecuteAsync(bool interactive = false, CancellationToken token = default, bool headedProof = false)
    {
        ChromiumFixtureBrowser? target = null; ChromiumPrivateViewer? viewer = null; ControlledScanSession? control = null;
        PrivateViewerBroker? broker = null; PrivateViewerServer? server = null; PrivateViewerHttpClient? client = null;
        var stage = "launch"; string? error = null; var decoded = false; var paused = false; var resumed = false; var excluded = false; var revoked = false; var unchanged = false;
        HeadedDisplayObservation? display = null; HeadedKernelObservation? kernel = null;
        try
        {
            if (headedProof)
            {
                display = HeadedDisplayObservation.ReadCurrent(); kernel = HeadedKernelObservation.ReadCurrent();
                if (!kernel.Restricted) throw new BrowserOperationException("VIEW_CLIENT_KERNEL_UNCONFIRMED");
                await TraversalArtifactWriter.WriteNewAsync("display-number.txt", display.Number.ToString(CultureInfo.InvariantCulture) + "\n");
                await TraversalArtifactWriter.WriteNewAsync("display-pid.txt", display.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
            }
            stage = "target-launch"; target = await ChromiumFixtureBrowser.LaunchPrivatePixelFixtureAsync(headedProof, token);
            stage = "controller-start";
            control = new(target, new OwnedEvidenceFileStore(Path.Combine("artifacts", "private-client")));
            var ai = await control.AcquireAsync(TimeSpan.FromMinutes(2));
            await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent, cancellationToken: token);
            var scope = new ViewerScope(control.Status.ScanId, control.SessionId); var subject = Guid.NewGuid(); var identity = new ProcessViewerIdentity(subject);
            broker = new(scope, subject); var bootstrap = broker.IssueBootstrap(subject, scope, TimeSpan.FromMinutes(1));
            stage = "server-start"; server = await PrivateViewerServer.StartAsync(identity, new PrivateViewerService(broker, control), token, new PrivatePixelViewerService(broker, control));
            stage = "exchange";
            client = new(server.BaseAddress, scope, identity.Credential.Value); await client.ExchangeAsync(bootstrap.Secret.Value, token);
            stage = "viewer-launch"; viewer = await ChromiumPrivateViewer.LaunchAsync(interactive || headedProof, token); var refresh = new PrivateViewerRefresh(client, control, viewer);
            var before = await target.ReadPixelFixtureStateAsync();
            stage = "first-render"; await refresh.RefreshAsync(token);
            await UntilAsync(async () => (await viewer.ObserveAuditAsync()).PixelsDecoded, token); decoded = true;
            if (interactive)
            {
                Console.WriteLine("Private fixture viewer is open for up to two minutes. Pause, Resume, Refresh and End affect viewing only. Close the window or press Ctrl+C to finish.");
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token); lifetime.CancelAfter(TimeSpan.FromMinutes(2));
                var loop = LoopAsync(refresh, viewer, lifetime.Token);
                await Task.WhenAny(loop, viewer.Ended, Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token));
                lifetime.Cancel();
                // Close the actual renderer before joining the loop; pending Evaluate callbacks must settle too.
                try { await viewer.DisposeAsync(); }
                finally { try { await loop; } catch (OperationCanceledException) { } }
                unchanged = before == await target.ReadPixelFixtureStateAsync(); stage = "interactive-ended";
            }
            else
            {
                stage = "pause"; await viewer.ActivateControlAsync(PrivateViewAction.Pause);
                await UntilAsync(() => Task.FromResult(viewer.Intent.Action == PrivateViewAction.Pause), token);
                var requests = client.Requests; await refresh.RefreshAsync(token);
                paused = (await viewer.ObserveAuditAsync()).ContentCleared && client.Requests == requests;
                await viewer.ActivateControlAsync(PrivateViewAction.Resume);
                await UntilAsync(() => Task.FromResult(viewer.Intent.Action == PrivateViewAction.Resume), token);
                await refresh.RefreshAsync(token); await UntilAsync(async () => (await viewer.ObserveAuditAsync()).PixelsDecoded, token); resumed = true;
                stage = "excluded"; await control.ReleaseAsync(ai, token); await refresh.RefreshAsync(token);
                excluded = await viewer.ObserveAuditAsync() is { Phase: "Excluded", ContentCleared: true };
                await control.AcquireAsync(TimeSpan.FromMinutes(1)); await refresh.RefreshAsync(token);
                await UntilAsync(async () => (await viewer.ObserveAuditAsync()).PixelsDecoded, token);
                stage = "revoked"; await client.RevokeAsync(token); await refresh.RefreshAsync(token);
                revoked = await viewer.ObserveAuditAsync() is { Phase: "Ended", ContentCleared: true };
                unchanged = before == await target.ReadPixelFixtureStateAsync(); stage = "completed";
            }
        }
        catch (OperationCanceledException) { error = "VIEW_CLIENT_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch { error = "VIEW_CLIENT_PROOF_UNCONFIRMED"; }
        finally
        {
            try { if (viewer is not null) await viewer.DisposeAsync(); } catch { error ??= "VIEW_CLIENT_CLOSE_UNCONFIRMED"; }
            client?.Dispose();
            try { if (broker is not null) await broker.DisposeAsync(); } catch { error ??= "VIEW_CLIENT_BROKER_CLOSE_UNCONFIRMED"; }
            try { if (server is not null) await server.DisposeAsync(); } catch { error ??= "VIEW_CLIENT_SERVER_CLOSE_UNCONFIRMED"; }
            try { if (control is not null) await control.DisposeAsync(); else if (target is not null) await target.DisposeAsync(); }
            catch { error ??= "VIEW_CLIENT_TARGET_CLOSE_UNCONFIRMED"; }
        }
        var ready = error is null && decoded && unchanged && (interactive || paused && resumed && excluded && revoked)
            && viewer?.ShutdownStatus is { Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0 } && control?.Status.Shutdown is { Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0 };
        return new(ready, stage, error, decoded, paused, resumed, excluded, revoked, unchanged, viewer?.ShutdownStatus, control?.Status.Shutdown)
        { Display = display, Kernel = kernel, ResourceLimits = headedProof ? ReadResourceLimits() : null };
    }
    private static PrivateClientResourceLimits ReadResourceLimits()
    {
        static long? Read(string name, string? key = null)
        {
            try
            {
                var value = key is null ? File.ReadAllText("/sys/fs/cgroup/" + name).Trim()
                    : File.ReadAllLines("/sys/fs/cgroup/" + name).Single(line => line.StartsWith(key + " ", StringComparison.Ordinal)).Split(' ')[1];
                return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;
            }
            catch { return null; }
        }
        return new(Read("pids.events", "max"), Read("memory.events", "oom_kill"), Read("pids.peak"));
    }
    private static async Task LoopAsync(PrivateViewerRefresh refresh, ChromiumPrivateViewer viewer, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !viewer.Ended.IsCompleted)
            { await refresh.RefreshAsync(token); await Task.Delay(TimeSpan.FromSeconds(1), token); }
        }
        catch (BrowserOperationException) when (viewer.Ended.IsCompleted) { /* Window end settled the actual render; no new render is admitted. */ }
    }
    internal static async Task UntilAsync(Func<Task<bool>> condition, CancellationToken token = default)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(token); bound.CancelAfter(TimeSpan.FromSeconds(5));
        while (!await condition()) await Task.Delay(10, bound.Token);
    }
}
