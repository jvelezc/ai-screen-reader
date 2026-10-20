using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    public async Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_publicPolicy is not null) throw new BrowserInputDeniedException("PUBLIC_PAGE_READ_ONLY_INPUT_DENIED");
        if (!input.IsAuthorizedBy(await CaptureAsync(cancellationToken).ConfigureAwait(false)))
            throw new BrowserInputDeniedException("TYPEAHEAD_SCOPE_CHANGED");
        cancellationToken.ThrowIfCancellationRequested();
        try { await _page.Keyboard.PressAsync(input.Key).ConfigureAwait(false); }
        catch (PlaywrightException exception) { throw new BrowserOperationException("CHROMIUM_INPUT_FAILED", exception); }
    }
}
