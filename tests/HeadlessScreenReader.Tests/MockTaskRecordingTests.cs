using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed partial class MockTaskRecordingTests
{
    [Theory]
    [InlineData(BundledFixture.Foundation)]
    [InlineData(BundledFixture.SyntheticHandoff)]
    [InlineData(BundledFixture.TraversalRecording)]
    [InlineData((BundledFixture)999)]
    public void MockRecorderRejectsOtherSourcesBeforeCreatingFiles(BundledFixture fixture)
    {
        var folder = NewFolder();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = ChromiumFixtureBrowser.LaunchMockRecordingAsync(folder, fixture); });
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData("Alt+F4")]
    [InlineData("a")]
    [InlineData("Tab")]
    public void MockTaskPlansCannotAcceptCallerSuppliedInput(string key)
    {
        var task = MockSiteTasks.Describe(BundledFixture.MockCheckoutWorkingV1);
        Assert.Throws<ArgumentException>(() => task.KeyboardPlan(key));
    }

    [Fact]
    public void MockTaskSourcesMatchTheIndependentAuthoredCatalogue()
    {
        using var stream = typeof(MockTaskRecordingTests).Assembly.GetManifestResourceStream("MockSitesV1.Manifest.json")!;
        using var json = JsonDocument.Parse(stream);
        var scenarios = json.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.Equal(4, MockSiteTasks.Fixtures.Length);
        foreach (var fixture in MockSiteTasks.Fixtures)
        {
            var task = MockSiteTasks.Describe(fixture);
            var expected = scenarios.Single(s => s.GetProperty("id").GetString() == task.Id);
            Assert.Equal(expected.GetProperty("contentSha256").GetString(), task.SourceSha256);
            Assert.Equal(expected.GetProperty("expected").GetProperty("goalName").GetString(), task.GoalName);
            Assert.Equal(new[] { ReaderCommand.Tab, ReaderCommand.Activate }, task.KeyboardPlan("Enter").Instructions.Select(i => i.Command));
            Assert.Equal("Space", task.KeyboardPlan("Space").Instructions[^1].Key);
        }
    }

    [Theory]
    [InlineData(BundledFixture.MockCheckoutWorkingV1, "Enter", true)]
    [InlineData(BundledFixture.MockCheckoutWorkingV1, "Space", true)]
    [InlineData(BundledFixture.MockCheckoutKeyboardBlockedV1, "Enter", false)]
    [InlineData(BundledFixture.MockCheckoutKeyboardBlockedV1, "Space", false)]
    [InlineData(BundledFixture.MockNamedControlV1, "Enter", true)]
    [InlineData(BundledFixture.MockUnnamedControlV1, "Enter", true)]
    [Trait("Category", "Browser")]
    public async Task ActualMockRecordingsRetainSourceInputsGraphOriginalVideoAndUnknownAlignment(BundledFixture fixture, string key, bool reached)
    {
        var folder = NewFolder();
        var result = await TraversalRecordingDemo.RunMockRouteAsync(folder, fixture, key);
        Assert.True(result.Recording.Stage == "Complete", JsonSerializer.Serialize(result, TraversalDemo.JsonOptions)); Assert.Null(result.Recording.ErrorCode);
        Assert.NotNull(result.Receipt); var receipt = result.Receipt;
        Assert.Equal(reached ? TraversalReachability.Reached : TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(2, receipt.AttemptedKeystrokes); Assert.Equal(2, receipt.CompletedKeystrokes); Assert.True(receipt.HasRecording);
        Assert.Equal(new[] { "Tab", key }, receipt.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail));
        Assert.Equal(reached, receipt.Timeline.Any(e => e.Kind == ReaderEventKind.GoalVerified));
        Assert.Equal(BundledFixtureCatalog.Describe(fixture), result.Environment!.Content);
        var graph = TraversalProjection.Build(receipt);
        Assert.Equal(receipt.Plan.Instructions.Length, graph.Edges.Length); Assert.Equal(graph.Edges.Length + 1, graph.Nodes.Length);
        Assert.All(graph.Edges, e => Assert.True(e.InputEvidenceComplete));
        Assert.False(result.Recording.Alignment!.Calibrated); Assert.Empty(result.Recording.Alignment.Cues);
        Assert.Equal("TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID", result.Recording.Alignment.ReasonCode);
        Assert.Equal(CapturePhase.Closed, result.Recording.CaptureLifecycle!.Phase);
        Assert.Equal(BrowserShutdownPhase.Closed, result.Recording.Shutdown!.Phase);
        var media = result.Recording.Media!; Assert.NotEmpty(media.Source); Assert.NotEmpty(media.Video);
        var original = Assert.Single(media.Artifacts.Where(a => a.File == "original.webm"));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(folder, original.File));
        Assert.Equal(original.Sha256, TraversalArchive.Hash(bytes)); Assert.Equal(original.Bytes, bytes.Length);
        Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }));
        Assert.All(media.Source, source => Assert.Null(source.Marker));
        if (fixture == BundledFixture.MockUnnamedControlV1)
            Assert.Contains(receipt.Observations[0].Snapshot.Nodes, n => n.Role == "button" && n.Name == "");
    }

    [Theory]
    [InlineData(BundledFixture.MockCheckoutWorkingV1, "AllTestedKeyboardGoalsObserved")]
    [InlineData(BundledFixture.MockCheckoutKeyboardBlockedV1, "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved")]
    [Trait("Category", "Browser")]
    public async Task ActualMockSuiteRetainsFreshRepeatSpaceAndPointerExperimentsAndDetectsChangedOriginals(BundledFixture fixture, string observation)
    {
        var folder = NewFolder(); var export = await MockTaskRecordings.RecordAsync(folder, fixture);
        Assert.Equal(observation, export.Observation); Assert.Equal(3, export.KeyboardRuns); Assert.Equal("Observed", export.PointerGoalObservation);
        var verified = await MockTaskRecordings.VerifyAsync(folder, export.ManifestSha256); Assert.True(verified.IntegrityMatches);
        using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(folder, MockTaskRecordings.ManifestFile)));
        var root = json.RootElement;
        var runs = root.GetProperty("KeyboardRuns").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Enter", "Enter", "Space" }, runs.Select(r => r.GetProperty("Key").GetString()));
        Assert.Equal(4, runs.Select(r => r.GetProperty("RunId").GetString()).Append(root.GetProperty("PointerRunId").GetString()).Distinct().Count());
        Assert.False(root.GetProperty("LegalCaseDetermined").GetBoolean()); Assert.Equal("NotAssessed", root.GetProperty("WebsiteEvidenceAssessment").GetString());
        Assert.Matches("^[a-f0-9]{64}$", root.GetProperty("ProducerSha256").GetString()!);
        Assert.Equal(TraversalArchive.Hash(await File.ReadAllBytesAsync(typeof(MockTaskRecordings).Assembly.Location)),
            root.GetProperty("ProducerSha256").GetString());
        Assert.Equal("320x240", root.GetProperty("Viewport").GetString());
        using var pointer = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(folder, "pointer.json")));
        var receipt = pointer.RootElement.GetProperty("Receipt");
        Assert.Equal("Pointer", receipt.GetProperty("Mechanism").GetString()); Assert.Equal(0, receipt.GetProperty("KeyboardInputs").GetInt32());
        Assert.True(receipt.GetProperty("Attempted").GetBoolean()); Assert.True(receipt.GetProperty("Completed").GetBoolean());
        Assert.True(receipt.GetProperty("GoalObserved").GetBoolean());
        Assert.Equal(MockSiteTasks.Describe(fixture).SourceSha256, TraversalArchive.Hash(await File.ReadAllBytesAsync(Path.Combine(folder, "fixture-source.txt"))));
        using (var zip = ZipFile.OpenRead(export.Archive))
        {
            Assert.Equal(4, zip.Entries.Count(e => e.FullName.EndsWith("/original.webm", StringComparison.Ordinal)));
            Assert.Equal(verified.Files + 1, zip.Entries.Count); Assert.Contains(zip.Entries, e => e.FullName == "pointer.json");
        }
        var index = await File.ReadAllTextAsync(Path.Combine(folder, "index.html"));
        Assert.Contains("pointer-media/original.webm", index); Assert.Contains("cannot identify a verified video interval", index);
        await Assert.ThrowsAsync<IOException>(() => MockTaskRecordings.RecordAsync(folder, fixture));
        var video = Path.Combine(folder, "route-1-media", "original.webm");
        await File.AppendAllTextAsync(video, "altered", Encoding.UTF8);
        await Assert.ThrowsAsync<InvalidDataException>(() => MockTaskRecordings.VerifyAsync(folder, export.ManifestSha256));
    }

    private static string NewFolder() => Path.GetFullPath(Path.Combine("artifacts", "mock-task-recording-tests", Guid.NewGuid().ToString("N")));
}
