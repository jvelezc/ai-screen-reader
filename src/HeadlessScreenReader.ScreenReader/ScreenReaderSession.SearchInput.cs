using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

public sealed partial class ScreenReaderSession
{
    private async Task<ReaderStep> EnterSearchTextAsync(CancellationToken token)
    {
        if (_mode != ReaderMode.Focus || _browser is not ISyntheticSearchBrowser browser)
            return Result(ReaderOutcome.Unsupported, "Synthetic search entry requires focus mode and an explicitly supported browser.");
        var query = SyntheticSearchPolicy.Query;
        var scope = new SearchKeyInput(query[..1], _snapshot!.DocumentId, _snapshot.CaptureEpoch,
            _snapshot.FocusedNode?.Id ?? "", "");
        if (!scope.IsAuthorizedBy(_snapshot))
            return Result(ReaderOutcome.Denied, "An editable, unprotected search field must have actual focus.", errorCode: "SEARCH_SCOPE_CHANGED");
        try
        {
            foreach (var character in query)
            {
                scope = scope with { Key = character.ToString(), ExpectedPrefix = query[.._completedKeystrokes] };
                if (!scope.IsAuthorizedBy(_snapshot!))
                    throw new BrowserInputDeniedException("SEARCH_SCOPE_CHANGED");
                token.ThrowIfCancellationRequested();
                _inputAttempted = true;
                _text.Clear();
                Record(ReaderEventKind.InputAttempted, scope.Key, _snapshot!.FocusedNode?.Id);
                await browser.PressSearchKeyAsync(scope, token).ConfigureAwait(false);
                _completedKeystrokes++;
                Record(ReaderEventKind.InputCompleted, scope.Key, _snapshot.FocusedNode?.Id);
                await RefreshAsync(token).ConfigureAwait(false);
                _cursorId = _snapshot!.EffectiveFocusedNode?.Id;
            }
            var verified = scope.IsAuthorizedBy(_snapshot!)
                && await browser.SearchValueMatchesAsync(scope, query, token).ConfigureAwait(false);
            return Result(verified ? ReaderOutcome.Observed : ReaderOutcome.Unknown,
                verified ? "Synthetic search text entered. Search has not been submitted."
                    : "Search text was dispatched, but the field value could not be confirmed.", inputMayHaveOccurred: !verified)
                with
            { SearchInput = new(SyntheticSearchPolicy.Profile, query, _completedKeystrokes, verified) };
        }
        catch (BrowserInputDeniedException exception)
        {
            Record(ReaderEventKind.InputDenied, exception.Code);
            return Result(_completedKeystrokes == 0 ? ReaderOutcome.Denied : ReaderOutcome.Unknown,
                "Search entry stopped; remaining characters were not dispatched.", errorCode: exception.Code,
                inputMayHaveOccurred: _completedKeystrokes > 0)
                with
            { SearchInput = new(SyntheticSearchPolicy.Profile, query, _completedKeystrokes, false) };
        }
    }
}
