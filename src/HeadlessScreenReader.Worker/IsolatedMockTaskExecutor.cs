using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Returned only after parent validation, full control sequence and confirmed owned resource absence.</summary>
public sealed record IsolatedMockTaskResult(MockTaskStart Start, byte[] Bundle, bool ContainerAbsenceConfirmed);

public sealed partial class IsolatedFixtureExecutor
{
    /// <summary>Independent no-network mock profile. Authority callback must check the current original caller and job lease.</summary>
    public async Task<IsolatedMockTaskResult> ExecuteMockTaskAsync(MockTaskJob job, Guid workerId,
        Func<CancellationToken, Task<bool>> authorize, CancellationToken token = default)
    {
        job.Validate();
        if (workerId == Guid.Empty) throw new BrowserOperationException("MOCK_TASK_WORKER_INVALID");
        ArgumentNullException.ThrowIfNull(authorize);
        token.ThrowIfCancellationRequested();
        if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("MOCK_TASK_AUTHORITY_UNCONFIRMED");
        var controlRoot = Path.Combine(Path.GetDirectoryName(_journal.IntentsDirectory)!, "mock-task-control");
        // Only the four-part native cleanup identity uses the legacy DTO. No source, observation or task result does.
        var cleanup = new FixtureJob(job.OrganizationId, job.ScanId, job.JobId, job.Generation);
        var intent = await _journal.BeginAsync(cleanup, _runtime, _image, token).ConfigureAwait(false);
        var start = new MockTaskStart(MockTaskControl.Schema, job, workerId, intent.Owner);
        byte[] collected;
        try
        {
            Directory.CreateDirectory(controlRoot);
            var fence = Path.Combine(controlRoot, $"job-{job.OrganizationId:N}-{job.ScanId:N}-{job.JobId:N}-{job.Generation}.json");
            try
            {
                await using var saved = new FileStream(fence, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
                await JsonSerializer.SerializeAsync(saved, start, ReaderWorkerWire.Json, token).ConfigureAwait(false);
                await saved.FlushAsync(token).ConfigureAwait(false); saved.Flush(flushToDisk: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { throw new BrowserOperationException("MOCK_TASK_REPLAY_OR_FENCE_UNCONFIRMED"); }
            if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("MOCK_TASK_AUTHORITY_UNCONFIRMED");
            await CreateAndInspectAsync(intent, token, mockTask: true).ConfigureAwait(false);
            var progress = new MockTaskProgressExchange(start, controlRoot, authorize);
            var result = await InvokeAsync(["start", "--attach", "--interactive", intent.ContainerName], null, token,
                TimeSpan.FromSeconds(120), MockTaskCollectionBundle.MaximumBundleBytes, progress).ConfigureAwait(false);
            if (result.Code != 0) throw new BrowserOperationException("MOCK_TASK_COLLECTION_UNCONFIRMED");
            progress.RequireComplete();
            MockTaskCollectionBundle.Decode(result.Bytes, start);
            if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("MOCK_TASK_AUTHORITY_UNCONFIRMED");
            collected = result.Bytes;
        }
        finally
        {
            // Cancellation never skips removal. A pending intent is retained on unknown absence.
            if (!await RemoveOwnedAsync(intent).ConfigureAwait(false)) throw new BrowserOperationException("WORKER_CONTAINER_CLEANUP_UNCONFIRMED");
            try { await _journal.RecordRemovalAsync(intent, false, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { throw new BrowserOperationException("WORKER_NATIVE_OWNERSHIP_UNCONFIRMED"); }
        }
        token.ThrowIfCancellationRequested();
        if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("MOCK_TASK_AUTHORITY_UNCONFIRMED");
        return new(start, collected, true);
    }

    internal static void ValidateMockContainer(JsonElement item, NativeOwnership intent)
    {
        var config = item.GetProperty("Config");
        var labels = config.GetProperty("Labels");
        var entry = config.GetProperty("Entrypoint");
        var fixedEntry = entry.ValueKind == JsonValueKind.String ? entry.GetString() == "dotnet"
            : entry.ValueKind == JsonValueKind.Array && entry.EnumerateArray().Select(e => e.GetString()).SequenceEqual(["dotnet"]);
        if (item.GetProperty("Image").GetString()?.Replace("sha256:", "", StringComparison.Ordinal) != intent.Image[7..]
            || !fixedEntry || !config.GetProperty("Cmd").EnumerateArray().Select(e => e.GetString())
                .SequenceEqual(["/app/cli/HeadlessScreenReader.Cli.dll", "--worker-mock-tasks"])
            || labels.GetProperty("hsr.observation.organization").GetString() != intent.Job.OrganizationId.ToString("N")
            || labels.GetProperty("hsr.observation.scan").GetString() != intent.Job.ScanId.ToString("N")
            || labels.GetProperty("hsr.observation.job").GetString() != intent.Job.JobId.ToString("N")
            || labels.GetProperty("hsr.observation.generation").GetString() != "1")
            throw new BrowserOperationException("MOCK_TASK_CONTAINER_IDENTITY_UNCONFIRMED");
    }
}
