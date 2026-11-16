using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record IsolatedSearchTaskResult(SyntheticSearchTaskStart Start, byte[] Bundle, bool ContainerAbsenceConfirmed,
    string RetainedEvidenceDirectory);

public sealed partial class IsolatedFixtureExecutor
{
    public async Task<IsolatedSearchTaskResult> ExecuteSearchTaskAsync(SyntheticSearchTaskJob job, Guid workerId,
        Func<CancellationToken, Task<bool>> authorize, CancellationToken token = default)
    {
        job.Validate();
        if (workerId == Guid.Empty) throw new BrowserOperationException("SEARCH_TASK_WORKER_INVALID");
        ArgumentNullException.ThrowIfNull(authorize);
        token.ThrowIfCancellationRequested();
        if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("SEARCH_TASK_AUTHORITY_UNCONFIRMED");
        // This legacy descriptor is used only for exact native ownership and recovery, never observations or input authority.
        var cleanup = new FixtureJob(job.OrganizationId, job.ScanId, job.JobId, job.Generation);
        var native = await _journal.BeginAsync(cleanup, _runtime, _image, token).ConfigureAwait(false);
        var start = new SyntheticSearchTaskStart(SyntheticSearchTaskControl.Schema, job, workerId, native.Owner);
        var controlRoot = Path.Combine(Path.GetDirectoryName(_journal.IntentsDirectory)!, "search-task-control");
        SyntheticSearchTaskGrants? grants = null;
        byte[] collected;
        try
        {
            grants = await SyntheticSearchTaskGrants.BeginAsync(controlRoot, start, token).ConfigureAwait(false);
            if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("SEARCH_TASK_AUTHORITY_UNCONFIRMED");
            await CreateAndInspectAsync(native, token, searchTask: true).ConfigureAwait(false);
            var progress = new SyntheticSearchTaskProgressExchange(start, grants, authorize);
            var result = await InvokeAsync(["start", "--attach", "--interactive", native.ContainerName], null, token,
                TimeSpan.FromSeconds(180), SyntheticSearchTaskBundle.MaximumBundleBytes, progress).ConfigureAwait(false);
            if (result.Code != 0) throw new BrowserOperationException("SEARCH_TASK_COLLECTION_UNCONFIRMED");
            progress.RequireComplete();
            var bundle = SyntheticSearchTaskBundle.Decode(result.Bytes, start);
            bundle.MatchRetainedSources(grants.ReadRetainedSources());
            if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("SEARCH_TASK_AUTHORITY_UNCONFIRMED");
            collected = result.Bytes;
        }
        finally
        {
            grants?.Stop();
            if (!await RemoveOwnedAsync(native).ConfigureAwait(false)) throw new BrowserOperationException("WORKER_CONTAINER_CLEANUP_UNCONFIRMED");
            try { await _journal.RecordRemovalAsync(native, false, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { throw new BrowserOperationException("WORKER_NATIVE_OWNERSHIP_UNCONFIRMED"); }
        }
        token.ThrowIfCancellationRequested();
        if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("SEARCH_TASK_AUTHORITY_UNCONFIRMED");
        return new(start, collected, true, grants.DirectoryPath);
    }

    internal static void ValidateSearchContainer(JsonElement item, NativeOwnership native)
    {
        var config = item.GetProperty("Config"); var labels = config.GetProperty("Labels"); var entry = config.GetProperty("Entrypoint");
        var fixedEntry = entry.ValueKind == JsonValueKind.String ? entry.GetString() == "dotnet"
            : entry.ValueKind == JsonValueKind.Array && entry.EnumerateArray().Select(value => value.GetString()).SequenceEqual(["dotnet"]);
        if (item.GetProperty("Image").GetString()?.Replace("sha256:", "", StringComparison.Ordinal) != native.Image[7..] || !fixedEntry
            || !config.GetProperty("Cmd").EnumerateArray().Select(value => value.GetString()).SequenceEqual(["/app/cli/HeadlessScreenReader.Cli.dll", "--worker-search-tasks"])
            || labels.GetProperty("hsr.observation.organization").GetString() != native.Job.OrganizationId.ToString("N")
            || labels.GetProperty("hsr.observation.scan").GetString() != native.Job.ScanId.ToString("N")
            || labels.GetProperty("hsr.observation.job").GetString() != native.Job.JobId.ToString("N")
            || labels.GetProperty("hsr.observation.generation").GetString() != "1")
            throw new BrowserOperationException("SEARCH_TASK_CONTAINER_IDENTITY_UNCONFIRMED");
    }
}
