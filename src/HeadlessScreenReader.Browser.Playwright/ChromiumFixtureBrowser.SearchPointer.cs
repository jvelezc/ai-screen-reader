using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    public async Task<SearchFocusReceipt> FocusSyntheticSearchAsync(CancellationToken cancellationToken = default)
    {
        EnsureSyntheticSearchFixture();
        var started = DateTimeOffset.UtcNow;
        var before = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        AccessibilitySnapshot? after = null;
        var attempted = false; var completed = false; string? error = null;
        try
        {
            await _captureAdmission.RunAsync(async () =>
            {
                var current = (await CaptureCoreAsync().ConfigureAwait(false)) with { CaptureEpoch = _captureAdmission.Status.Epoch };
                if (current.DocumentId != before.DocumentId || current.CaptureEpoch != before.CaptureEpoch
                    || SyntheticSearchPolicy.KeyboardPlan("Enter").Destination.IsSatisfiedBy(current)
                    || !await _page.EvaluateAsync<bool>("() => { const e=document.getElementById('sample-search'); return e instanceof HTMLInputElement && e.type==='search' && !e.disabled && !e.readOnly && e.value===''; }").ConfigureAwait(false))
                    throw new BrowserInputDeniedException("SYNTHETIC_SEARCH_FRESH_STATE_REQUIRED");
                cancellationToken.ThrowIfCancellationRequested();
                attempted = true;
                await _page.GetByLabel("Search courses", new() { Exact = true }).ClickAsync().ConfigureAwait(false);
                completed = true;
                return true;
            }, input: true).ConfigureAwait(false);
            after = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BrowserInputDeniedException exception) { error = exception.Code; }
        catch (OperationCanceledException) { error = "SYNTHETIC_SEARCH_FOCUS_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch (PlaywrightException) { error = "SYNTHETIC_SEARCH_FOCUS_UNCONFIRMED"; }
        var observed = error is null && completed && after is not null && after.DocumentId == before.DocumentId
            && after.CaptureEpoch == before.CaptureEpoch
            && after.FocusedNode is { Role: "searchbox", Name: "Search courses", IsDisabled: false };
        return new(started, before, after, attempted, completed, observed, error);
    }

    public async Task<PointerTaskReceipt> ObserveSyntheticSearchPointerAsync(SearchKeyInput scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        EnsureSyntheticSearchFixture();
        var started = DateTimeOffset.UtcNow;
        var before = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        AccessibilitySnapshot? after = null;
        var attempted = false; var completed = false; string? error = null;
        var goal = SyntheticSearchPolicy.KeyboardPlan("Enter").Destination;
        try
        {
            await _captureAdmission.RunAsync(async () =>
            {
                var current = (await CaptureCoreAsync().ConfigureAwait(false)) with { CaptureEpoch = _captureAdmission.Status.Epoch };
                if (!scope.IsAuthorizedBy(current) || goal.IsSatisfiedBy(current)
                    || !await SearchFieldMatchesAsync(SyntheticSearchPolicy.Query).ConfigureAwait(false))
                    throw new BrowserInputDeniedException("SEARCH_SCOPE_CHANGED");
                cancellationToken.ThrowIfCancellationRequested();
                attempted = true;
                await _page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync().ConfigureAwait(false);
                completed = true;
                return true;
            }, input: true).ConfigureAwait(false);
            after = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BrowserInputDeniedException exception) { error = exception.Code; }
        catch (OperationCanceledException) { error = "SYNTHETIC_SEARCH_POINTER_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch (PlaywrightException) { error = "SYNTHETIC_SEARCH_POINTER_UNCONFIRMED"; }
        return new(Guid.NewGuid().ToString("N"), started, before, after, attempted, completed,
            error is null && completed && after is not null && after.DocumentId == before.DocumentId
                && after.CaptureEpoch == before.CaptureEpoch && goal.IsSatisfiedBy(after), error);
    }
}
