using System.Diagnostics;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>Observes one privately launched root; Windows stop uses its retained handle. No name lookup, tree walk or directory deletion.</summary>
internal sealed class OwnedChromiumProcess : IDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OwnedChromiumProcess(Process process)
    {
        _process = process;
        // Retain the process handle for the lifetime of this CDP-discovered root watch.
        _ = process.Handle;
        process.Exited += OnExited;
        process.EnableRaisingEvents = true;
        if (process.HasExited) _exited.TrySetResult();
    }
    public Task Exited => _exited.Task;
    public int ProcessId => _process.Id;
    public Func<Task>? WindowsRootStop => OperatingSystem.IsWindows() ? StopWindowsRootAsync : null;
    private Task StopWindowsRootAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new BrowserOperationException("CHROMIUM_ROOT_STOP_UNSUPPORTED");
        // Process.Handle was retained at attachment. Windows Kill() reuses that exact handle,
        // without reopening a PID or enumerating a process tree. A return is not exit proof.
        if (!_exited.Task.IsCompleted) _process.Kill();
        return Task.CompletedTask;
    }
    private void OnExited(object? sender, EventArgs args) => _exited.TrySetResult();
    public static async Task<OwnedChromiumProcess> AttachAsync(IBrowser browser)
    {
        var session = await browser.NewBrowserCDPSessionAsync().ConfigureAwait(false);
        Process? process = null;
        OwnedChromiumProcess? watch = null;
        try
        {
            try
            {
                var data = await session.SendAsync("SystemInfo.getProcessInfo").ConfigureAwait(false);
                var items = data!.Value.GetProperty("processInfo").EnumerateArray().ToArray();
                if (items.Length is < 1 or > 128) throw new InvalidOperationException();
                var pid = items.Single(i => i.GetProperty("type").GetString() == "browser").GetProperty("id").GetInt64();
                if (pid is <= 0 or > int.MaxValue) throw new InvalidOperationException();
                process = Process.GetProcessById((int)pid);
                watch = new(process);
                // Reconfirm through the same owned browser after taking the handle. A root that
                // vanished before attachment cannot lend stop authority to a reused numeric PID.
                var confirmation = await session.SendAsync("SystemInfo.getProcessInfo").ConfigureAwait(false);
                var confirmedPid = confirmation!.Value.GetProperty("processInfo").EnumerateArray()
                    .Single(item => item.GetProperty("type").GetString() == "browser").GetProperty("id").GetInt64();
                if (confirmedPid != pid || watch.Exited.IsCompleted) throw new InvalidOperationException();
            }
            finally { await session.DetachAsync().ConfigureAwait(false); }
            return watch;
        }
        catch { if (watch is not null) watch.Dispose(); else process?.Dispose(); throw new BrowserOperationException("CHROMIUM_PROCESS_WATCH_UNCONFIRMED"); }
    }
    public void Dispose() { _process.Exited -= OnExited; _process.Dispose(); }
}
