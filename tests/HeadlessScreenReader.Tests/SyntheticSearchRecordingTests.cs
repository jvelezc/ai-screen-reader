using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchRecordingTests
{
    [Theory]
    [InlineData(BundledFixture.Foundation)]
    [InlineData(BundledFixture.SyntheticHandoff)]
    [InlineData(BundledFixture.MockCheckoutWorkingV1)]
    [InlineData((BundledFixture)999)]
    public void SearchRecorderRejectsOtherSourcesBeforeCreatingOutput(BundledFixture fixture)
    {
        var folder = NewFolder();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = ChromiumFixtureBrowser.LaunchSyntheticSearchRecordingAsync(folder, fixture); });
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Tab")]
    [InlineData("Alt+F4")]
    public void SearchRecorderRejectsOtherKeysBeforeCreatingOutput(string key)
    {
        var folder = NewFolder();
        Assert.Throws<ArgumentException>(() => { _ = TraversalRecordingDemo.RunSyntheticSearchRouteAsync(folder, BundledFixture.MockSearchWorkingV1, key); });
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1)]
    public void OriginalAdmittedMockCatalogueDoesNotAcquireSearchAuthority(BundledFixture fixture)
    {
        Assert.DoesNotContain(fixture, MockSiteTasks.Fixtures);
        Assert.Throws<ArgumentOutOfRangeException>(() => MockSiteTasks.Describe(fixture));
        Assert.Equal("MOCK_TASK_SOURCE_INVALID", Assert.Throws<BrowserOperationException>(() =>
            MockTaskSource.FromId(SyntheticSearchSites.Describe(fixture).Id)).Code);
        Assert.Equal(13, MockTaskControl.Boundaries.Length);
    }

    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1, "Enter", true)]
    [InlineData(BundledFixture.MockSearchWorkingV1, "Space", true)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, "Enter", false)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, "Space", false)]
    [Trait("Category", "Browser")]
    public async Task ActualRecordedSearchRetainsNativeCharactersGraphAndOriginalVideo(BundledFixture fixture, string key, bool reached)
    {
        var root = NewFolder();
        var mediaFolder = Path.Combine(root, "route-1-media");
        var result = await TraversalRecordingDemo.RunSyntheticSearchRouteAsync(mediaFolder, fixture, key);
        Assert.True(result.Recording.Stage == "Complete", System.Text.Json.JsonSerializer.Serialize(result, TraversalDemo.JsonOptions));
        Assert.Null(result.Recording.ErrorCode);
        Assert.NotNull(result.Receipt);
        var receipt = result.Receipt;
        Assert.Equal(reached ? TraversalReachability.Reached : TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(16, receipt.AttemptedKeystrokes);
        Assert.Equal(16, receipt.CompletedKeystrokes);
        Assert.True(receipt.HasRecording);
        Assert.Equal(SyntheticSearchSites.Describe(fixture).SourceSha256, result.Environment!.Content!.ContentSha256);
        var entered = Assert.Single(receipt.Attempts, a => a.Instruction.Command == ReaderCommand.EnterSearchText);
        Assert.True(entered.Step!.SearchInput!.ValueVerified);
        Assert.Equal(SyntheticSearchPolicy.Profile, entered.Step.SearchInput.Profile);
        Assert.Equal(13, entered.Step.BrowserKeystrokes);
        var graph = TraversalProjection.Build(receipt);
        var typedEdge = Assert.Single(graph.Edges, e => e.Command == ReaderCommand.EnterSearchText);
        Assert.True(typedEdge.InputEvidenceComplete);
        Assert.Equal(SyntheticSearchPolicy.Query, string.Concat(typedEdge.KeyboardInputs.Select(k => k.Key)));
        Assert.Equal(receipt.Plan.Instructions.Length, graph.Edges.Length);
        Assert.Equal(graph.Edges.Length + 1, graph.Nodes.Length);
        Assert.All(graph.Edges, e => Assert.True(e.InputEvidenceComplete));
        Assert.False(result.Recording.Alignment!.Calibrated);
        Assert.Empty(result.Recording.Alignment.Cues);
        Assert.Equal(CapturePhase.Closed, result.Recording.CaptureLifecycle!.Phase);
        Assert.Equal(BrowserShutdownPhase.Closed, result.Recording.Shutdown!.Phase);
        Assert.True(result.Recording.Shutdown.ProcessExitConfirmed);
        var media = result.Recording.Media!;
        Assert.NotEmpty(media.Source);
        Assert.NotEmpty(media.Video);
        Assert.All(media.Source, s => Assert.Null(s.Marker));
        var video = Assert.Single(media.Artifacts, a => a.File == "original.webm");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(mediaFolder, video.File));
        Assert.Equal(video.Bytes, bytes.LongLength);
        Assert.Equal(video.Sha256, TraversalArchive.Hash(bytes));
        Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }));
        await TraversalArtifactWriter.WriteAsync(root, 1, receipt, result.Environment, result.Recording);
        var html = await File.ReadAllTextAsync(Path.Combine(root, "route-1.html"));
        Assert.Contains("EnterSearchText", html);
        Assert.Contains("route-1-media/original.webm", html);
    }

    private static string NewFolder() => Path.GetFullPath(Path.Combine("artifacts", "synthetic-search-recording-tests", Guid.NewGuid().ToString("N")));
}
