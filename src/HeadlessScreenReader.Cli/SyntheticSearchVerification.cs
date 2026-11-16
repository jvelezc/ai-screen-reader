using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>Local integrity and derived-fact verification; a caller-supplied digest is not collector authentication.</summary>
public static class SyntheticSearchVerification
{
    public static async Task<TraversalPackageVerification> VerifyAsync(string folder, string trustedManifestSha256)
    {
        if (!Hash(trustedManifestSha256)) throw new InvalidDataException("SEARCH_MANIFEST_DIGEST_REQUIRED");
        folder = Path.GetFullPath(folder);
        var inventory = await TraversalEvidencePackage.InventoryAsync(folder).ConfigureAwait(false);
        var manifestFile = inventory.SingleOrDefault(f => f.Path == SyntheticSearchRecordings.ManifestFile)
            ?? throw new InvalidDataException("SEARCH_MANIFEST_MISSING");
        if (manifestFile.Bytes > TraversalEvidencePackage.MaximumManifestBytes || manifestFile.Sha256 != trustedManifestSha256)
            throw new InvalidDataException("SEARCH_MANIFEST_CHANGED");
        var bytes = await ReadAsync(folder, SyntheticSearchRecordings.ManifestFile).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
        var root = document.RootElement;
        bool Fixed(string name, string value) => root.TryGetProperty(name, out var item)
            && item.ValueKind == JsonValueKind.String && item.GetString() == value;
        if (!Fixed("Scope", "LocalEmbeddedSyntheticSearchExperiments") || !Fixed("VideoAlignment", "UnverifiedWithoutCalibrationMarkers")
            || !Fixed("WebsiteEvidenceAssessment", "NotAssessed") || !Fixed("ExternalAssistiveTechnologyComparison", "NotPerformed")
            || !root.TryGetProperty("LegalCaseDetermined", out var legal) || legal.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("PlannedKeyboardRuns", out var planned) || !planned.TryGetInt32(out var count) || count != 3)
            throw new InvalidDataException("SEARCH_SCOPE_INVALID");
        var manifest = JsonSerializer.Deserialize<SyntheticSearchManifest>(bytes, SyntheticSearchRecordings.Json)
            ?? throw new InvalidDataException("SEARCH_MANIFEST_INVALID");
        if (!JsonElement.DeepEquals(root, JsonSerializer.SerializeToElement(manifest, SyntheticSearchRecordings.Json)))
            throw new InvalidDataException("SEARCH_DERIVED_FIELD_CHANGED");
        if (!SyntheticSearchSites.Fixtures.Contains(manifest.Source.Fixture)
            || manifest.Schema != SyntheticSearchRecordings.Schema || manifest.CreatedAtUtc == default
            || manifest.Source != SyntheticSearchSites.Describe(manifest.Source.Fixture)
            || !Hash(manifest.ProducerSha256)
            || new[] { manifest.ProducerVersion, manifest.Runtime, manifest.Platform }.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 500 || s.Any(char.IsControl))
            || manifest.Viewport != "320x240" || manifest.Files.IsDefaultOrEmpty
            || manifest.Files.Length > TraversalEvidencePackage.MaximumFiles
            || !manifest.Files.SequenceEqual(inventory.Where(f => f.Path != SyntheticSearchRecordings.ManifestFile))
            || manifest.KeyboardRuns.IsDefault || manifest.KeyboardRuns.Length is < 1 or > 3
            || !manifest.KeyboardRuns.Select(r => r.Route).SequenceEqual(Enumerable.Range(1, manifest.KeyboardRuns.Length))
            || !manifest.KeyboardRuns.Select(r => r.Key).SequenceEqual(new[] { "Enter", "Enter", "Space" }.Take(manifest.KeyboardRuns.Length))
            || manifest.Files.SingleOrDefault(f => f.Path == "fixture-source.txt")?.Sha256 != manifest.Source.SourceSha256)
            throw new InvalidDataException("SEARCH_INVENTORY_CHANGED");

        var results = new List<RecordedTraversalResult>();
        foreach (var run in manifest.KeyboardRuns)
        {
            var result = await ReadJsonAsync<RecordedTraversalResult>(folder, $"route-{run.Route}-recording.json").ConfigureAwait(false);
            if (run != SyntheticSearchRecordings.Summarize(run.Route, run.Key, result)) throw new InvalidDataException("SEARCH_RUN_SUMMARY_CHANGED");
            results.Add(result);
            CheckMedia(result.Recording.Media, $"route-{run.Route}-media/", manifest.Files);
            if (result.Receipt is { } receipt)
            {
                using var savedRoute = JsonDocument.Parse(await ReadAsync(folder, $"route-{run.Route}.json").ConfigureAwait(false), new() { AllowDuplicateProperties = false });
                var expected = JsonSerializer.SerializeToElement(new
                {
                    Receipt = receipt,
                    Graph = TraversalProjection.Build(receipt),
                    result.Environment,
                    result.Recording,
                    Features = (object?)null,
                    ViewerFile = $"route-{run.Route}.html"
                }, TraversalDemo.JsonOptions);
                if (!JsonElement.DeepEquals(savedRoute.RootElement, expected)) throw new InvalidDataException("SEARCH_ROUTE_GRAPH_CHANGED");
                CheckText(manifest.Files, $"route-{run.Route}.html", TraversalEvidenceViewer.Render(receipt, run.Route, result.Recording));
            }
        }
        var allComplete = results.Where((r, i) => !SyntheticSearchAssessment.CompleteKeyboard(r, manifest.Source, i == 2 ? "Space" : "Enter")).Any() == false;
        // A saved uncertain native attempt cannot be followed by a supposedly independent retry.
        if (results.Take(results.Count - 1).Where((r, i) => !SyntheticSearchAssessment.CompleteKeyboard(r, manifest.Source, i == 2 ? "Space" : "Enter")).Any()
            || manifest.CollectionStoppedReason != (allComplete ? null : "SEARCH_ATTEMPT_INCOMPLETE_NO_FURTHER_INPUT")
            || allComplete && results.Count != 3) throw new InvalidDataException("SEARCH_COLLECTION_ORDER_INVALID");
        SearchPointerRecording? pointer = null;
        if (allComplete)
        {
            pointer = await ReadJsonAsync<SearchPointerRecording>(folder, "pointer.json").ConfigureAwait(false);
            var graph = await ReadJsonAsync<SearchPointerGraphDocument>(folder, "pointer-graph.json").ConfigureAwait(false);
            var derived = SyntheticSearchPointerProjection.Build(pointer.Preparation, pointer.Activation);
            if (graph.Schema != derived.Schema || graph.Scope != derived.Scope
                || !JsonElement.DeepEquals(graph.Nodes, JsonSerializer.SerializeToElement(derived.Nodes, SyntheticSearchRecordings.Json))
                || !JsonElement.DeepEquals(graph.Edges, JsonSerializer.SerializeToElement(derived.Edges, SyntheticSearchRecordings.Json)))
                throw new InvalidDataException("SEARCH_POINTER_GRAPH_CHANGED");
            CheckMedia(pointer.Media, "pointer-media/", manifest.Files);
        }
        else if (manifest.Files.Any(f => f.Path == "pointer.json" || f.Path == "pointer-graph.json" || f.Path.StartsWith("pointer-media/", StringComparison.Ordinal)))
            throw new InvalidDataException("SEARCH_POINTER_AFTER_INCOMPLETE_INPUT");
        if (manifest.PointerRunId != pointer?.Activation?.RunId
            || manifest.Observation != SyntheticSearchAssessment.Describe(results, pointer, manifest.Source))
            throw new InvalidDataException("SEARCH_OBSERVATION_CHANGED");
        CheckText(manifest.Files, "index.html", SyntheticSearchViewer.Render(manifest.Source, manifest.KeyboardRuns, pointer, manifest.Observation, manifest.CollectionStoppedReason));
        return new(true, trustedManifestSha256, manifest.Files.Length, manifest.Files.Sum(f => f.Bytes));
    }

    private sealed record SearchPointerGraphDocument(JsonElement Nodes, JsonElement Edges, string Schema, string Scope);

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    private static void CheckText(System.Collections.Immutable.ImmutableArray<TraversalPackageFile> files, string name, string expected)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var file = files.SingleOrDefault(f => f.Path == name);
        if (file?.Bytes != bytes.LongLength || file.Sha256 != Worker.TraversalArchive.Hash(bytes))
            throw new InvalidDataException("SEARCH_VIEWER_CHANGED");
    }

    private static void CheckMedia(RecordingProbeMedia? media, string prefix, System.Collections.Immutable.ImmutableArray<TraversalPackageFile> files)
    {
        if (media is null) return;
        if (media.Artifacts.IsDefaultOrEmpty || media.Artifacts.Select(a => a.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != media.Artifacts.Length)
            throw new InvalidDataException("SEARCH_MEDIA_INVENTORY_INVALID");
        foreach (var artifact in media.Artifacts)
        {
            var file = files.SingleOrDefault(f => f.Path == prefix + artifact.File);
            if (file is null || file.Bytes != artifact.Bytes || file.Sha256 != artifact.Sha256 || artifact.DecodedFrames < 1)
                throw new InvalidDataException("SEARCH_MEDIA_ORIGINAL_CHANGED");
        }
    }

    private static async Task<byte[]> ReadAsync(string folder, string name)
    {
        var path = TraversalEvidencePackage.Resolve(folder, name);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > TraversalEvidencePackage.MaximumManifestBytes) throw new InvalidDataException("SEARCH_DOCUMENT_TOO_LARGE");
        var bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes).ConfigureAwait(false);
        if (await input.ReadAsync(new byte[1]).ConfigureAwait(false) != 0) throw new InvalidDataException("SEARCH_DOCUMENT_CHANGED");
        return bytes;
    }

    private static async Task<T> ReadJsonAsync<T>(string folder, string name) where T : class
    {
        var bytes = await ReadAsync(folder, name).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<T>(bytes, SyntheticSearchRecordings.Json)
            ?? throw new InvalidDataException("SEARCH_DOCUMENT_INVALID");
        using var document = JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
        if (!JsonElement.DeepEquals(document.RootElement, JsonSerializer.SerializeToElement(value, SyntheticSearchRecordings.Json)))
            throw new InvalidDataException("SEARCH_DERIVED_FIELD_CHANGED");
        return value;
    }
}
