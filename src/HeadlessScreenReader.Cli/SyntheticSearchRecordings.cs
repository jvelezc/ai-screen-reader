using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.ScreenReader;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

public sealed record SearchPointerRecording(SearchPointerPreparation? Preparation, PointerTaskReceipt? Activation,
    FixtureBrowserEnvironment? Environment, RecordingProbeMedia? Media, CaptureStatus? Capture,
    BrowserShutdownStatus? Shutdown, string Stage, string? ErrorCode);

public sealed record SyntheticSearchManifest(string Schema, DateTimeOffset CreatedAtUtc, SyntheticSearchSite Source,
    ImmutableArray<MockTaskRun> KeyboardRuns, string? PointerRunId, string Observation, string? CollectionStoppedReason,
    string ProducerSha256, string ProducerVersion, string Runtime, string Platform, string Viewport,
    ImmutableArray<TraversalPackageFile> Files)
{
    public string Scope => "LocalEmbeddedSyntheticSearchExperiments";
    public string VideoAlignment => "UnverifiedWithoutCalibrationMarkers";
    public string WebsiteEvidenceAssessment => "NotAssessed";
    public string ExternalAssistiveTechnologyComparison => "NotPerformed";
    public bool LegalCaseDetermined => false;
    public int PlannedKeyboardRuns => 3;
}

/// <summary>Append-only local search experiments. This entry point grants no hosted or public-site input.</summary>
public static class SyntheticSearchRecordings
{
    public const string Schema = "hsr-synthetic-search-recordings-v1";
    public const string ManifestFile = "search-manifest.json";
    internal static readonly JsonSerializerOptions Json = new(TraversalDemo.JsonOptions)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public static Task<MockTaskExport> RecordAsync(string folder, BundledFixture fixture, CancellationToken token = default) =>
        RecordCoreAsync(folder, fixture, TraversalRecordingDemo.RunSyntheticSearchRouteAsync, RecordPointerAsync, token);

    internal static async Task<MockTaskExport> RecordCoreAsync(string folder, BundledFixture fixture,
        Func<string, BundledFixture, string, CancellationToken, Task<RecordedTraversalResult>> keyboard,
        Func<string, BundledFixture, CancellationToken, Task<SearchPointerRecording>> mouse, CancellationToken token)
    {
        var source = SyntheticSearchSites.Describe(fixture);
        folder = Path.GetFullPath(folder);
        token.ThrowIfCancellationRequested();
        if (Directory.Exists(folder) || File.Exists(folder) || File.Exists(folder + ".zip"))
            throw new IOException("SEARCH_OUTPUT_ALREADY_EXISTS");
        Directory.CreateDirectory(folder);
        await SaveAsync(folder, "fixture-source.txt", BundledFixtureCatalog.ReadHtml(fixture)).ConfigureAwait(false);
        var results = new List<RecordedTraversalResult>();
        var runs = ImmutableArray.CreateBuilder<MockTaskRun>();
        string? stopped = null;
        foreach (var key in new[] { "Enter", "Enter", "Space" })
        {
            token.ThrowIfCancellationRequested();
            var route = results.Count + 1;
            var result = await keyboard(Path.Combine(folder, $"route-{route}-media"), fixture, key, token).ConfigureAwait(false);
            results.Add(result);
            // Preserve a failed/uncertain attempt before deciding whether any further input is safe.
            await SaveJsonAsync(folder, $"route-{route}-recording.json", result).ConfigureAwait(false);
            if (result.Receipt is { } receipt)
                await TraversalArtifactWriter.WriteAsync(folder, route, receipt, result.Environment, result.Recording).ConfigureAwait(false);
            runs.Add(Summarize(route, key, result));
            if (!SyntheticSearchAssessment.CompleteKeyboard(result, source, key))
            {
                stopped = "SEARCH_ATTEMPT_INCOMPLETE_NO_FURTHER_INPUT";
                break;
            }
        }
        SearchPointerRecording? pointer = null;
        if (stopped is null)
        {
            token.ThrowIfCancellationRequested();
            pointer = await mouse(Path.Combine(folder, "pointer-media"), fixture, token).ConfigureAwait(false);
            await SaveJsonAsync(folder, "pointer.json", pointer).ConfigureAwait(false);
            await SaveJsonAsync(folder, "pointer-graph.json", SyntheticSearchPointerProjection.Build(pointer.Preparation, pointer.Activation)).ConfigureAwait(false);
        }
        var observation = SyntheticSearchAssessment.Describe(results, pointer, source);
        await SaveAsync(folder, "index.html", SyntheticSearchViewer.Render(source, runs.ToImmutable(), pointer, observation, stopped)).ConfigureAwait(false);
        var assembly = typeof(SyntheticSearchRecordings).Assembly;
        await using var producer = File.OpenRead(assembly.Location);
        var manifest = new SyntheticSearchManifest(Schema, DateTimeOffset.UtcNow, source, runs.ToImmutable(), pointer?.Activation?.RunId,
            observation, stopped, Convert.ToHexStringLower(await SHA256.HashDataAsync(producer, token).ConfigureAwait(false)),
            assembly.GetName().Version?.ToString() ?? "unknown", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription, "320x240", await TraversalEvidencePackage.InventoryAsync(folder).ConfigureAwait(false));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        var digest = TraversalArchive.Hash(bytes);
        await SaveAsync(folder, ManifestFile, Encoding.UTF8.GetString(bytes)).ConfigureAwait(false);
        await SyntheticSearchVerification.VerifyAsync(folder, digest).ConfigureAwait(false);
        var archive = folder + ".zip";
        await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in await TraversalEvidencePackage.InventoryAsync(folder).ConfigureAwait(false))
            {
                await using var entry = zip.CreateEntry(file.Path, CompressionLevel.NoCompression).Open();
                var actual = await TraversalEvidencePackage.CopyAndHashAsync(TraversalEvidencePackage.Resolve(folder, file.Path), entry).ConfigureAwait(false);
                if (actual.Bytes != file.Bytes || actual.Sha256 != file.Sha256) throw new InvalidDataException("SEARCH_ORIGINAL_CHANGED");
            }
        }
        await SyntheticSearchVerification.VerifyAsync(folder, digest).ConfigureAwait(false);
        await using var saved = File.OpenRead(archive);
        return new(folder, archive, digest, Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, token).ConfigureAwait(false)),
            observation, runs.Count, pointer?.Activation?.GoalObservation ?? "Unavailable");
    }

    internal static MockTaskRun Summarize(int route, string key, RecordedTraversalResult result) => new(route, key, result.Receipt?.RunId,
        result.Receipt?.Execution.ToString() ?? "NotStarted", result.Receipt?.Reachability.ToString() ?? "Unknown",
        result.Receipt?.AttemptedKeystrokes ?? 0, result.Receipt?.CompletedKeystrokes ?? 0,
        result.Receipt?.HasRecording == true, result.Recording.ErrorCode);

    private static Task SaveAsync(string folder, string name, string text) => TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, name), text);
    private static Task SaveJsonAsync<T>(string folder, string name, T value) => SaveAsync(folder, name, JsonSerializer.Serialize(value, Json));

    internal static async Task<SearchPointerRecording> RecordPointerAsync(string folder, BundledFixture fixture, CancellationToken token)
    {
        _ = SyntheticSearchSites.Describe(fixture);
        ChromiumRecordingProbe? probe = null; SearchFocusReceipt? focus = null; ReaderStep? entry = null;
        AccessibilitySnapshot? after = null; PointerTaskReceipt? activation = null; RecordingProbeMedia? media = null;
        ImmutableArray<ReaderEvent> timeline = []; DateTimeOffset? readerStarted = null; var timelineTruncated = false;
        var stage = "Launch"; string? error = null;
        try
        {
            await using var owned = await ChromiumFixtureBrowser.LaunchSyntheticSearchRecordingAsync(folder, fixture, token).ConfigureAwait(false);
            probe = owned;
            stage = "MouseFocus";
            focus = await owned.FocusSyntheticSearchAsync(token).ConfigureAwait(false);
            if (focus is { FocusObserved: true, InputCompleted: true, ErrorCode: null })
            {
                stage = "QueryEntry";
                readerStarted = DateTimeOffset.UtcNow;
                using var reader = new ScreenReaderSession(owned.Browser);
                await reader.ExecuteAsync(ReaderCommand.EnterFocusMode, cancellationToken: token).ConfigureAwait(false);
                entry = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query, cancellationToken: token).ConfigureAwait(false);
                timeline = reader.Timeline;
                timelineTruncated = reader.TimelineTruncated;
                after = await owned.Browser.CaptureAsync(token).ConfigureAwait(false);
                if (entry is { ErrorCode: null, BrowserKeystrokes: 13, SearchInput: { ValueVerified: true, CompletedCharacters: 13 } }
                    && after.FocusedNode is { } node)
                {
                    stage = "MouseActivation";
                    activation = await owned.ObserveSyntheticSearchPointerAsync(new("e", after.DocumentId, after.CaptureEpoch,
                        node.Id, SyntheticSearchPolicy.Query[..^1]), token).ConfigureAwait(false);
                }
                else error = "SEARCH_POINTER_ENTRY_UNCONFIRMED";
            }
            else error = "SEARCH_POINTER_FOCUS_UNCONFIRMED";
            await Task.Delay(120, token).ConfigureAwait(false);
            stage = "FinalizeAndDecodeOriginalMedia";
            media = await owned.FinishAndInspectAsync().ConfigureAwait(false);
            stage = "Complete";
        }
        catch (OperationCanceledException) { error = "SEARCH_POINTER_CANCELLED"; }
        catch (BrowserOperationException e) { error = e.Code; }
        catch (Exception e) when (e is IOException or TimeoutException or Microsoft.Playwright.PlaywrightException or UnauthorizedAccessException)
        { error = "SEARCH_POINTER_UNCONFIRMED"; }
        var preparation = new SearchPointerPreparation(focus, readerStarted, entry, timeline, after, error) { TimelineTruncated = timelineTruncated };
        return new(preparation, activation, probe?.Environment, media, probe?.CaptureStatus, probe?.ShutdownStatus, stage, error);
    }
}
