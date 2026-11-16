using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchPackageTests
{
    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1, "AllTestedKeyboardGoalsObserved")]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved")]
    [Trait("Category", "Browser")]
    public async Task ActualSearchPackageRetainsFreshComparisonsAndRecomputesClaims(BundledFixture fixture, string observation)
    {
        var folder = NewFolder();
        var export = await SyntheticSearchRecordings.RecordAsync(folder, fixture);
        Assert.Equal(observation, export.Observation);
        Assert.Equal(3, export.KeyboardRuns);
        Assert.Equal("Observed", export.PointerGoalObservation);
        var verified = await SyntheticSearchVerification.VerifyAsync(folder, export.ManifestSha256);
        Assert.True(verified.IntegrityMatches);
        var manifestPath = Path.Combine(folder, SyntheticSearchRecordings.ManifestFile);
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize<SyntheticSearchManifest>(manifestBytes, SyntheticSearchRecordings.Json)!;
        Assert.Equal(SyntheticSearchSites.Describe(fixture), manifest.Source);
        Assert.Equal("LocalEmbeddedSyntheticSearchExperiments", manifest.Scope);
        Assert.Equal("UnverifiedWithoutCalibrationMarkers", manifest.VideoAlignment);
        Assert.Equal("NotAssessed", manifest.WebsiteEvidenceAssessment);
        Assert.Equal("NotPerformed", manifest.ExternalAssistiveTechnologyComparison);
        Assert.False(manifest.LegalCaseDetermined);
        Assert.Null(manifest.CollectionStoppedReason);
        Assert.Equal(new[] { "Enter", "Enter", "Space" }, manifest.KeyboardRuns.Select(r => r.Key));
        Assert.All(manifest.KeyboardRuns, run => { Assert.Equal(16, run.AttemptedKeys); Assert.Equal(16, run.CompletedKeys); });
        Assert.Equal(4, manifest.KeyboardRuns.Select(r => r.RunId).Append(manifest.PointerRunId).Distinct().Count());
        Assert.Equal(TraversalArchive.Hash(await File.ReadAllBytesAsync(typeof(SyntheticSearchRecordings).Assembly.Location)), manifest.ProducerSha256);
        Assert.Equal(manifest.Source.SourceSha256, TraversalArchive.Hash(await File.ReadAllBytesAsync(Path.Combine(folder, "fixture-source.txt"))));
        var results = new List<RecordedTraversalResult>();
        for (var i = 1; i <= 3; i++) results.Add(JsonSerializer.Deserialize<RecordedTraversalResult>(
            await File.ReadAllBytesAsync(Path.Combine(folder, $"route-{i}-recording.json")), SyntheticSearchRecordings.Json)!);
        var pointer = JsonSerializer.Deserialize<SearchPointerRecording>(await File.ReadAllBytesAsync(Path.Combine(folder, "pointer.json")), SyntheticSearchRecordings.Json)!;
        Assert.True(pointer.Preparation!.Focus!.FocusObserved);
        Assert.True(pointer.Preparation.Entry!.SearchInput!.ValueVerified);
        Assert.False(pointer.Preparation.TimelineTruncated);
        Assert.Equal(13, pointer.Preparation.Timeline.Count(e => e.Kind == ReaderEventKind.InputCompleted));
        Assert.DoesNotContain(pointer.Preparation.Timeline, e => e.Kind == ReaderEventKind.InputCompleted && e.Detail is "Tab" or "Enter" or "Space");
        Assert.True(pointer.Activation!.Attempted);
        Assert.True(pointer.Activation.Completed);
        Assert.True(pointer.Activation.GoalObserved);
        Assert.Equal(CapturePhase.Closed, pointer.Capture!.Phase);
        Assert.True(pointer.Shutdown!.ProcessExitConfirmed);
        Assert.Equal(observation, SyntheticSearchAssessment.Describe(results, pointer, manifest.Source));
        Assert.Equal(SyntheticSearchAssessment.Incomplete, SyntheticSearchAssessment.Describe(results, pointer with { ErrorCode = "INJECTED_FAILURE" }, manifest.Source));
        Assert.Equal(SyntheticSearchAssessment.Incomplete, SyntheticSearchAssessment.Describe(results, pointer with
        { Preparation = pointer.Preparation with { TimelineTruncated = true } }, manifest.Source));
        Assert.Equal(SyntheticSearchAssessment.Incomplete, SyntheticSearchAssessment.Describe(results, pointer with
        { Activation = pointer.Activation with { GoalObserved = false } }, manifest.Source));
        Assert.Equal(SyntheticSearchAssessment.Incomplete, SyntheticSearchAssessment.Describe(results.Take(2).ToArray(), pointer, manifest.Source));
        Assert.Equal(SyntheticSearchAssessment.Incomplete, SyntheticSearchAssessment.Describe(new[] { results[0], results[0], results[2] }, pointer, manifest.Source));
        var incomplete = results[0] with { Recording = results[0].Recording with { Shutdown = null } };
        Assert.False(SyntheticSearchAssessment.CompleteKeyboard(incomplete, manifest.Source, "Enter"));
        var original = results[0];
        foreach (var invalid in new[]
        {
            original with { Environment = original.Environment! with { Content = BundledFixtureCatalog.Describe(BundledFixture.Foundation) } },
            original with { Recording = original.Recording with { Stage = "DecodeFailed" } },
            original with { Recording = original.Recording with { ErrorCode = "INJECTED_FAILURE" } },
            original with { Recording = original.Recording with { Media = null } },
            original with { Recording = original.Recording with { Media = original.Recording.Media! with { Artifacts = [] } } },
            original with { Recording = original.Recording with { CaptureLifecycle = null } },
            original with { Receipt = original.Receipt! with { Execution = TraversalExecution.SystemError } },
            original with { Receipt = original.Receipt! with { Policy = TraversalPolicy.Denied } },
            original with { Receipt = original.Receipt! with { TimelineTruncated = true } },
            original with { Receipt = original.Receipt! with { UnexecutedInstructions = 1 } },
            original with { Receipt = original.Receipt! with { Observations = original.Receipt.Observations.SetItem(1,
                original.Receipt.Observations[1] with { Snapshot = original.Receipt.Observations[1].Snapshot with { CaptureEpoch = 99 } }) } }
        }) Assert.False(SyntheticSearchAssessment.CompleteKeyboard(invalid, manifest.Source, "Enter"));
        using (var zip = ZipFile.OpenRead(export.Archive))
        {
            Assert.Equal(4, zip.Entries.Count(e => e.FullName.EndsWith("/original.webm", StringComparison.Ordinal)));
            Assert.Equal(verified.Files + 1, zip.Entries.Count);
            Assert.Contains(zip.Entries, e => e.FullName == "pointer-graph.json");
        }
        Assert.Equal(export.ArchiveSha256, TraversalArchive.Hash(await File.ReadAllBytesAsync(export.Archive)));
        await Assert.ThrowsAsync<IOException>(() => SyntheticSearchRecordings.RecordAsync(folder, fixture));
        await VerifyNativePlaybackAsync(folder, observation);
        await RejectRehashedCopyAsync(folder, manifestBytes, "pointer.json", node =>
            node!["Preparation"]!["ReaderClock"] = "VerifiedVideoClock", "SEARCH_DERIVED_FIELD_CHANGED");
        await RejectRehashedCopyAsync(folder, manifestBytes, "pointer-graph.json", node =>
            node!["Edges"]![1]!["CompletedNativeCalls"] = 12, "SEARCH_POINTER_GRAPH_CHANGED");
        await RejectRehashedCopyAsync(folder, manifestBytes, "route-1.json", node =>
            node!["Graph"]!["Edges"]![1]!["CompletedKeystrokes"] = 12, "SEARCH_ROUTE_GRAPH_CHANGED");

        // Rehashing a false conclusion cannot turn the saved observations into that conclusion.
        var changed = JsonNode.Parse(manifestBytes)!.AsObject();
        changed["Observation"] = observation == "AllTestedKeyboardGoalsObserved" ? "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved" : "AllTestedKeyboardGoalsObserved";
        var altered = JsonSerializer.SerializeToUtf8Bytes(changed, SyntheticSearchRecordings.Json);
        await File.WriteAllBytesAsync(manifestPath, altered);
        Assert.Equal("SEARCH_OBSERVATION_CHANGED", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            SyntheticSearchVerification.VerifyAsync(folder, TraversalArchive.Hash(altered)))).Message);
        changed["LegalCaseDetermined"] = true;
        altered = JsonSerializer.SerializeToUtf8Bytes(changed, SyntheticSearchRecordings.Json);
        await File.WriteAllBytesAsync(manifestPath, altered);
        Assert.Equal("SEARCH_SCOPE_INVALID", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            SyntheticSearchVerification.VerifyAsync(folder, TraversalArchive.Hash(altered)))).Message);
        changed = JsonNode.Parse(manifestBytes)!.AsObject();
        changed["Source"]!["Profile"] = "undeclared-profile";
        altered = JsonSerializer.SerializeToUtf8Bytes(changed, SyntheticSearchRecordings.Json);
        await File.WriteAllBytesAsync(manifestPath, altered);
        Assert.Equal("SEARCH_DERIVED_FIELD_CHANGED", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            SyntheticSearchVerification.VerifyAsync(folder, TraversalArchive.Hash(altered)))).Message);
        await File.WriteAllBytesAsync(manifestPath, manifestBytes);
        var video = Path.Combine(folder, "route-1-media", "original.webm");
        await File.AppendAllTextAsync(video, "altered");
        Assert.Equal("SEARCH_INVENTORY_CHANGED", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            SyntheticSearchVerification.VerifyAsync(folder, export.ManifestSha256))).Message);
    }

    [Fact]
    public async Task IncompleteSearchAttemptIsSavedAndStopsAllFurtherInput()
    {
        var folder = NewFolder(); var keyboardCalls = 0; var mouseCalls = 0;
        var failed = new RecordedTraversalResult(null, new("Traverse", "NATIVE_INPUT_UNCONFIRMED", null, [], null, null), null);
        var export = await SyntheticSearchRecordings.RecordCoreAsync(folder, BundledFixture.MockSearchWorkingV1,
            (_, _, _, _) => { keyboardCalls++; return Task.FromResult(failed); },
            (_, _, _) => { mouseCalls++; throw new InvalidOperationException("Mouse input must never run."); }, CancellationToken.None);
        Assert.Equal(1, keyboardCalls);
        Assert.Equal(0, mouseCalls);
        Assert.Equal(1, export.KeyboardRuns);
        Assert.Equal(SyntheticSearchAssessment.Incomplete, export.Observation);
        Assert.Equal("Unavailable", export.PointerGoalObservation);
        Assert.True(File.Exists(Path.Combine(folder, "route-1-recording.json")));
        Assert.False(File.Exists(Path.Combine(folder, "route-2-recording.json")));
        Assert.False(File.Exists(Path.Combine(folder, "pointer.json")));
        Assert.Contains("No further keyboard or mouse input was sent", await File.ReadAllTextAsync(Path.Combine(folder, "index.html")));
        Assert.True((await SyntheticSearchVerification.VerifyAsync(folder, export.ManifestSha256)).IntegrityMatches);
    }

    [Theory]
    [InlineData("--record-search-tasks", "checkout-working")]
    [InlineData("--record-search-tasks", "https://example.com")]
    [InlineData("--record-search-tasks", "search-working", false)]
    public async Task SearchCommandRejectsUnknownSourcesAndMalformedArguments(string command, string value, bool completeArguments = true)
    {
        var folder = NewFolder(); using var output = new StringWriter();
        var code = await SyntheticSearchCommands.RunAsync(completeArguments ? [command, value, folder] : [command, value], output);
        Assert.Equal(2, code);
        Assert.False(Directory.Exists(folder));
        Assert.DoesNotContain(value, output.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEF0000000000000000000000000000000000000000000000000000000000")]
    public async Task SearchVerificationRequiresATrustedLowercaseDigestBeforeReadingFiles(string digest)
    {
        var folder = NewFolder();
        Assert.Equal("SEARCH_MANIFEST_DIGEST_REQUIRED", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            SyntheticSearchVerification.VerifyAsync(folder, digest))).Message);
        Assert.False(Directory.Exists(folder));
    }

    private static async Task VerifyNativePlaybackAsync(string folder, string observation)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync();
        var network = new List<string>();
        page.Request += (_, request) => { if (request.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) network.Add(request.Url); };
        await page.GotoAsync(new Uri(Path.Combine(folder, "index.html")).AbsoluteUri);
        Assert.Equal(0, await page.Locator("script,form,iframe").CountAsync());
        Assert.Equal(observation == "AllTestedKeyboardGoalsObserved" ? "The keyboard searches reached the result."
            : "The mouse search worked. The keyboard searches did not reach the result.", await page.Locator("h1").InnerTextAsync());
        foreach (var width in new[] { 1024, 320 })
        {
            await page.SetViewportSizeAsync(width, 800);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        }
        await PlayEntireVideoAsync(page);
        for (var i = 1; i <= 3; i++)
        {
            await page.GotoAsync(new Uri(Path.Combine(folder, $"route-{i}.html")).AbsoluteUri);
            await PlayEntireVideoAsync(page);
            var beforeSelection = await page.Locator("video").EvaluateAsync<double>("v => v.currentTime");
            await page.Locator("[data-evidence=a2]").ClickAsync();
            Assert.Equal("a2: EnterSearchText", await page.Locator("#selected-label").InnerTextAsync());
            Assert.Contains("Completed calls 13", await page.Locator("#selected-detail").InnerTextAsync());
            Assert.Contains("No video position is inferred", await page.Locator("#selected-interval").InnerTextAsync());
            Assert.Equal(beforeSelection, await page.Locator("video").EvaluateAsync<double>("v => v.currentTime"), 2);
        }
        Assert.Empty(network);
    }

    private static async Task RejectRehashedCopyAsync(string originalFolder, byte[] originalManifest, string changedFile,
        Action<JsonNode?> change, string expectedCode)
    {
        var copy = NewFolder();
        foreach (var file in Directory.EnumerateFiles(originalFolder, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(copy, Path.GetRelativePath(originalFolder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
        var path = Path.Combine(copy, changedFile);
        var document = JsonNode.Parse(await File.ReadAllBytesAsync(path));
        change(document);
        var altered = JsonSerializer.SerializeToUtf8Bytes(document, SyntheticSearchRecordings.Json);
        await File.WriteAllBytesAsync(path, altered);
        var manifest = JsonNode.Parse(originalManifest)!;
        var entry = manifest["Files"]!.AsArray().Single(f => f!["Path"]!.GetValue<string>() == changedFile)!;
        entry["Bytes"] = altered.LongLength;
        entry["Sha256"] = TraversalArchive.Hash(altered);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, SyntheticSearchRecordings.Json);
        await File.WriteAllBytesAsync(Path.Combine(copy, SyntheticSearchRecordings.ManifestFile), bytes);
        Assert.Equal(expectedCode, (await Assert.ThrowsAsync<InvalidDataException>(() =>
            SyntheticSearchVerification.VerifyAsync(copy, TraversalArchive.Hash(bytes)))).Message);
    }

    private static async Task PlayEntireVideoAsync(IPage page)
    {
        var status = await page.Locator("video").EvaluateAsync<JsonElement>("""
            async v => {
              v.currentTime = 0;
              const outcome = new Promise(resolve => {
                const timer = setTimeout(() => finish(false), 20000);
                const finish = ok => { clearTimeout(timer); v.removeEventListener('ended', ended); v.removeEventListener('error', failed);
                  resolve({ok, ended:v.ended, error:v.error?.code ?? null, frames:v.getVideoPlaybackQuality().totalVideoFrames, duration:v.duration, current:v.currentTime}); };
                const ended = () => finish(true), failed = () => finish(false);
                v.addEventListener('ended', ended); v.addEventListener('error', failed);
              });
              await v.play(); return outcome;
            }
            """, null, new() { Timeout = 25000 });
        Assert.True(status.GetProperty("ok").GetBoolean(), status.GetRawText());
        Assert.True(status.GetProperty("ended").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("error").ValueKind);
        Assert.True(status.GetProperty("frames").GetInt32() > 0);
        Assert.Equal(status.GetProperty("duration").GetDouble(), status.GetProperty("current").GetDouble(), 2);
    }

    private static string NewFolder() => Path.GetFullPath(Path.Combine("artifacts", "synthetic-search-package-tests", Guid.NewGuid().ToString("N")));
}
