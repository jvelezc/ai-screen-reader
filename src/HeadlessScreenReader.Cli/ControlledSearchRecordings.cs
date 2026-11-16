using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>Independent search profile. The caller supplies current authority and retained-original acknowledgements.</summary>
internal static class ControlledSearchRecordings
{
    internal static async Task<MockTaskExport> RecordAsync(string folder, SyntheticSearchTaskStart start,
        Func<int, string, CancellationToken, Task> boundary,
        Func<int, SyntheticSearchInputIntent, CancellationToken, Task<SyntheticSearchInputDecision>> native,
        Func<int, string, long, ImmutableArray<CompletedCaptureSource>, CancellationToken, Task> sources, CancellationToken token)
    {
        start.Validate();
        var fixture = start.Job.Source.Id == "search-working" ? BundledFixture.MockSearchWorkingV1 : BundledFixture.MockSearchKeyboardBlockedV1;
        var source = SyntheticSearchSites.Describe(fixture);
        if (source.SourceSha256 != start.Job.Source.Sha256) throw new BrowserOperationException("SEARCH_TASK_SOURCE_INVALID");
        folder = Path.GetFullPath(folder);
        token.ThrowIfCancellationRequested();
        if (Directory.Exists(folder) || File.Exists(folder) || File.Exists(folder + ".zip")) throw new IOException("SEARCH_OUTPUT_ALREADY_EXISTS");
        Directory.CreateDirectory(folder);
        await Save(folder, "search-task-execution.json", JsonSerializer.Serialize(start, ReaderWorkerWire.Json)).ConfigureAwait(false);
        await Save(folder, "fixture-source.txt", BundledFixtureCatalog.ReadHtml(fixture)).ConfigureAwait(false);
        var results = new List<RecordedTraversalResult>();
        var trials = ImmutableArray.CreateBuilder<SyntheticSearchTaskTrial>();
        for (var trial = 1; trial <= 4; trial++)
        {
            token.ThrowIfCancellationRequested();
            await boundary(trial, "Open", token).ConfigureAwait(false);
            var result = await TraversalRecordingDemo.RunControlledSearchRouteAsync(Path.Combine(folder, $"route-{trial}-media"), fixture, trial,
                (input, cancellation) => native(trial, input, cancellation), cancellation => boundary(trial, "FocusMode", cancellation),
                (document, epoch, originals, cancellation) => sources(trial, document, epoch, originals, cancellation), token).ConfigureAwait(false);
            await Save(folder, $"route-{trial}-recording.json", JsonSerializer.Serialize(result, TraversalDemo.JsonOptions)).ConfigureAwait(false);
            if (result.Receipt is { } receipt)
                await TraversalArtifactWriter.WriteAsync(folder, trial, receipt, result.Environment, result.Recording).ConfigureAwait(false);
            if (!ControlledSearchAssessment.Complete(result, source, trial))
                throw new BrowserOperationException("SEARCH_TASK_COLLECTION_INCOMPLETE_NO_FURTHER_INPUT");
            results.Add(result);
            var actual = result.Receipt!;
            trials.Add(new(trial, trial <= 2 ? "FieldEnter" : trial == 3 ? "ButtonEnter" : "ButtonSpace", actual.RunId,
                actual.Execution.ToString(), actual.Reachability.ToString(), actual.AttemptedKeystrokes, actual.CompletedKeystrokes));
        }
        await boundary(5, "Open", token).ConfigureAwait(false);
        var pointer = await ControlledSearchPointer.RecordAsync(Path.Combine(folder, "pointer-media"), fixture,
            (input, cancellation) => native(5, input, cancellation), cancellation => boundary(5, "FocusMode", cancellation),
            (document, epoch, originals, cancellation) => sources(5, document, epoch, originals, cancellation), token).ConfigureAwait(false);
        await Save(folder, "pointer.json", JsonSerializer.Serialize(pointer, TraversalDemo.JsonOptions)).ConfigureAwait(false);
        await Save(folder, "pointer-graph.json", JsonSerializer.Serialize(SyntheticSearchPointerProjection.Build(pointer.Preparation, pointer.Activation), TraversalDemo.JsonOptions)).ConfigureAwait(false);
        if (!ControlledSearchAssessment.PointerComplete(pointer, source)) throw new BrowserOperationException("SEARCH_TASK_POINTER_INCOMPLETE");
        var documents = results.Select(result => result.Receipt!.Observations[0].Snapshot.DocumentId).Append(pointer.Preparation!.Focus!.Before.DocumentId);
        if (documents.Distinct(StringComparer.Ordinal).Count() != 5 || results.Select(result => result.Receipt!.RunId).Append(pointer.Activation!.RunId).Distinct(StringComparer.Ordinal).Count() != 5)
            throw new BrowserOperationException("SEARCH_TASK_FRESH_CONTEXTS_UNCONFIRMED");
        var goal = SyntheticSearchTaskControl.KeyboardPlan(1).Destination;
        var observation = results.All(result => result.Receipt!.Reachability == TraversalReachability.Reached && goal.IsSatisfiedBy(result.Receipt.Observations[^1].Snapshot))
            ? "AllTestedKeyboardGoalsObserved"
            : results.Any(result => result.Receipt!.Reachability == TraversalReachability.Reached && goal.IsSatisfiedBy(result.Receipt.Observations[^1].Snapshot))
                ? "AKeyboardAlternativeReachedTheGoal"
                : results.All(result => result.Receipt!.Reachability == TraversalReachability.Unknown
                    && !result.Receipt.Observations.Any(item => goal.IsSatisfiedBy(item.Snapshot))
                    && result.Receipt.Attempts[^1].Step?.Verification == VerificationStatus.TimedOut)
                    ? "KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved" : "IncompleteOrMixedResults";
        await Save(folder, "index.html", Index(source, trials.ToImmutable(), observation)).ConfigureAwait(false);
        var files = await TraversalEvidencePackage.InventoryAsync(folder).ConfigureAwait(false);
        if (files.Sum(file => file.Bytes) > SyntheticSearchSourceProtocol.MaximumOriginalBytes) throw new BrowserOperationException("SEARCH_TASK_ORIGINAL_BUDGET_EXCEEDED");
        var assembly = typeof(ControlledSearchRecordings).Assembly;
        await using var producer = File.OpenRead(assembly.Location);
        var manifest = new SyntheticSearchTaskManifest(SyntheticSearchTaskManifest.CurrentSchema, DateTimeOffset.UtcNow, start, trials.ToImmutable(), pointer.Activation.RunId,
            observation, Convert.ToHexStringLower(await SHA256.HashDataAsync(producer, token).ConfigureAwait(false)), assembly.GetName().Version?.ToString() ?? "unknown",
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, System.Runtime.InteropServices.RuntimeInformation.OSDescription, "320x240",
            [.. files.Select(file => new SyntheticSearchTaskOriginal(file.Path, file.Bytes, file.Sha256))]);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ReaderWorkerWire.Json);
        await Save(folder, SyntheticSearchTaskManifest.ManifestFile, System.Text.Encoding.UTF8.GetString(manifestBytes)).ConfigureAwait(false);
        var archive = folder + ".zip";
        await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in await TraversalEvidencePackage.InventoryAsync(folder).ConfigureAwait(false))
            {
                await using var entry = zip.CreateEntry(file.Path, CompressionLevel.NoCompression).Open();
                var actual = await TraversalEvidencePackage.CopyAndHashAsync(TraversalEvidencePackage.Resolve(folder, file.Path), entry).ConfigureAwait(false);
                if (actual.Bytes != file.Bytes || actual.Sha256 != file.Sha256) throw new IOException("SEARCH_TASK_ORIGINAL_CHANGED");
            }
        }
        await boundary(0, "Publish", token).ConfigureAwait(false);
        await using var saved = File.OpenRead(archive);
        return new(folder, archive, TraversalArchive.Hash(manifestBytes), Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, token).ConfigureAwait(false)),
            observation, 4, pointer.Activation.GoalObservation);
    }

    private static Task Save(string folder, string name, string text) => TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, name), text);
    internal static string Index(SyntheticSearchSite source, ImmutableArray<SyntheticSearchTaskTrial> trials, string observation) =>
        "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<title>Search test recordings</title><main><h1>Search test recordings</h1><p>" +
        (source.Id == "search-working" ? "Working search reference" : "Deliberately keyboard-blocked search") + "</p><p>" +
        (observation switch
        {
            "AllTestedKeyboardGoalsObserved" => "The tested keyboard paths reached the search result.",
            "AKeyboardAlternativeReachedTheGoal" => "A keyboard path reached the search result.",
            "KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved" => "The tested keyboard paths did not reach the search result. The mouse comparison reached it.",
            _ => "The recorded attempts do not establish a complete comparison."
        }) + "</p><p>Each attempt uses a fresh page and the same sample query. Choose a keyboard attempt to view its original recording and recorded path.</p><ol>" +
        string.Concat(trials.Select(trial => $"<li><a href=\"route-{trial.Trial}.html\">{trial.Trial switch { 1 => "Enter from search field — first attempt", 2 => "Enter from search field — second attempt", 3 => "Enter on Search button", _ => "Space on Search button" }}</a></li>")) +
        "</ol><h2>Original mouse comparison</h2><video controls preload=\"metadata\" aria-label=\"Original synthetic search mouse comparison\" src=\"pointer-media/original.webm\"></video>" +
        "<p><a href=\"pointer-graph.json\">View the recorded mouse and query path</a></p>" +
        "<p>These recordings have no external screen-reader audio or verified event-to-video alignment. Required website evidence has not been assessed. This fixed demonstration does not determine site-wide accessibility or a legal case.</p></main></html>";
}
