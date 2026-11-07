using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    internal async Task<PointerTaskReceipt> ObserveMockPointerAsync(CancellationToken token)
    {
        if (_fixture is not { } fixture || _publicPolicy is not null)
            throw new BrowserOperationException("MOCK_TASK_SOURCE_REQUIRED");
        var task = MockSiteTasks.Describe(fixture);
        var plan = task.KeyboardPlan("Enter");
        var before = await CaptureAsync(token).ConfigureAwait(false);
        if (!plan.Precondition.IsSatisfiedBy(before) || plan.Destination.IsSatisfiedBy(before))
            throw new BrowserOperationException("MOCK_TASK_FRESH_STATE_REQUIRED");
        var id = Guid.NewGuid().ToString("N"); var started = DateTimeOffset.UtcNow;
        var attempted = false; var completed = false; AccessibilitySnapshot? after = null; string? error = null;
        try
        {
            token.ThrowIfCancellationRequested();
            await _captureAdmission.RunAsync(async () =>
            {
                attempted = true;
                // This fixed selector belongs only to the verified embedded mock sources.
                // Do not expose a general click or URL channel through this comparison.
                await _page.Locator("#action").ClickAsync().ConfigureAwait(false);
                completed = true; return true;
            }, input: true).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            after = await CaptureAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { error = "MOCK_POINTER_CANCELLED"; }
        catch (BrowserOperationException) { error = "MOCK_POINTER_OBSERVATION_UNCONFIRMED"; }
        catch (PlaywrightException) { error = "MOCK_POINTER_INPUT_UNCONFIRMED"; }
        return new(id, started, before, after, attempted, completed,
            after is not null && plan.Destination.IsSatisfiedBy(after), error);
    }
}
