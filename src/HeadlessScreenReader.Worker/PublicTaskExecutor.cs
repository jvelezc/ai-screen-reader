using System.Diagnostics;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record PublicTaskNativeResult(PublicTaskStart Start, PublicTaskBundle Bundle, bool ResourcesAbsent);

/// <summary>One-shot isolated public task driver; neither database nor S3 credentials enter its child.</summary>
public sealed class PublicTaskExecutor : IDisposable
{
    private readonly string _root, _scripts, _runtime, _image;
    private readonly PublicReaderOwnershipJournal _journal;
    private readonly FileStream _exclusive;
    public PublicTaskExecutor(string root, string scripts, string runtime, string image)
    {
        _root = Path.Combine(Path.GetFullPath(root), ".public-task-runtime");
        _scripts = Path.GetFullPath(scripts); _runtime = runtime; _image = image;
        PublicReaderOwnershipJournal.Validate(new(PublicReaderOwnership.CurrentSchema, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), runtime, image,
            PublicReaderSource.FromUrl("https://screenreader.privensic.com/ci/v1/source")));
        _journal = new(_root); _exclusive = _journal.AcquireExclusive();
    }
    public async Task<int> RecoverAsync()
    {
        var pending = _journal.ReadPending();
        foreach (var intent in pending)
        {
            if (intent.Runtime != _runtime) throw new BrowserOperationException("PUBLIC_TASK_RECOVERY_UNCONFIRMED");
            await RemoveAsync(intent, true).ConfigureAwait(false);
        }
        return pending.Count;
    }

    public async Task<PublicTaskNativeResult> ExecuteAsync(PublicTaskJob job, Guid worker,
        Func<string, CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        job.Validate();
        if (worker == Guid.Empty || !await authorize("Starting", token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_TASK_AUTHORITY_LOST");
        var intent = await _journal.BeginAsync(job.OrganizationId, job.JobId, job.Source, _runtime, _image, token).ConfigureAwait(false);
        var start = new PublicTaskStart(PublicTaskStart.CurrentSchema, job, worker, intent.Owner);
        PublicTaskBundle collected;
        try
        {
            var fenceRoot = Path.Combine(_root, "executions"); Directory.CreateDirectory(fenceRoot);
            await using (var fence = new FileStream(Path.Combine(fenceRoot, $"{job.OrganizationId:N}-{job.JobId:N}.json"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await JsonSerializer.SerializeAsync(fence, start, ReaderWorkerWire.Json, token).ConfigureAwait(false); await fence.FlushAsync(token).ConfigureAwait(false); fence.Flush(true); }
            var info = Launch("Start-IsolatedPublicReader.ps1", intent);
            foreach (var arg in new[] { "-AllowedHost", job.Source.AllowedHost, "-TargetUrl", job.Source.TargetUrl, "-NavigationTask" }) info.ArgumentList.Add(arg);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(190));
            using var process = new Process { StartInfo = info };
            if (!process.Start()) throw new BrowserOperationException("PUBLIC_TASK_PROCESS_UNCONFIRMED");
            using var kill = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
            var progress = new PublicTaskProgress(start, Path.Combine(_root, "control"), authorize);
            var output = ReadAsync(process.StandardOutput.BaseStream, PublicTaskReport.MaximumArchiveBytes, deadline.Token);
            var control = ControlAsync();
            await ReaderWorkerWire.WriteAsync(process.StandardInput.BaseStream, JsonSerializer.SerializeToUtf8Bytes(start, ReaderWorkerWire.Json), 16 * 1024, deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, control, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new BrowserOperationException("PUBLIC_TASK_COLLECTION_UNCONFIRMED");
            collected = PublicTaskBundle.Decode(await output.ConfigureAwait(false), start); progress.Validate(collected.Report);
            if (!await authorize("Finalizing", token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_TASK_AUTHORITY_LOST");

            async Task ControlAsync()
            {
                try
                {
                    while (await ReaderWorkerWire.ReadAsync(process.StandardError.BaseStream, PublicTaskDiagnostic.MaximumBytes, deadline.Token).ConfigureAwait(false) is { } bytes)
                    {
                        using var raw = JsonDocument.Parse(bytes);
                        if (raw.RootElement.TryGetProperty("Stage", out _))
                        {
                            var diagnostic = JsonSerializer.Deserialize<PublicTaskDiagnostic>(bytes, ReaderWorkerWire.Json);
                            if (diagnostic is null || diagnostic.Start != start || diagnostic.Attempt is < 0 or > 3
                                || diagnostic.Stage is not ("Start" or "Isolation" or "Open" or "Discover" or "Snapshot" or "Input" or "Recording" or "Report")
                                || diagnostic.Category is not ("Browser" or "Playwright" or "Protocol" or "Timeout" or "Io" or "Canceled" or "Unexpected")
                                || diagnostic.Code is { } code && (code.Length > 100 || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_'))) throw new IOException();
                            diagnostic.Network?.Validate(); diagnostic.Egress?.Validate();
                            diagnostic.Failure?.Validate(PublicTaskJob.MaximumTabs + 1);
                            var folder = Path.Combine(_root, "diagnostics"); Directory.CreateDirectory(folder);
                            await File.WriteAllBytesAsync(Path.Combine(folder, $"{start.ExecutionId:N}-{Guid.NewGuid():N}.json"), bytes, deadline.Token).ConfigureAwait(false);
                            Console.Error.WriteLine(JsonSerializer.Serialize(new
                            {
                                Schema = "hsr-public-task-diagnostic-v1",
                                job.OrganizationId,
                                job.JobId,
                                start.ExecutionId,
                                diagnostic.Attempt,
                                diagnostic.Stage,
                                diagnostic.Category,
                                diagnostic.Code
                            }));
                            continue;
                        }
                        await progress.HandleAsync(bytes, process.StandardInput.BaseStream, deadline.Token).ConfigureAwait(false);
                        if (progress.Published) process.StandardInput.Close();
                    }
                }
                catch { await deadline.CancelAsync().ConfigureAwait(false); throw; }
            }
        }
        finally { await RemoveAsync(intent, false).ConfigureAwait(false); }
        token.ThrowIfCancellationRequested(); return new(start, collected, true);
    }

    private ProcessStartInfo Launch(string name, PublicReaderOwnership intent)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment.Clear(); foreach (var value in IsolatedFixtureExecutor.ChildEnvironment()) start.Environment[value.Key] = value.Value;
        if (OperatingSystem.IsWindows()) start.Environment["PATHEXT"] = ".EXE";
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(_scripts, name),
            "-Runtime", intent.Runtime, "-OwnerToken", intent.Owner.ToString("N"), "-Image", intent.Image }) start.ArgumentList.Add(arg);
        return start;
    }
    private async Task RemoveAsync(PublicReaderOwnership intent, bool recovered)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var process = new Process { StartInfo = Launch("Stop-IsolatedPublicReader.ps1", intent) };
        if (!process.Start()) throw new BrowserOperationException("PUBLIC_TASK_CLEANUP_UNCONFIRMED");
        process.StandardInput.Close();
        using var kill = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var output = ReadAsync(process.StandardOutput.BaseStream, 16 * 1024, deadline.Token);
        var errors = ReadAsync(process.StandardError.BaseStream, 16 * 1024, deadline.Token);
        await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
        var proof = JsonSerializer.Deserialize<PublicReaderResourceRemoval>(await output.ConfigureAwait(false), ReaderWorkerWire.Json);
        if (process.ExitCode != 0 || (await errors.ConfigureAwait(false)).Length != 0 || proof is null || proof.Owner != intent.Owner
            || proof.Schema != "hsr-public-reader-resource-removal-v1" || proof.Runtime != intent.Runtime || proof.Image != intent.Image
            || !proof.ContainersAbsent || !proof.NetworksAbsent) throw new BrowserOperationException("PUBLIC_TASK_CLEANUP_UNCONFIRMED");
        await _journal.RecordRemovalAsync(intent, recovered, CancellationToken.None).ConfigureAwait(false);
    }
    private static async Task<byte[]> ReadAsync(Stream source, int limit, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, token).ConfigureAwait(false); if (read == 0) return output.ToArray();
            if (output.Length + read > limit) throw new BrowserOperationException("PUBLIC_TASK_OUTPUT_TOO_LARGE");
            output.Write(buffer, 0, read);
        }
    }
    public void Dispose() => _exclusive.Dispose();
}
