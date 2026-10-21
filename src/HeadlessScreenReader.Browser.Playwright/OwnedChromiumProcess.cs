using System.Diagnostics;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>Observes one privately launched browser process. No process-name lookup, kill or directory deletion.</summary>
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
            }
            finally { await session.DetachAsync().ConfigureAwait(false); }
            return watch;
        }
        catch { if (watch is not null) watch.Dispose(); else process?.Dispose(); throw new BrowserOperationException("CHROMIUM_PROCESS_WATCH_UNCONFIRMED"); }
    }
    public void Dispose() { _process.Exited -= OnExited; _process.Dispose(); }
}
