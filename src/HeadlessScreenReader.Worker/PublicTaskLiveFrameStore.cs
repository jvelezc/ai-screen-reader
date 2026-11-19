using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Latest-frame cache kept outside originals, with one disposable file per owned execution.</summary>
public sealed class PublicTaskLiveFrameStore(string evidenceRoot)
{
    private readonly string _runtime = Path.Combine(Path.GetFullPath(evidenceRoot), ".public-task-runtime");

    internal string PathFor(Guid execution) => Path.Combine(_runtime, "live", $"{execution:N}.json");

    public async Task<PublicTaskLiveFrame?> ReadAsync(PublicTaskJob job, int attempt, DateTimeOffset now, CancellationToken token)
    {
        var fence = Path.Combine(_runtime, "executions", $"{job.OrganizationId:N}-{job.JobId:N}.json");
        var start = await TryReadAsync<PublicTaskStart>(fence, 16 * 1024, token).ConfigureAwait(false);
        if (start is null || start.Schema != PublicTaskStart.CurrentSchema || start.Job != job
            || start.ExecutionId == Guid.Empty || start.WorkerId == Guid.Empty) return null;
        var frame = await TryReadAsync<PublicTaskLiveFrame>(PathFor(start.ExecutionId), PublicTaskLiveFrame.MaximumEnvelopeBytes, token).ConfigureAwait(false);
        return frame?.IsCurrent(start, attempt, now) == true ? frame : null;
    }

    private static async Task<T?> TryReadAsync<T>(string path, int maximum, CancellationToken token) where T : class
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget is not null || info.Directory?.LinkTarget is not null) return null;
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (file.Length < 1 || file.Length > maximum) return null;
            var bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (file.ReadByte() != -1) return null;
            return JsonSerializer.Deserialize<T>(bytes, ReaderWorkerWire.Json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}

internal sealed class PublicTaskLiveFrameSink(PublicTaskStart start, string path) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _attempt, _sequence;

    internal async Task SetAttemptAsync(int attempt, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { _attempt = attempt; TryDelete(path); }
        finally { _gate.Release(); }
    }

    internal async Task ObserveAsync(byte[] bytes, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var frame = JsonSerializer.Deserialize<PublicTaskLiveFrame>(bytes, ReaderWorkerWire.Json);
            if (_attempt == 0 || frame is null || frame.Sequence <= _sequence || !frame.IsCurrent(start, _attempt, DateTimeOffset.UtcNow)) return;
            var folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);
            if (new DirectoryInfo(folder).LinkTarget is not null) return;
            var temporary = path + ".next";
            try
            {
                await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                    await file.WriteAsync(bytes, token).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
                _sequence = frame.Sequence;
            }
            finally { TryDelete(temporary); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { /* An unavailable live window never changes the worker's attempt or findings. */ }
        finally { _gate.Release(); }
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        TryDelete(path);
        TryDelete(path + ".next");
        _gate.Dispose();
    }
}
