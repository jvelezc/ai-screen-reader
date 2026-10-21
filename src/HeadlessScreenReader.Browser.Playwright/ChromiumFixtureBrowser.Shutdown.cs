using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser : IBrowserShutdownDiagnostics
{
    private readonly object _disposeSync = new();
    private readonly ChromiumShutdown _shutdown;
    private Task? _disposeTask;
    public BrowserShutdownStatus ShutdownStatus => _shutdown.Status;
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            _ = _disposeTask.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            _disposed = true;
            _ = DisposeCoreAsync(completion);
            return new(_disposeTask);
        }
    }
    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            await _captureAdmission.CloseAsync(_shutdown.CloseAsync).ConfigureAwait(false);
        }
        catch (Exception exception) { failure = exception; }
        try { _nativeProcess.Dispose(); }
        catch { failure ??= new BrowserOperationException("CHROMIUM_PROCESS_WATCH_RELEASE_UNCONFIRMED"); }
        if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
    }
}
