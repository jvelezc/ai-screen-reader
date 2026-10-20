using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumCoordinatedHandoffTests
{
    [Fact]
    public async Task CoordinatedProtectedInput_StaysExcludedAndPersistsOnlyExplicitSimulationMetadata()
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "coordinated-handoff-tests"));
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.SyntheticHandoff);
        var control = new ControlledScanSession(browser, store);
        try
        {
            var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var before = await control.ExecuteAsync(ai, 1, ReaderCommand.Tab);
            Assert.Equal("Synthetic canary field", before.Evidence!.Snapshot!.FocusedNode!.Name);
            await control.ReleaseAsync(ai);
            var simulation = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
            await Assert.ThrowsAsync<BrowserOperationException>(() => control.ExecuteAsync(simulation, 1, ReaderCommand.ReadCurrent));
            var typed = await control.ExecuteSimulatedAssistanceAsync(simulation, SyntheticInputAction.EnterCanary);
            Assert.Equal(1, typed.Completion.Input.CompletedInputCalls); Assert.False(typed.Completion.HumanAcceptancePerformed);
            Assert.Equal(browser.SyntheticCanaryForAudit, await browser.DiagnosticPage.Locator("input").InputValueAsync());
            Assert.Equal(browser.SyntheticCanaryForAudit, await browser.DiagnosticPage.Locator("h1").InnerTextAsync());
            await control.ReleaseAsync(simulation);
            Assert.Equal("CONTROL_SYNTHETIC_CLEAR_REQUIRED", (await Assert.ThrowsAsync<BrowserOperationException>(
                () => control.AcquireAsync(TimeSpan.FromMinutes(1)))).Code);
            Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
            var clearer = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
            var cleared = await control.ExecuteSimulatedAssistanceAsync(clearer, SyntheticInputAction.ClearCanary);
            Assert.True(cleared.Completion.Input.CanaryCleared); Assert.Equal(2, cleared.Completion.Input.CompletedInputCalls);
            await control.ReleaseAsync(clearer);
            var resumed = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var after = await control.ExecuteAsync(resumed, 2, ReaderCommand.ReadCurrent);
            Assert.Equal(before.Evidence.Snapshot.DocumentId, after.Evidence!.Snapshot!.DocumentId);
            Assert.Equal(4, resumed.Generation); Assert.Equal(2, after.Completion.Intent.Epoch);
            Assert.True(after.Completion.Intent.PriorSimulatedAssistance);
            Assert.Contains(after.Evidence.Events, e => e.Kind == ReaderEventKind.CaptureBaselineReset);
            var files = Directory.EnumerateFiles(store.Folder).ToArray();
            Assert.Equal(2, files.Count(file => file.EndsWith(".result.json", StringComparison.Ordinal)));
            Assert.Equal(2, files.Count(file => Path.GetFileName(file).StartsWith("assistance-", StringComparison.Ordinal)
                && file.EndsWith(".completion.json", StringComparison.Ordinal)));
            foreach (var file in files)
            {
                var json = await File.ReadAllTextAsync(file);
                Assert.DoesNotContain(browser.SyntheticCanaryForAudit, json);
                foreach (var token in new[] { ai.Token, simulation.Token, clearer.Token, resumed.Token }) Assert.DoesNotContain(token.ToString(), json);
            }
            Assert.DoesNotContain(browser.SyntheticCanaryForAudit, JsonSerializer.Serialize(control.Status));
        }
        finally { await control.DisposeAsync(); }
        Assert.Equal(CapturePhase.Closed, browser.CaptureStatus.Phase);
        Assert.Equal(ControllerPhase.Closed, control.Status.Phase);
        Assert.Empty(Directory.EnumerateFiles(store.Folder, "*.pending"));
    }
}
