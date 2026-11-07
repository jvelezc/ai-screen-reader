using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

public sealed record MockTaskRun(int Route, string Key, string? RunId, string Execution,
    string Reachability, int AttemptedKeys, int CompletedKeys, bool HasVideo, string? ErrorCode);
public sealed record MockPointerRecording(PointerTaskReceipt? Receipt, FixtureBrowserEnvironment? Environment,
    RecordingProbeMedia? Media, CaptureStatus? Capture, BrowserShutdownStatus? Shutdown, string? ErrorCode);
public sealed record MockTaskManifest(string Schema, DateTimeOffset CreatedAtUtc, MockSiteTask Source,
    ImmutableArray<MockTaskRun> KeyboardRuns, string? PointerRunId, string PointerGoalObservation,
    string Observation, string ProducerSha256, string ProducerVersion, string Runtime, string Platform,
    string Viewport, ImmutableArray<TraversalPackageFile> Files)
{
    public string Scope => "LocalEmbeddedMockTaskExperiments";
    public string VideoAlignment => "UnverifiedWithoutCalibrationMarkers";
    public string WebsiteEvidenceAssessment => "NotAssessed";
    public string ExternalAssistiveTechnologyComparison => "NotPerformed";
    public bool LegalCaseDetermined => false;
}
public sealed record MockTaskExport(string Folder, string Archive, string ManifestSha256, string ArchiveSha256,
    string Observation, int KeyboardRuns, string PointerGoalObservation);

/// <summary>Fresh, source-bound lab experiments and passive local playback. No hosted admission or public-site input.</summary>
public static class MockTaskRecordings
{
    public const string Schema = "hsr-mock-task-recordings-v1";
    public const string ManifestFile = "mock-task-manifest.json";
    private static readonly JsonSerializerOptions Json = new(TraversalDemo.JsonOptions)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public static async Task<MockTaskExport> RecordAsync(string folder, BundledFixture fixture, CancellationToken token = default)
    {
        var task = MockSiteTasks.Describe(fixture);
        folder = Path.GetFullPath(folder);
        token.ThrowIfCancellationRequested();
        if (Directory.Exists(folder) || File.Exists(folder) || File.Exists(folder + ".zip"))
            throw new IOException("MOCK_TASK_OUTPUT_ALREADY_EXISTS");
        Directory.CreateDirectory(folder);
        var results = new List<RecordedTraversalResult>();
        var runs = ImmutableArray.CreateBuilder<MockTaskRun>();
        foreach (var key in new[] { "Enter", "Enter", "Space" })
        {
            token.ThrowIfCancellationRequested();
            var route = results.Count + 1;
            var result = await TraversalRecordingDemo.RunMockRouteAsync(Path.Combine(folder, $"route-{route}-media"), fixture, key, token).ConfigureAwait(false);
            results.Add(result);
            if (result.Receipt is { } receipt)
                await TraversalArtifactWriter.WriteAsync(folder, route, receipt, result.Environment, result.Recording).ConfigureAwait(false);
            else
                await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, $"route-{route}-failure.json"), JsonSerializer.Serialize(result, Json)).ConfigureAwait(false);
            runs.Add(new(route, key, result.Receipt?.RunId, result.Receipt?.Execution.ToString() ?? "NotStarted",
                result.Receipt?.Reachability.ToString() ?? "Unknown", result.Receipt?.AttemptedKeystrokes ?? 0,
                result.Receipt?.CompletedKeystrokes ?? 0, result.Receipt?.HasRecording == true, result.Recording.ErrorCode));
        }
        token.ThrowIfCancellationRequested();
        var pointer = await RecordPointerAsync(Path.Combine(folder, "pointer-media"), fixture, token).ConfigureAwait(false);
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "pointer.json"), JsonSerializer.Serialize(pointer, Json)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "fixture-source.txt"), BundledFixtureCatalog.ReadHtml(fixture)).ConfigureAwait(false);
        var observation = DescribeObservation(results, pointer);
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "index.html"), Render(task, runs.ToImmutable(), pointer, observation)).ConfigureAwait(false);
        var assembly = typeof(MockTaskRecordings).Assembly;
        await using var producer = File.OpenRead(assembly.Location);
        var producerDigest = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(producer, token).ConfigureAwait(false));
        var manifest = new MockTaskManifest(Schema, DateTimeOffset.UtcNow, task, runs.ToImmutable(), pointer.Receipt?.RunId,
            pointer.Receipt?.GoalObservation ?? "Unavailable", observation, producerDigest, assembly.GetName().Version?.ToString() ?? "unknown",
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, System.Runtime.InteropServices.RuntimeInformation.OSDescription, "320x240",
            await TraversalEvidencePackage.InventoryAsync(folder).ConfigureAwait(false));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        var digest = TraversalArchive.Hash(bytes);
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, ManifestFile), Encoding.UTF8.GetString(bytes)).ConfigureAwait(false);
        await VerifyAsync(folder, digest).ConfigureAwait(false);
        var archive = folder + ".zip";
        await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in await TraversalEvidencePackage.InventoryAsync(folder).ConfigureAwait(false))
            {
                await using var entry = zip.CreateEntry(file.Path, CompressionLevel.NoCompression).Open();
                var actual = await TraversalEvidencePackage.CopyAndHashAsync(TraversalEvidencePackage.Resolve(folder, file.Path), entry).ConfigureAwait(false);
                if (actual.Bytes != file.Bytes || actual.Sha256 != file.Sha256) throw new InvalidDataException("MOCK_TASK_ORIGINAL_CHANGED");
            }
        }
        await VerifyAsync(folder, digest).ConfigureAwait(false);
        await using var saved = File.OpenRead(archive);
        var archiveDigest = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(saved, token).ConfigureAwait(false));
        return new(folder, archive, digest, archiveDigest, observation, manifest.KeyboardRuns.Length, manifest.PointerGoalObservation);
    }

    internal static string DescribeObservation(IReadOnlyList<RecordedTraversalResult> results, MockPointerRecording pointer)
    {
        const string incomplete = "IncompleteOrMixedResults";
        var source = results.FirstOrDefault()?.Environment?.Content;
        if (source is null || !MockSiteTasks.Fixtures.Contains(source.Fixture)
            || source.ContentSha256 != MockSiteTasks.Describe(source.Fixture).SourceSha256
            || pointer.Environment?.Content != source) return incomplete;
        var goal = MockSiteTasks.Describe(source.Fixture).KeyboardPlan("Enter").Destination;
        bool Complete(RecordedTraversalResult r) => r.Environment?.Content == source && r.Receipt is
        {
            Execution: TraversalExecution.Completed, Policy: TraversalPolicy.Allowed, UnexecutedInstructions: 0, TimelineTruncated: false,
            HasRecording: true, AttemptedKeystrokes: 2, CompletedKeystrokes: 2
        }
            && r.Recording.ErrorCode is null && r.Recording.Stage == "Complete"
            && r.Recording.Media is { Source.IsDefaultOrEmpty: false, Video.IsDefaultOrEmpty: false }
            && r.Recording.CaptureLifecycle is { Phase: CapturePhase.Closed, EventsTruncated: false }
            && r.Recording.Shutdown is { Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0, ErrorCode: null, ProcessExitConfirmed: true, CollectorStopAcknowledged: true }
            && !r.Receipt.Observations.IsDefaultOrEmpty && !goal.IsSatisfiedBy(r.Receipt.Observations[0].Snapshot);
        if (results.Count != 3 || results.Any(r => !Complete(r)) || pointer.ErrorCode is not null
            || pointer.Receipt is not { Attempted: true, Completed: true, GoalObserved: true, ErrorCode: null, After: not null }
            || pointer.Media is not { Source.IsDefaultOrEmpty: false, Video.IsDefaultOrEmpty: false }
            || pointer.Capture is not { Phase: CapturePhase.Closed, EventsTruncated: false }
            || pointer.Shutdown is not { Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0, ErrorCode: null, ProcessExitConfirmed: true, CollectorStopAcknowledged: true }
            || goal.IsSatisfiedBy(pointer.Receipt.Before) || !goal.IsSatisfiedBy(pointer.Receipt.After)) return incomplete;
        if (results.Select(r => r.Receipt!.RunId).Append(pointer.Receipt.RunId).Distinct(StringComparer.Ordinal).Count() != 4
            || results.Select(r => r.Receipt!.Observations[0].Snapshot.DocumentId).Append(pointer.Receipt.Before.DocumentId).Distinct(StringComparer.Ordinal).Count() != 4)
            return incomplete;
        for (var i = 0; i < 3; i++)
            if (!results[i].Receipt!.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail)
                .SequenceEqual(new[] { "Tab", i == 2 ? "Space" : "Enter" })) return incomplete;
        if (results.All(r => r.Receipt!.Reachability == TraversalReachability.Reached && goal.IsSatisfiedBy(r.Receipt.Observations[^1].Snapshot)))
            return "AllTestedKeyboardGoalsObserved";
        if (results.All(r => r.Receipt!.Reachability == TraversalReachability.Unknown
            && !r.Receipt.Observations.Any(o => goal.IsSatisfiedBy(o.Snapshot))
            && r.Receipt.Attempts[^1].Step?.Verification == VerificationStatus.TimedOut))
            return "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved";
        return incomplete;
    }

    private static async Task<MockPointerRecording> RecordPointerAsync(string folder, BundledFixture fixture, CancellationToken token)
    {
        PointerTaskReceipt? receipt = null; ChromiumRecordingProbe? probe = null; RecordingProbeMedia? media = null;
        string? error = null;
        try
        {
            await using var owned = await ChromiumFixtureBrowser.LaunchMockRecordingAsync(folder, fixture, token).ConfigureAwait(false);
            probe = owned;
            receipt = await owned.ObserveMockPointerAsync(token).ConfigureAwait(false);
            // Let an actual post-action capture arrive; this hold is not task effort.
            await Task.Delay(120, token).ConfigureAwait(false);
            media = await owned.FinishAndInspectAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { error = "MOCK_POINTER_RECORDING_CANCELLED"; }
        catch (Exception e) when (e is BrowserOperationException or IOException or TimeoutException or Microsoft.Playwright.PlaywrightException)
        { error = "MOCK_POINTER_RECORDING_UNCONFIRMED"; }
        return new(receipt, probe?.Environment, media, probe?.CaptureStatus, probe?.ShutdownStatus, error);
    }

    public static async Task<TraversalPackageVerification> VerifyAsync(string folder, string trustedManifestSha256)
    {
        if (trustedManifestSha256 is not { Length: 64 } || !trustedManifestSha256.All(char.IsAsciiHexDigitLower))
            throw new InvalidDataException("MOCK_TASK_MANIFEST_DIGEST_REQUIRED");
        folder = Path.GetFullPath(folder);
        var inventory = await TraversalEvidencePackage.InventoryAsync(folder).ConfigureAwait(false);
        var manifestFile = inventory.SingleOrDefault(f => f.Path == ManifestFile)
            ?? throw new InvalidDataException("MOCK_TASK_MANIFEST_MISSING");
        if (manifestFile.Bytes > TraversalEvidencePackage.MaximumManifestBytes || manifestFile.Sha256 != trustedManifestSha256)
            throw new InvalidDataException("MOCK_TASK_MANIFEST_CHANGED");
        var bytes = await File.ReadAllBytesAsync(TraversalEvidencePackage.Resolve(folder, ManifestFile)).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
        var root = document.RootElement;
        bool Fixed(string name, string value) => root.TryGetProperty(name, out var item)
            && item.ValueKind == JsonValueKind.String && item.GetString() == value;
        if (!Fixed("Scope", "LocalEmbeddedMockTaskExperiments") || !Fixed("VideoAlignment", "UnverifiedWithoutCalibrationMarkers")
            || !Fixed("WebsiteEvidenceAssessment", "NotAssessed") || !root.TryGetProperty("LegalCaseDetermined", out var legal)
            || !Fixed("ExternalAssistiveTechnologyComparison", "NotPerformed")
            || legal.ValueKind != JsonValueKind.False)
            throw new InvalidDataException("MOCK_TASK_SCOPE_INVALID");
        var manifest = JsonSerializer.Deserialize<MockTaskManifest>(bytes, Json)
            ?? throw new InvalidDataException("MOCK_TASK_MANIFEST_INVALID");
        if (manifest.Schema != Schema || manifest.CreatedAtUtc == default || manifest.Source != MockSiteTasks.Describe(manifest.Source.Fixture)
            || manifest.ProducerSha256 is not { Length: 64 } || !manifest.ProducerSha256.All(char.IsAsciiHexDigitLower)
            || new[] { manifest.ProducerVersion, manifest.Runtime, manifest.Platform }.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 500 || s.Any(char.IsControl))
            || manifest.Viewport != "320x240"
            || manifest.Files.IsDefaultOrEmpty || manifest.Files.Length > TraversalEvidencePackage.MaximumFiles
            || manifest.KeyboardRuns.IsDefault || manifest.KeyboardRuns.Length != 3
            || !manifest.KeyboardRuns.Select(r => r.Route).SequenceEqual(new[] { 1, 2, 3 })
            || !manifest.KeyboardRuns.Select(r => r.Key).SequenceEqual(new[] { "Enter", "Enter", "Space" })
            || manifest.KeyboardRuns.Where(r => r.RunId is not null).Select(r => r.RunId).Distinct(StringComparer.Ordinal).Count()
                != manifest.KeyboardRuns.Count(r => r.RunId is not null)
            || manifest.PointerRunId is not null && manifest.KeyboardRuns.Any(r => r.RunId == manifest.PointerRunId)
            || manifest.Observation is not ("IncompleteOrMixedResults" or "AllTestedKeyboardGoalsObserved" or "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved")
            || !manifest.Files.SequenceEqual(inventory.Where(f => f.Path != ManifestFile)))
            throw new InvalidDataException("MOCK_TASK_INVENTORY_CHANGED");
        var source = manifest.Files.SingleOrDefault(f => f.Path == "fixture-source.txt");
        if (source?.Sha256 != manifest.Source.SourceSha256) throw new InvalidDataException("MOCK_TASK_SOURCE_CHANGED");
        return new(true, trustedManifestSha256, manifest.Files.Length, manifest.Files.Sum(f => f.Bytes));
    }

    private static string Render(MockSiteTask task, ImmutableArray<MockTaskRun> runs, MockPointerRecording pointer, string observation)
    {
        static string H(string text) => WebUtility.HtmlEncode(text);
        var headline = observation switch
        {
            "AllTestedKeyboardGoalsObserved" => "Every keyboard attempt reached the goal.",
            "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved" => "The goal was not observed after the keyboard attempts.",
            _ => "Some test records are incomplete."
        };
        const string css = """
            :root{color-scheme:light;--ink:#182d32;--paper:#f7f6f2;--line:#bdc7c7;--accent:#815117}
            *{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:1.1rem/1.65 Palatino,'Book Antiqua',serif}
            main{max-width:74rem;margin:auto;padding:clamp(1.2rem,5vw,4rem)}.eyebrow,nav,a,small{font-family:'Trebuchet MS',sans-serif}
            .eyebrow{letter-spacing:.12em;font-size:.75rem;font-weight:bold;color:var(--accent)}h1{max-width:22ch;font-size:clamp(2rem,4vw,3.5rem);line-height:1.13;margin:.6rem 0 1.2rem}
            h2{font-size:1.6rem;line-height:1.25}.summary{max-width:65ch}.columns{display:grid;grid-template-columns:1fr 1fr;gap:3rem;border-top:1px solid var(--line);margin-top:2rem;padding-top:1rem}
            ol{padding-left:1.5rem}li{border-bottom:1px solid var(--line);padding:1rem 0}li p{margin:.25rem 0}a{color:var(--ink);text-underline-offset:.2em}a:focus-visible,video:focus-visible{outline:3px solid var(--accent);outline-offset:5px}
            video{width:100%;max-height:28rem;background:#14262b}.scope{border-left:3px solid var(--accent);padding-left:1rem}.technical{border-top:1px solid var(--line);margin-top:3rem;padding-top:1rem}
            .skip{position:absolute;top:-8rem;left:1rem}.skip:focus{top:1rem;background:white;padding:1rem}small{display:block;font-size:.9rem}
            @media(max-width:48rem){.columns{grid-template-columns:1fr;gap:1.5rem}}
            """;
        var styleHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(css)));
        var text = new StringBuilder("<!doctype html><html lang=en><head><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\">")
            .Append("<meta http-equiv=Content-Security-Policy content=\"default-src 'none'; media-src 'self' file:; base-uri 'none'; form-action 'none'; style-src 'sha256-")
            .Append(styleHash).Append("'\"><title>Recorded mock task</title><style>").Append(css).Append("</style></head><body><a class=skip href=#main>Skip to test results</a><main id=main>")
            .Append("<p class=eyebrow>PRIVENSIC SCREEN READER · RECORDED LAB EXAMPLE</p><h1>").Append(H(headline)).Append("</h1><div class=summary><p>Test page: ")
            .Append(H(task.Heading)).Append(".</p><p>Task: ").Append(task.Id.StartsWith("checkout-", StringComparison.Ordinal)
                ? "place the sample order using the keyboard." : "open the sample details using the keyboard.")
            .Append(" Expected result: ").Append(H(task.GoalName)).Append(".</p><p>We tried the task on three fresh pages: Enter twice, then Space. We tested a mouse click separately on another fresh page.</p>")
            .Append("<p class=scope>This is a synthetic test page, not a finding about a business. This example does not assess a real website’s supporting evidence.</p>")
            .Append("<p>Next: watch the attempts and compare their recorded results. Play each full clip; selecting a graph event cannot identify a verified video interval because event-to-video timing has not been verified.</p></div>")
            .Append("<div class=columns><section aria-labelledby=keyboard-title><h2 id=keyboard-title>Watch the keyboard attempts</h2><ol>");
        foreach (var run in runs)
        {
            var result = run.ErrorCode is not null ? "The recording is incomplete." : run.Reachability == "Reached"
                ? "The goal appeared." : "The goal was not observed in this attempt.";
            text.Append("<li><strong>Attempt ").Append(run.Route).Append(" · ").Append(H(run.Key)).Append("</strong><p>").Append(H(result)).Append("</p>");
            if (run.RunId is not null) text.Append("<a href=\"route-").Append(run.Route).Append(".html\">Open recording and step-by-step graph</a>");
            else text.Append("<a href=\"route-").Append(run.Route).Append("-failure.json\">Open collection details</a>");
            text.Append("</li>");
        }
        text.Append("</ol></section><section aria-labelledby=pointer-title><h2 id=pointer-title>Compare the mouse attempt</h2><p>")
            .Append(pointer.Receipt?.GoalObserved == true ? "The goal appeared after a fresh mouse click." : "The mouse result is unavailable or the goal was not observed.")
            .Append("</p><p>This comparison has its own recording and observations. <a href=pointer.json>Open comparison records</a></p>");
        if (pointer.Media is not null) text.Append("<video controls preload=metadata src=pointer-media/original.webm aria-label=\"Original pointer comparison recording\"></video>");
        return text.Append("</section></div><section class=technical aria-labelledby=files-title><h2 id=files-title>Original files and test details</h2><nav aria-label=\"Evidence files\"><a href=fixture-source.txt>Exact test-page source</a> · <a href=mock-task-manifest.json>Saved files and fingerprints</a></nav><small>Keep the package digest separately. Hashes detect changed files relative to that digest. They do not establish collector identity, standing or admissibility.</small></section></main></body></html>").ToString();
    }
}
