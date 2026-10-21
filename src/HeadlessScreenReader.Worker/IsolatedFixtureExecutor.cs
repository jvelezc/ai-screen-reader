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
    public IsolatedFixtureExecutor(string runtime, string image, string? seccomp = null)
    {
        if (runtime is not ("podman" or "docker") || !ImageId().IsMatch(image))
            throw new ArgumentException("WORKER_RUNTIME_CONFIGURATION_INVALID");
        if (runtime == "docker" && (seccomp is null || !File.Exists(seccomp)))
            throw new ArgumentException("WORKER_SECCOMP_REQUIRED");
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

    public async Task<byte[]> ExecuteAsync(FixtureJob job, CancellationToken token)
    {
        job.Validate();
        var owner = Guid.NewGuid().ToString("N");
        var name = "hsr-observation-" + owner;
        try
        {
            List<string> create = ["create", "--pull", "never", "--interactive", "--name", name, "--network", "none", "--user", "1001:1001",
                "--read-only", "--cap-drop", "ALL", "--cap-add", "SYS_CHROOT", "--security-opt", "no-new-privileges",
                "--memory", "1024m", "--cpus", "2", "--pids-limit", "256", "--shm-size", "256m",
                "--tmpfs", "/tmp:rw,nosuid,size=256m,mode=1777", "--tmpfs", "/home/pwuser:rw,nosuid,size=64m,mode=1777",
                "--label", "hsr.observation.owner=" + owner];
            if (_runtime == "docker") create.AddRange(["--security-opt", "seccomp=" + _seccomp]);
            create.Add(_image);
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
            }
            var result = await InvokeAsync(["start", "--attach", "--interactive", name],
                JsonSerializer.SerializeToUtf8Bytes(job), token, TimeSpan.FromSeconds(120));
            if (result.Code != 0) throw new InvalidDataException("WORKER_CONTAINER_EXECUTION_UNCONFIRMED");
            // A valid observation also requires successful, independently confirmed container removal below.
            ObservationStore.Validate(result.Bytes, job);
            return result.Bytes;
        }
        finally
        {
            // Do not let caller cancellation cancel owned cleanup. Every command remains bounded.
            if (!await RemoveOwnedAsync(name, owner)) throw new BrowserOperationException("WORKER_CONTAINER_CLEANUP_UNCONFIRMED");
        }
    }

    private async Task<bool> RemoveOwnedAsync(string name, string owner)
    {
        try
        {
            var inspect = await InvokeAsync(["inspect", name], null, CancellationToken.None);
            if (inspect.Code == 0)
            {
                using var document = JsonDocument.Parse(inspect.Bytes);
                if (document.RootElement[0].GetProperty("Config").GetProperty("Labels")
                    .GetProperty("hsr.observation.owner").GetString() != owner) return false;
                if ((await InvokeAsync(["rm", "--force", "--volumes", name], null, CancellationToken.None)).Code != 0) return false;
            }
            var remaining = await InvokeAsync(["ps", "--all", "--filter", "name=^" + name + "$", "--format", "{{.ID}}"], null, CancellationToken.None);
            return remaining.Code == 0 && string.IsNullOrWhiteSpace(System.Text.Encoding.UTF8.GetString(remaining.Bytes));
        }
        catch { return false; }
    }

    private async Task<(int Code, byte[] Bytes)> InvokeAsync(IEnumerable<string> args, byte[]? input,
        CancellationToken token, TimeSpan? budget = null)
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
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout);
        var errors = ReadBoundedAsync(process.StandardError.BaseStream, timeout);
        try
        {
            if (input is not null) await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token);
            process.StandardInput.Close();
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token));
            return (process.ExitCode, await output);
        }
        catch
        {
            await timeout.CancelAsync();
            // Settle actual pipe reads and the owned runtime client, including cancellation/error paths.
            try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(output, errors); } catch (Exception) { }
            throw new IOException("WORKER_RUNTIME_UNCONFIRMED");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationTokenSource stop)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, stop.Token);
            if (count == 0) return bytes.ToArray();
            if (bytes.Length + count > FixtureObservation.MaximumBytes)
            {
                await stop.CancelAsync();
                throw new InvalidDataException("WORKER_RUNTIME_OUTPUT_TOO_LARGE");
            }
            bytes.Write(buffer, 0, count);
        }
    }
}
