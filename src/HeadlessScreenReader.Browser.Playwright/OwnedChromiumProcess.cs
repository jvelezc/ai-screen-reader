using System.Diagnostics;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>Observes one privately launched root; Windows stop uses its retained handle. No name lookup, tree walk or directory deletion.</summary>
internal sealed class OwnedChromiumProcess : IDisposable
{
    private readonly Process _process;
    private readonly Task _exited;
    private OwnedChromiumProcess(Process process)
    {
        _process = process;
        // Retain the process handle for the lifetime of this CDP-discovered root watch.
        _ = process.Handle;
        // Process.Exited uses the shared thread pool. Under contention its callback and
        // an asynchronous TCS continuation can observe exit after our native deadline.
        // Wait on the retained kernel handle on a dedicated thread instead. The task
        // permits the shutdown observer to run inline on that thread after actual exit.
        _exited = Task.Factory.StartNew(process.WaitForExit, CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        _ = _exited.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }
    public Task Exited => _exited;
    public int ProcessId => _process.Id;
    public Func<Task>? WindowsRootStop => OperatingSystem.IsWindows() ? StopWindowsRootAsync : null;
    private Task StopWindowsRootAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new BrowserOperationException("CHROMIUM_ROOT_STOP_UNSUPPORTED");
        // Process.Handle was retained at attachment. Windows Kill() reuses that exact handle,
        // without reopening a PID or enumerating a process tree. A return is not exit proof.
        if (!_exited.IsCompleted) _process.Kill();
        return Task.CompletedTask;
    }
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
    public void Dispose() { _process.Dispose(); }
}
