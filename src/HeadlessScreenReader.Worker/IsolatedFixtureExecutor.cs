using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Fixed, immutable local image. Browser receives only the nonsecret descriptor on stdin.</summary>
public sealed partial class IsolatedFixtureExecutor : IFixtureExecutor
{
    private readonly string _runtime;
    private readonly string _image;
    private readonly string? _seccomp;
    private readonly OwnershipJournal _journal;
    public IsolatedFixtureExecutor(string runtime, string image, string? seccomp = null, OwnershipJournal? journal = null)
    {
        if (runtime is not ("podman" or "docker") || !ImageId().IsMatch(image))
            throw new ArgumentException("WORKER_RUNTIME_CONFIGURATION_INVALID");
        if (runtime == "docker" && (seccomp is null || !File.Exists(seccomp)))
            throw new ArgumentException("WORKER_SECCOMP_REQUIRED");
        _journal = journal ?? throw new ArgumentException("WORKER_OWNERSHIP_JOURNAL_REQUIRED");
        _runtime = runtime; _image = image; _seccomp = seccomp is null ? null : Path.GetFullPath(seccomp);
    }
    [GeneratedRegex("^sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex ImageId();

    public static IReadOnlyDictionary<string, string?> ChildEnvironment()
    {
        // Explicit allowlist: no Supabase, PG, worker connection string, runtime env-file or pass-through secrets.
        string[] names = ["PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "HOME", "USERPROFILE", "LOCALAPPDATA",
            "APPDATA", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS"];
        return names.Where(name => Environment.GetEnvironmentVariable(name) is not null)
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
    }

    public Task<byte[]> ExecuteAsync(FixtureJob job, CancellationToken token) => ExecuteCoreAsync(job, null, null, token);
    public Task<byte[]> ExecuteAsync(WorkerLease lease, Func<CancellationToken, Task<bool>> authorize, CancellationToken token) =>
        ExecuteCoreAsync(lease.Job, lease.WorkerId, authorize, token);
    private async Task<byte[]> ExecuteCoreAsync(FixtureJob job, Guid? worker, Func<CancellationToken, Task<bool>>? authorize, CancellationToken token)
    {
        job.Validate();
        NativeOwnership intent;
        // Native cleanup journals retain only the original four-part identity.
        // Recovery never executes work; older rollback binaries can still remove this exact owned container.
        try { intent = await _journal.BeginAsync(job with { RecordObservation = false, TraversalRoute = 0 }, _runtime, _image, token); }
        catch (Exception) { throw new BrowserOperationException("WORKER_NATIVE_OWNERSHIP_UNCONFIRMED"); }
        var name = intent.ContainerName;
        RecordedOutputJournal? output = null;
        try
        {
            if (job.RecordObservation && worker is { } recordingProducer && authorize is not null)
            {
                if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("WORKER_OUTPUT_AUTHORITY_UNCONFIRMED");
                try
                {
                    output = await RecordedOutputJournal.BeginAsync(Path.Combine(Path.GetDirectoryName(_journal.IntentsDirectory)!, "recorded-output"),
                        intent, job, recordingProducer, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { throw new BrowserOperationException("WORKER_OUTPUT_WRITE_UNCONFIRMED"); }
            }
            FixtureProgressExchange? progress = null;
            if (job.TraversalRoute > 0 && worker is { } producer && authorize is not null)
            {
                var journal = await CommandProgressJournal.BeginAsync(Path.Combine(Path.GetDirectoryName(_journal.IntentsDirectory)!, "command-progress"),
                    intent, job, producer, token);
                var sources = await RecordedSourceJournal.BeginAsync(Path.Combine(Path.GetDirectoryName(_journal.IntentsDirectory)!, "recorded-sources"),
                    intent, job, producer, token);
                progress = new(new(FixtureProgressProtocol.Schema, job, intent.Owner) { RetainSourceFrames = true }, journal, authorize, sources);
            }
            await CreateAndInspectAsync(intent, token, journaled: progress is not null);
            var result = await InvokeAsync(["start", "--attach", "--interactive", name],
                JsonSerializer.SerializeToUtf8Bytes(job), token, TimeSpan.FromSeconds(120),
                job.RecordObservation ? PassiveObservationRecording.MaximumBundleBytes : FixtureObservation.MaximumBytes, progress);
            if (result.Code != 0) throw new InvalidDataException("WORKER_CONTAINER_EXECUTION_UNCONFIRMED");
            // A valid observation also requires successful, independently confirmed container removal below.
            if (job.TraversalRoute > 0)
            {
                var bundle = KeyboardRecordingBundle.Decode(result.Bytes, job);
                if (progress is not null)
                {
                    progress.Match(bundle.Validate(job).Receipt);
                    progress.MatchSources(bundle);
                }
            }
            else if (job.RecordObservation) PassiveRecordingBundle.Decode(result.Bytes, job);
            else ObservationStore.Validate(result.Bytes, job);
            if (output is not null)
            {
                try { await output.SealAsync(result.Bytes, authorize!, token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { throw new BrowserOperationException("WORKER_OUTPUT_WRITE_UNCONFIRMED"); }
            }
            return result.Bytes;
        }
        finally
        {
            // Do not let caller cancellation cancel owned cleanup. Every command remains bounded.
            if (!await RemoveOwnedAsync(intent)) throw new BrowserOperationException("WORKER_CONTAINER_CLEANUP_UNCONFIRMED");
            try { await _journal.RecordRemovalAsync(intent, false, CancellationToken.None); }
            catch (Exception) { throw new BrowserOperationException("WORKER_NATIVE_OWNERSHIP_UNCONFIRMED"); }
            if (output is not null)
            {
                try { await output.AcknowledgeRemovalAsync(_journal, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { throw new BrowserOperationException("WORKER_OUTPUT_REMOVAL_WRITE_UNCONFIRMED"); }
            }
        }
    }

    private async Task CreateAndInspectAsync(NativeOwnership intent, CancellationToken token, bool reader = false, bool journaled = false)
    {
        var owner = intent.Owner.ToString("N");
        var name = intent.ContainerName;
        var job = intent.Job;
        List<string> create = ["create", "--pull", "never", "--interactive", "--name", name, "--network", "none", "--user", "1001:1001",
            "--read-only", "--cap-drop", "ALL", "--cap-add", "SYS_CHROOT", "--security-opt", "no-new-privileges",
            "--memory", "1024m", "--cpus", "2", "--pids-limit", "256", "--shm-size", "256m",
            "--tmpfs", "/tmp:rw,nosuid,size=256m,mode=1777", "--tmpfs", "/home/pwuser:rw,nosuid,size=64m,mode=1777",
            "--label", "hsr.observation.owner=" + owner,
            "--label", "hsr.observation.organization=" + job.OrganizationId.ToString("N"),
            "--label", "hsr.observation.scan=" + job.ScanId.ToString("N"),
            "--label", "hsr.observation.job=" + job.JobId.ToString("N"),
            "--label", "hsr.observation.generation=" + job.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        if (_runtime == "docker") create.AddRange(["--security-opt", "seccomp=" + _seccomp]);
        if (reader || journaled) create.AddRange(["--entrypoint", "dotnet"]);
        create.Add(_image);
        if (reader) create.AddRange(["/app/cli/HeadlessScreenReader.Cli.dll", "--reader-fixture"]);
        if (journaled) create.AddRange(["/app/cli/HeadlessScreenReader.Cli.dll", "--worker-fixture-progress"]);
        if ((await InvokeAsync(create, null, token)).Code != 0) throw new InvalidDataException("WORKER_CONTAINER_CREATE_UNCONFIRMED");
        var inspect = await InvokeAsync(["inspect", name], null, token);
        if (inspect.Code != 0) throw new InvalidDataException("WORKER_CONTAINER_INSPECTION_UNCONFIRMED");
        using (var document = JsonDocument.Parse(inspect.Bytes))
        {
            var item = document.RootElement[0];
            var host = item.GetProperty("HostConfig");
            var config = item.GetProperty("Config");
            if (config.GetProperty("Labels").GetProperty("hsr.observation.owner").GetString() != owner
                || config.GetProperty("User").GetString() != "1001:1001"
                || host.GetProperty("NetworkMode").GetString() != "none" || host.GetProperty("Privileged").GetBoolean()
                || !host.GetProperty("ReadonlyRootfs").GetBoolean() || host.GetProperty("PidsLimit").GetInt64() != 256
                || host.GetProperty("Memory").GetInt64() != 1_073_741_824
                || item.GetProperty("Mounts").EnumerateArray().Any(m => m.GetProperty("Type").GetString() is "bind" or "volume"))
                throw new InvalidDataException("WORKER_CONTAINER_ISOLATION_UNCONFIRMED");
            if (reader) ValidateReaderContainer(item, intent);
        }
    }

    internal static void ValidateReaderContainer(JsonElement item, NativeOwnership intent)
    {
        var config = item.GetProperty("Config");
        var labels = config.GetProperty("Labels");
        var entrypoint = config.GetProperty("Entrypoint");
        // Podman 4.9 serializes this as a string; Docker/newer Podman use an array.
        // Both must describe exactly the same fixed executable, with no shell parsing.
        var fixedEntrypoint = entrypoint.ValueKind switch
        {
            JsonValueKind.String => entrypoint.GetString() == "dotnet",
            JsonValueKind.Array => entrypoint.EnumerateArray()
                .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).SequenceEqual(["dotnet"]),
            _ => false
        };
        if (item.GetProperty("Image").GetString()?.Replace("sha256:", "", StringComparison.Ordinal) != intent.Image[7..]
            || labels.GetProperty("hsr.observation.organization").GetString() != intent.Job.OrganizationId.ToString("N")
            || labels.GetProperty("hsr.observation.scan").GetString() != intent.Job.ScanId.ToString("N")
            || labels.GetProperty("hsr.observation.job").GetString() != intent.Job.JobId.ToString("N")
            || labels.GetProperty("hsr.observation.generation").GetString() != "1"
            || !fixedEntrypoint
            || !config.GetProperty("Cmd").EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(["/app/cli/HeadlessScreenReader.Cli.dll", "--reader-fixture"]))
            throw new InvalidDataException("READER_CONTAINER_IDENTITY_UNCONFIRMED");
    }

    internal async Task<IsolatedReaderSession> OpenReaderAsync(FixtureJob descriptor, Guid session, CancellationToken token)
    {
        descriptor.Validate();
        var intent = await _journal.BeginAsync(descriptor, _runtime, _image, token).ConfigureAwait(false);
        IsolatedReaderSession? connection = null;
        async Task RemoveAsync()
        {
            if (!await RemoveOwnedAsync(intent).ConfigureAwait(false))
                throw new BrowserOperationException("READER_CONTAINER_CLEANUP_UNCONFIRMED");
            await _journal.RecordRemovalAsync(intent, false, CancellationToken.None).ConfigureAwait(false);
        }
        try
        {
            await CreateAndInspectAsync(intent, token, reader: true).ConfigureAwait(false);
            var start = new ProcessStartInfo(_runtime)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "start", "--attach", "--interactive", intent.ContainerName }) start.ArgumentList.Add(arg);
            start.Environment.Clear();
            foreach (var pair in ChildEnvironment()) start.Environment[pair.Key] = pair.Value;
            connection = new IsolatedReaderSession(start, session, RemoveAsync);
            await connection.OpenAsync(token).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            else await RemoveAsync().ConfigureAwait(false);
            throw new BrowserOperationException("READER_CONTAINER_OPEN_UNCONFIRMED");
        }
    }

    public async Task<int> RecoverAsync()
    {
        var intents = _journal.ReadPending();
        if (intents.Any(intent => intent.Runtime != _runtime)) throw new BrowserOperationException("WORKER_RECOVERY_RUNTIME_MISMATCH");
        foreach (var intent in intents)
        {
            if (!await RemoveOwnedAsync(intent)) throw new BrowserOperationException("WORKER_CONTAINER_CLEANUP_UNCONFIRMED");
            await _journal.RecordRemovalAsync(intent, true, CancellationToken.None);
        }
        using var repair = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await RecordedOutputJournal.RecoverRemovalsAsync(Path.Combine(Path.GetDirectoryName(_journal.IntentsDirectory)!, "recorded-output"),
            _journal, repair.Token).ConfigureAwait(false);
        return intents.Count;
    }

    /// <summary>Reconcile one failed reader launch after its attach client has settled, without touching sibling intents.</summary>
    internal async Task<int> RecoverReaderAsync(FixtureJob descriptor)
    {
        descriptor.Validate();
        if (descriptor.ScanId != descriptor.JobId || descriptor.Generation != 1)
            throw new ArgumentException("READER_RECOVERY_SCOPE_INVALID");
        var intents = _journal.ReadPending().Where(intent => intent.Job.ScanId == descriptor.ScanId
            || intent.Job.JobId == descriptor.JobId).ToArray();
        if (intents.Any(intent => intent.Runtime != _runtime || intent.Job != descriptor))
            throw new BrowserOperationException("READER_RECOVERY_SCOPE_MISMATCH");
        foreach (var intent in intents)
        {
            if (!await RemoveOwnedAsync(intent).ConfigureAwait(false))
                throw new BrowserOperationException("READER_CONTAINER_CLEANUP_UNCONFIRMED");
            await _journal.RecordRemovalAsync(intent, true, CancellationToken.None).ConfigureAwait(false);
        }
        return intents.Length;
    }

    private async Task<bool> RemoveOwnedAsync(NativeOwnership intent)
    {
        try
        {
            var name = intent.ContainerName;
            var inspect = await InvokeAsync(["inspect", name], null, CancellationToken.None);
            if (inspect.Code == 0)
            {
                using var document = JsonDocument.Parse(inspect.Bytes);
                var item = document.RootElement[0];
                var labels = item.GetProperty("Config").GetProperty("Labels");
                if (labels.GetProperty("hsr.observation.owner").GetString() != intent.Owner.ToString("N")
                    || labels.GetProperty("hsr.observation.organization").GetString() != intent.Job.OrganizationId.ToString("N")
                    || labels.GetProperty("hsr.observation.scan").GetString() != intent.Job.ScanId.ToString("N")
                    || labels.GetProperty("hsr.observation.job").GetString() != intent.Job.JobId.ToString("N")
                    || labels.GetProperty("hsr.observation.generation").GetString() != intent.Job.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    || item.GetProperty("Image").GetString()?.Replace("sha256:", "", StringComparison.Ordinal) != intent.Image[7..]) return false;
                if ((await InvokeAsync(["rm", "--force", "--volumes", name], null, CancellationToken.None)).Code != 0) return false;
            }
            var remaining = await InvokeAsync(["ps", "--all", "--filter", "name=^" + name + "$", "--format", "{{.ID}}"], null, CancellationToken.None);
            return remaining.Code == 0 && string.IsNullOrWhiteSpace(System.Text.Encoding.UTF8.GetString(remaining.Bytes));
        }
        catch { return false; }
    }

    private async Task<(int Code, byte[] Bytes)> InvokeAsync(IEnumerable<string> args, byte[]? input,
        CancellationToken token, TimeSpan? budget = null, int maximumOutputBytes = FixtureObservation.MaximumBytes, FixtureProgressExchange? progress = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(budget ?? TimeSpan.FromSeconds(20));
        var start = new ProcessStartInfo(_runtime)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment.Clear();
        foreach (var item in ChildEnvironment()) start.Environment[item.Key] = item.Value;
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("WORKER_RUNTIME_START_UNCONFIRMED");
        using var kill = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (Exception) { } });
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout, maximumOutputBytes);
        Task errors = progress is null ? ReadBoundedAsync(process.StandardError.BaseStream, timeout)
            : progress.ReadAsync(process.StandardError.BaseStream, process.StandardInput.BaseStream, timeout);
        try
        {
            if (progress is not null) await progress.StartAsync(process.StandardInput.BaseStream, timeout.Token);
            else
            {
                if (input is not null) await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token);
                process.StandardInput.Close();
            }
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token));
            return (process.ExitCode, await output);
        }
        catch (Exception failure)
        {
            await timeout.CancelAsync();
            // Settle actual pipe reads and the owned runtime client, including cancellation/error paths.
            try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(output, errors); } catch (Exception) { }
            if (failure is BrowserOperationException progressFailure && progressFailure.Code.StartsWith("WORKER_PROGRESS_", StringComparison.Ordinal))
                throw new BrowserOperationException(progressFailure.Code);
            throw new IOException("WORKER_RUNTIME_UNCONFIRMED");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationTokenSource stop, int maximumBytes = FixtureObservation.MaximumBytes)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, stop.Token);
            if (count == 0) return bytes.ToArray();
            if (bytes.Length + count > maximumBytes)
            {
                await stop.CancelAsync();
                throw new InvalidDataException("WORKER_RUNTIME_OUTPUT_TOO_LARGE");
            }
            bytes.Write(buffer, 0, count);
        }
    }
}
