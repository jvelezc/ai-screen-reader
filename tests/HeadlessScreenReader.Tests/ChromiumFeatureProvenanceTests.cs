using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumFeatureProvenanceTests
{
    [Theory]
    [InlineData(0, TraversalReachability.Reached, 2)]
    [InlineData(1, TraversalReachability.NotReached, 0)]
    [InlineData(2, TraversalReachability.Unknown, 3)]
    [InlineData(3, TraversalReachability.Unknown, 4)]
    public async Task FourRealRoutes_KeepDiagnosticHintsSeparateFromNamesAndActualPaths(int route, TraversalReachability reachability, int keys)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        await browser.DiagnosticPage.EvaluateAsync("() => { window.diagnosticKeys=0; addEventListener('keydown',()=>diagnosticKeys++); }");
        var before = await browser.CaptureAsync();
        var discovery = await browser.CaptureFeatureDiagnosticsAsync();
        var after = await browser.CaptureAsync();
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        Assert.Equal(0, await browser.DiagnosticPage.EvaluateAsync<int>("() => window.diagnosticKeys"));
        Assert.Equal(5, discovery.Candidates.Length);
        Assert.All(discovery.Candidates, c => Assert.True(c.StableDuringDiscovery));
        var unnamed = Assert.Single(discovery.Candidates, c => c.ObservedNode.Name.Length == 0);
        Assert.Equal("account-settings", unnamed.Hint!.Value);
        Assert.Equal(DiagnosticHintSource.DeclaredFeatureAttribute, unnamed.Hint.Source);
        using (var reader = new ScreenReaderSession(browser))
        {
            await reader.ExecuteAsync(ReaderCommand.DocumentEnd);
            await reader.ExecuteAsync(ReaderCommand.PreviousButton);
            var observed = await reader.ExecuteAsync(ReaderCommand.PreviousButton);
            Assert.Equal(unnamed.ObservedNode.Id, observed.CursorNodeId);
            Assert.DoesNotContain("account-settings", observed.Announcement);
            Assert.Contains("button", observed.Announcement);
        }
        var receipt = await new TraversalRunner().RunAsync(browser, TraversalDemo.Plans[route],
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 });
        var serialized = JsonSerializer.Serialize(receipt);
        var inventory = FeatureProvenance.Build(discovery, receipt);
        Assert.All(inventory.Features, f => Assert.Equal(FeatureSourceBinding.Matched, f.Binding));
        Assert.Equal(reachability, receipt.Reachability);
        Assert.Equal(keys, receipt.CompletedKeystrokes);
        Assert.Equal(serialized, JsonSerializer.Serialize(receipt));
        Assert.Empty(Assert.Single(inventory.Features, f => !f.HasExposedName).Relations);
        if (route == 0)
            Assert.Equal(new[] { "a1", "a2" }, inventory.Features[0].Relations.Select(r => r.AttemptId));
        if (route == 1)
        {
            Assert.Equal(TraversalBarrier.Observed, receipt.Barrier);
            Assert.Equal(new[] { "a2", "a3" }, Assert.Single(inventory.Features, f => f.Candidate.ObservedNode.IsDisabled).Relations.Select(r => r.AttemptId));
        }
    }

    [Fact]
    public async Task BoundsHiddenEditableAndOversizedHints_RemainExplicitWithoutInventedLabels()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        await browser.DiagnosticPage.EvaluateAsync("""
            () => {
              document.querySelector('#unlabelled').setAttribute('data-hsr-feature','x'.repeat(129));
              const hidden=document.createElement('button');hidden.hidden=true;hidden.dataset.hsrFeature='hidden-private-hint';document.body.append(hidden);
              const editable=document.createElement('button');editable.contentEditable='true';editable.dataset.hsrFeature='editable-private-hint';document.body.append(editable);
              for(let i=0;i<70;i++){const b=document.createElement('button');b.textContent='Extra '+i;document.body.append(b);}
            }
            """);
        var discovery = await browser.CaptureFeatureDiagnosticsAsync();
        Assert.Equal(64, discovery.Candidates.Length);
        Assert.True(discovery.Truncated);
        var unnamed = Assert.Single(discovery.Candidates, c => c.HintState == DiagnosticHintState.RejectedOversize);
        Assert.Null(unnamed.Hint);
        Assert.Equal("", unnamed.ObservedNode.Name);
        var serialized = JsonSerializer.Serialize(discovery);
        Assert.DoesNotContain("hidden-private-hint", serialized);
        Assert.DoesNotContain("editable-private-hint", serialized);
        Assert.DoesNotContain(new string('x', 129), serialized);
        Assert.Contains("child-frame", string.Join(" ", discovery.Limitations));
    }

    [Fact]
    public async Task ResumedEpochForeignDocumentAndExcludedDiagnostics_CannotAcquireAReaderPath()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var discovery = await browser.CaptureFeatureDiagnosticsAsync();
        using var reader = new ScreenReaderSession(browser);
        var lease = await reader.ExcludeCaptureAsync();
        Assert.Equal("CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => browser.CaptureFeatureDiagnosticsAsync())).Code);
        await browser.DiagnosticPage.SetContentAsync(BundledFixtureCatalog.ReadHtml(BundledFixture.Traversal));
        await reader.ResumeCaptureAsync(lease);
        var receipt = await new TraversalRunner().RunAsync(browser, TraversalDemo.Plans[0],
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 });
        Assert.True(receipt.Observations[0].Snapshot.CaptureEpoch > discovery.CaptureEpoch);
        var inventory = FeatureProvenance.Build(discovery, receipt);
        Assert.All(inventory.Features, f =>
        {
            Assert.Equal(FeatureSourceBinding.EpochMismatch, f.Binding);
            Assert.Empty(f.Relations);
        });
        await using var other = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var foreignReceipt = await new TraversalRunner().RunAsync(other, TraversalDemo.Plans[0],
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 });
        Assert.All(FeatureProvenance.Build(discovery, foreignReceipt).Features,
            f => Assert.Equal(FeatureSourceBinding.DocumentMismatch, f.Binding));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => browser.CaptureFeatureDiagnosticsAsync(cancelled.Token));
    }

    [Fact]
    public async Task NodeRemovedDuringDiagnosticRead_RemainsStaleAndCannotAcquirePathLinks()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        await browser.DiagnosticPage.EvaluateAsync("""
            () => {
              const original=Element.prototype.getAttribute;
              Element.prototype.getAttribute=function(name){
                const value=original.call(this,name);
                if(name==='data-hsr-feature' && this.id==='preview')this.remove();
                return value;
              };
            }
            """);
        var discovery = await browser.CaptureFeatureDiagnosticsAsync();
        var removed = Assert.Single(discovery.Candidates, c => c.Hint?.Value == "checkout-preview");
        Assert.False(removed.StableDuringDiscovery);
        var receipt = await new TraversalRunner().RunAsync(browser, TraversalDemo.Plans[0],
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 });
        var feature = Assert.Single(FeatureProvenance.Build(discovery, receipt).Features, f => f.Candidate.Id == removed.Id);
        Assert.Equal(FeatureSourceBinding.DiscoveryUnconfirmed, feature.Binding);
        Assert.Empty(feature.Relations);
        Assert.Null(feature.SourceObservationId);
    }
}
