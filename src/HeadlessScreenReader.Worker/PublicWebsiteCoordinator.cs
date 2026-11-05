using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public interface IPublicWebsiteExecutor
{
    Task<PublicWebsiteObservation> ExecuteAsync(PublicWebsiteLease lease, Func<CancellationToken, Task<bool>> authorize, CancellationToken token);
}

/// <summary>Collects the declared page only; current authority and exact native retirement precede return.</summary>
public sealed class IsolatedPublicWebsiteExecutor(IsolatedPublicReaderSupervisor supervisor, TimeProvider? time = null) : IPublicWebsiteExecutor
{
    public async Task<PublicWebsiteObservation> ExecuteAsync(PublicWebsiteLease lease, Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        lease.Job.Validate();
        IsolatedPublicReaderSession? reader = null;
        AccessibilitySnapshot? observed = null;
        DateTimeOffset received = default;
        PublicReaderRetirement? retired = null;
        try
        {
            if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_WEBSITE_AUTHORITY_LOST");
            reader = await supervisor.OpenAsync(lease.Job.OrganizationId, lease.Job.JobId, lease.Job.Source, token).ConfigureAwait(false);
            if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_WEBSITE_AUTHORITY_LOST");
            observed = await reader.ObserveAsync(token).ConfigureAwait(false);
            received = (time ?? TimeProvider.System).GetUtcNow();
            if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_WEBSITE_AUTHORITY_LOST");
        }
        finally
        {
            try { retired = await supervisor.RetireAsync(lease.Job.OrganizationId, lease.Job.JobId).ConfigureAwait(false); }
            catch { throw new BrowserOperationException("PUBLIC_WEBSITE_CLEANUP_UNCONFIRMED"); }
        }
        token.ThrowIfCancellationRequested();
        if (reader is null || observed is null || retired is not { NativeShutdownConfirmed: true, ResourcesAbsenceConfirmed: true })
            throw new BrowserOperationException("PUBLIC_WEBSITE_CLEANUP_UNCONFIRMED");
        var result = new PublicWebsiteObservation(PublicWebsiteObservation.CurrentSchema, lease.Job, lease.WorkerId,
            [reader.InitialSnapshot, observed], received, retired.NativeShutdownConfirmed, retired.ResourcesAbsenceConfirmed, 0, false);
        result.Validate(lease.Job, lease.WorkerId); return result;
    }
}

public sealed record PublicWebsiteRunResult(string State, Guid? JobId = null, string? ErrorCode = null)
{
    public PublicReaderFailureDiagnostic? FailureDiagnostic { get; init; }
}

public sealed record PublicWebsiteOperationalEvent(string Schema, string Stage, Guid OrganizationId, Guid ScanId,
    Guid JobId, long Generation, Guid WorkerId, double ElapsedMilliseconds, string? State,
    string? ErrorCode, int? ObservedNodes, bool? NativeShutdownConfirmed, bool? ResourcesAbsenceConfirmed,
    PublicReaderFailureDiagnostic? FailureDiagnostic);

/// <summary>The database owns publication authority; this coordinator supplies only actually collected passive facts.</summary>
public sealed class PublicWebsiteCoordinator(IPublicWebsiteLeases leases, IPublicWebsiteExecutor executor)
{
    public Action<PublicWebsiteOperationalEvent>? Diagnostic { get; init; }

    public async Task<PublicWebsiteRunResult> RunOnceAsync(Guid worker, CancellationToken token)
    {
        var claim = await leases.ClaimAsync(worker, token).ConfigureAwait(false);
        if (claim.Lease is not { } lease) return new(claim.Status);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        void Log(string stage, PublicWebsiteRunResult? result = null, PublicWebsiteObservation? observed = null)
        {
            try
            {
                Diagnostic?.Invoke(new("hsr-public-website-operation-v1", stage, lease.Job.OrganizationId,
                    lease.Job.ScanId, lease.Job.JobId, lease.Job.Generation, lease.WorkerId,
                    System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, result?.State,
                    result?.ErrorCode, observed?.Captures.Sum(capture => capture.Nodes.Length),
                    observed?.NativeShutdownConfirmed, observed?.ResourcesAbsenceConfirmed, result?.FailureDiagnostic));
            }
            catch (Exception) { /* Logging cannot change publication or cleanup authority. */ }
        }
        Log("Claimed");
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var heartbeatStop = new CancellationTokenSource();
        var permissionLost = 0;
        var publishing = false;
        async Task<bool> Authorize(CancellationToken check)
        {
            var allowed = await leases.RenewAsync(lease, check).ConfigureAwait(false);
            if (!allowed) { Interlocked.Exchange(ref permissionLost, 1); await execution.CancelAsync().ConfigureAwait(false); }
            return allowed;
        }
        var heartbeat = HeartbeatAsync(Authorize, heartbeatStop.Token, execution);
        try
        {
            var observed = await executor.ExecuteAsync(lease, Authorize, execution.Token).ConfigureAwait(false);
            await heartbeatStop.CancelAsync().ConfigureAwait(false);
            await heartbeat.ConfigureAwait(false);
            execution.Token.ThrowIfCancellationRequested();
            if (!await Authorize(execution.Token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_WEBSITE_AUTHORITY_LOST");
            observed.Validate(lease.Job, lease.WorkerId);
            Log("Collected", observed: observed);
            publishing = true;
            var result = await leases.CompleteAsync(lease, observed, execution.Token).ConfigureAwait(false)
                ? new PublicWebsiteRunResult("Observed", lease.Job.JobId)
                : await BlockAsync(lease, "PUBLIC_WEBSITE_PUBLICATION_UNCONFIRMED").ConfigureAwait(false);
            Log("Finished", result, observed); return result;
        }
        catch (Exception error)
        {
            var code = error is BrowserOperationException { Code: "PUBLIC_WEBSITE_CLEANUP_UNCONFIRMED" }
                ? "PUBLIC_WEBSITE_CLEANUP_UNCONFIRMED"
                : Volatile.Read(ref permissionLost) == 1 || error is BrowserOperationException { Code: "PUBLIC_WEBSITE_AUTHORITY_LOST" }
                    ? "PUBLIC_WEBSITE_AUTHORITY_LOST"
                    : token.IsCancellationRequested ? "PUBLIC_WEBSITE_EXECUTION_CANCELLED"
                    : publishing ? "PUBLIC_WEBSITE_PUBLICATION_UNCONFIRMED"
                    : "PUBLIC_WEBSITE_SOURCE_UNCONFIRMED";
            var result = await BlockAsync(lease, code).ConfigureAwait(false);
            result = result with { FailureDiagnostic = PublicReaderFailureDiagnostic.From(error, lease.Job.JobId) };
            Log("Finished", result); return result;
        }
        finally
        {
            await heartbeatStop.CancelAsync().ConfigureAwait(false);
            await heartbeat.ConfigureAwait(false);
        }
    }

    private async Task<PublicWebsiteRunResult> BlockAsync(PublicWebsiteLease lease, string code)
    {
        // Independent bounded reconciliation; cancelled execution cannot cancel this final authority check.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            return await leases.BlockAsync(lease, code, deadline.Token).ConfigureAwait(false)
                ? new("Blocked", lease.Job.JobId, code) : new("Unconfirmed", lease.Job.JobId, code);
        }
        catch { return new("Unconfirmed", lease.Job.JobId, code); }
    }

    private static async Task HeartbeatAsync(Func<CancellationToken, Task<bool>> authorize, CancellationToken stop, CancellationTokenSource execution)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
                if (!await authorize(stop).ConfigureAwait(false)) return;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { await execution.CancelAsync().ConfigureAwait(false); }
    }
}
