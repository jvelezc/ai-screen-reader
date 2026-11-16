using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser : ISyntheticSearchBrowser
{
    public async Task PressSearchKeyAsync(SearchKeyInput input, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSyntheticSearchFixture();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _captureAdmission.RunAsync(async () =>
            {
                var snapshot = (await CaptureCoreAsync().ConfigureAwait(false)) with { CaptureEpoch = _captureAdmission.Status.Epoch };
                if (!input.IsAuthorizedBy(snapshot)
                    || !await SearchFieldMatchesAsync(input.ExpectedPrefix).ConfigureAwait(false))
                    throw new BrowserInputDeniedException("SEARCH_SCOPE_CHANGED");
                using var grant = _syntheticSearchAuthority is null ? null : await AuthorizeSyntheticSearchInputAsync(
                    SyntheticSearchInputKind.Character, input.Key == " " ? "Space" : input.Key,
                    input.ExpectedPrefix.Length + 1, snapshot, input.NodeId, cancellationToken).ConfigureAwait(false);
                if (grant is not null && !await SearchFieldMatchesAsync(input.ExpectedPrefix).ConfigureAwait(false))
                    throw new BrowserInputDeniedException("SEARCH_SCOPE_CHANGED");
                cancellationToken.ThrowIfCancellationRequested();
                grant?.EnsureCurrent();
                try { await _page.Keyboard.PressAsync(input.Key == " " ? "Space" : input.Key).ConfigureAwait(false); }
                catch (PlaywrightException exception) { throw new BrowserOperationException("CHROMIUM_INPUT_FAILED", exception); }
                return true;
            }, input: true).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception) { throw new BrowserOperationException("CHROMIUM_INPUT_TIMEOUT", exception); }
    }

    public async Task<bool> SearchValueMatchesAsync(SearchKeyInput scope, string expected, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSyntheticSearchFixture();
        if (expected != SyntheticSearchPolicy.Query) return false;
        cancellationToken.ThrowIfCancellationRequested();
        return await _captureAdmission.RunAsync(async () =>
        {
            var snapshot = (await CaptureCoreAsync().ConfigureAwait(false)) with { CaptureEpoch = _captureAdmission.Status.Epoch };
            return scope.IsAuthorizedBy(snapshot) && await SearchFieldMatchesAsync(expected).ConfigureAwait(false);
        }).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureSyntheticSearchFixture()
    {
        if (_publicPolicy is not null || Environment.Content?.Fixture is not
            (BundledFixture.MockSearchWorkingV1 or BundledFixture.MockSearchKeyboardBlockedV1))
            throw new BrowserInputDeniedException("SYNTHETIC_SEARCH_FIXTURE_REQUIRED");
    }

    private Task<bool> SearchFieldMatchesAsync(string expected) => _page.EvaluateAsync<bool>(
        "expected => { const e=document.activeElement; return e instanceof HTMLInputElement && e.id==='sample-search' && e.type==='search' && !e.disabled && !e.readOnly && e.value===expected; }", expected);
}
