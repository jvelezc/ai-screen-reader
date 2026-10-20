using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumControlledReplayTests
{
    [Fact]
    public async Task EquivalentFreshChromiumInstance_CreatesNewVerifiedTraceWithNativeKeysAndImmutableHistory()
    {
        var (historical, environment) = await Historical();
        var before = JsonSerializer.Serialize(historical);
        await using var browser = await Fresh();
        await CountKeys(browser);
        var result = await new ControlledReplay().RunAsync(browser, historical, environment, ReplayDemo.Environment(browser.Environment));
        Assert.Equal(ReplayDisposition.AttemptRecorded, result.Disposition);
        Assert.Equal(TraversalReachability.Reached, result.Result!.Reachability);
        Assert.Equal(2, result.Result.CompletedKeystrokes);
        Assert.Equal(2, await KeyCount(browser));
        Assert.Equal(result.HistoricalSource, result.FreshSource);
        Assert.NotEqual(historical.RunId, result.Result.RunId);
        Assert.NotEqual(historical.Observations[0].Snapshot.DocumentId, result.Result.Observations[0].Snapshot.DocumentId);
        Assert.Equal(historical.RunId, result.HistoricalRunId);
        Assert.Equal(before, JsonSerializer.Serialize(historical));
        Assert.Equal(VerificationStatus.Satisfied, result.Result.Attempts[^1].Step!.Verification);
    }

    [Fact]
    public async Task SameUrlChangedDisabledState_RejectsBeforeAnyNativeInput()
    {
        var (historical, environment) = await Historical();
        await using var browser = await Fresh();
        await CountKeys(browser);
        Assert.Equal("about:blank", browser.DiagnosticPage.Url);
        await browser.DiagnosticPage.EvaluateAsync("() => document.querySelector('#preview').disabled = true");
        var result = await new ControlledReplay().RunAsync(browser, historical, environment, ReplayDemo.Environment(browser.Environment));
        Assert.Equal(ReplayDisposition.Rejected, result.Disposition);
        Assert.Equal("REPLAY_SOURCE_MISMATCH", result.Code);
        Assert.NotEqual(result.HistoricalSource, result.FreshSource);
        Assert.Contains(result.PreflightObservation!.Nodes, n => n.Name == "Open checkout preview" && n.IsDisabled);
        Assert.Null(result.Result);
        Assert.Equal(0, await KeyCount(browser));
    }

    [Fact]
    public async Task EquivalentObservedStartWithChangedBehavior_PreservesFreshUnknownInsteadOfHistoricalSuccess()
    {
        var (historical, environment) = await Historical();
        await using var browser = await Fresh();
        // Independent fault injection: equivalent exposed control, with its fixture click handler removed.
        await browser.DiagnosticPage.EvaluateAsync("() => { const button=document.querySelector('#preview');button.replaceWith(button.cloneNode(true)); }");
        var result = await new ControlledReplay().RunAsync(browser, historical, environment, ReplayDemo.Environment(browser.Environment));
        Assert.Equal(ReplayDisposition.AttemptRecorded, result.Disposition);
        Assert.Equal(result.HistoricalSource, result.FreshSource);
        Assert.Equal(TraversalReachability.Reached, historical.Reachability);
        Assert.Equal(TraversalReachability.Unknown, result.Result!.Reachability);
        Assert.Equal(VerificationStatus.TimedOut, result.Result.Attempts[^1].Step!.Verification);
        Assert.Equal(TraversalBarrier.NoneObserved, result.Result.Barrier);
        Assert.Equal(2, result.Result.CompletedKeystrokes);
    }

    [Fact]
    public async Task SourceChangesImmediatelyBeforeNativeDispatch_DeniesWithoutKeysOrInputUncertainty()
    {
        var (historical, environment) = await Historical();
        await using var browser = await Fresh();
        await CountKeys(browser);
        var guarded = new ChangeBeforeDispatch(browser);
        var result = await new ControlledReplay().RunAsync(guarded, historical, environment, ReplayDemo.Environment(browser.Environment));
        Assert.Equal(ReplayDisposition.Rejected, result.Disposition);
        Assert.Equal("REPLAY_SOURCE_CHANGED_BEFORE_INPUT", result.Code);
        Assert.Equal(0, await KeyCount(browser));
        Assert.Equal(0, result.Result!.CompletedKeystrokes);
        Assert.Equal(TraversalPolicy.Denied, result.Result.Policy);
        Assert.NotEqual(TraversalBarrier.Observed, result.Result.Barrier);
        Assert.False(result.Result.Attempts[0].Step!.InputMayHaveOccurred);
        Assert.Contains(result.RejectedObservation!.Nodes, n => n.Name == "Changed before dispatch");
    }

    [Fact]
    public async Task AdditionalFrameCoverage_RejectsUnsupportedSourceWithoutInput()
    {
        var (historical, environment) = await Historical();
        await using var browser = await Fresh();
        await CountKeys(browser);
        await browser.DiagnosticPage.EvaluateAsync("""
            () => new Promise(resolve => {
              const frame=document.createElement('iframe');frame.srcdoc='<h1>Child document</h1>';
              frame.onload=resolve;document.body.append(frame);
            })
            """);
        var result = await new ControlledReplay().RunAsync(browser, historical, environment, ReplayDemo.Environment(browser.Environment));
        Assert.Equal(ReplayDisposition.Rejected, result.Disposition);
        Assert.Equal("REPLAY_FRESH_SOURCE_UNSUPPORTED", result.Code);
        Assert.Null(result.Result);
        Assert.Equal(0, await KeyCount(browser));
    }

    private static async Task<(TraversalReceipt, ReplayEnvironment)> Historical()
    {
        await using var browser = await Fresh();
        var result = await new TraversalRunner().RunAsync(browser, ControlledReplay.Plan,
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 });
        Assert.Equal(TraversalReachability.Reached, result.Reachability);
        return (result, ReplayDemo.Environment(browser.Environment));
    }
    private static Task<ChromiumFixtureBrowser> Fresh() => ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
    private static Task CountKeys(ChromiumFixtureBrowser browser) =>
        browser.DiagnosticPage.EvaluateAsync("() => { window.replayKeys=0;addEventListener('keydown',()=>replayKeys++); }");
    private static Task<int> KeyCount(ChromiumFixtureBrowser browser) => browser.DiagnosticPage.EvaluateAsync<int>("() => window.replayKeys");

    private sealed class ChangeBeforeDispatch(ChromiumFixtureBrowser browser) : IScreenReaderBrowser
    {
        private int _captures;
        public async Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            if (++_captures == 4)
                await browser.DiagnosticPage.EvaluateAsync("() => document.querySelector('#preview').setAttribute('aria-label','Changed before dispatch')");
            return await browser.CaptureAsync(cancellationToken);
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => browser.PressKeyAsync(key, cancellationToken);
    }
}
