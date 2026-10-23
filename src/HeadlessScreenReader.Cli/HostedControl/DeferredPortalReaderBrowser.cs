using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Reserves an owner before invoking the legacy local factory. An opaque launch failure proves no cleanup.</summary>
internal sealed class DeferredPortalReaderBrowser(Func<Task<IPortalReaderBrowser>> open) : IPortalReaderBrowser
{
    private Task? _initialization;
    private IPortalReaderBrowser? _browser;
    public int InitialCaptures => _browser?.InitialCaptures ?? 0;
    public bool CleanupConfirmed { get; private set; }
    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        _browser = await open().ConfigureAwait(false);
        await _browser.InitializeAsync().ConfigureAwait(false);
    }

    public Task<AccessibilitySnapshot> CaptureAsync() => (_browser ?? throw new InvalidOperationException()).CaptureAsync();

    public async ValueTask DisposeAsync()
    {
        if (_initialization is null) { CleanupConfirmed = true; return; }
        try { await _initialization.ConfigureAwait(false); }
        catch when (_browser is not null) { /* The allocated owner can still prove cleanup after failed initialization. */ }
        if (_browser is null) throw new InvalidOperationException("CONTROL_READER_LAUNCH_CLEANUP_UNCONFIRMED");
        try
        {
            await _browser.DisposeAsync().ConfigureAwait(false);
            CleanupConfirmed = true;
        }
        finally { CleanupConfirmed |= _browser.CleanupConfirmed; }
    }
}
