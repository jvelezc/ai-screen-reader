using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    public async Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_publicPolicy is not null) throw new BrowserInputDeniedException("PUBLIC_PAGE_READ_ONLY_INPUT_DENIED");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _captureAdmission.RunAsync(async () =>
            {
                await PressListboxKeyCoreAsync(input).ConfigureAwait(false);
                return true;
            }, input: true).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception) { throw new BrowserOperationException("CHROMIUM_INPUT_TIMEOUT", exception); }
    }

    private async Task PressListboxKeyCoreAsync(ListboxKeyInput input)
    {
        // Authorization and dispatch share one admission; exclusion drains both.
        if (!input.IsAuthorizedBy(await CaptureCoreAsync().ConfigureAwait(false)))
            throw new BrowserInputDeniedException("TYPEAHEAD_SCOPE_CHANGED");
        try { await _page.Keyboard.PressAsync(input.Key).ConfigureAwait(false); }
        catch (PlaywrightException exception) { throw new BrowserOperationException("CHROMIUM_INPUT_FAILED", exception); }
    }
}
