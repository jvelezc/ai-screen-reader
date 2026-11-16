using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ControlledSearchCollectionTests
{
    [Theory]
    [InlineData("search-working", "AllTestedKeyboardGoalsObserved")]
    [InlineData("search-keyboard-blocked", "KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved")]
    [Trait("Category", "Browser")]
    public async Task ActualControlledCollectionRetainsFiveFreshContextsAndAllEightyFiveControlBoundaries(string source, string observation)
    {
        var start = new SyntheticSearchTaskStart(SyntheticSearchTaskControl.Schema,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId(source)), Guid.NewGuid(), Guid.NewGuid());
        var root = Path.GetFullPath(Path.Combine("artifacts", "controlled-search-collection", Guid.NewGuid().ToString("N")));
        var grants = await SyntheticSearchTaskGrants.BeginAsync(Path.Combine(root, "control"), start);
        var sourceSequence = 0;
        var nativeCalls = 0;
        var modes = 0;
        Task<SyntheticSearchTaskAck> Prepare(int trial, string action, SyntheticSearchInputIntent? input, CancellationToken token)
        {
            var sequence = grants.PreparedResponses + 1;
            var boundary = SyntheticSearchTaskControl.Boundaries[sequence - 1];
            Assert.Equal(trial, boundary.Trial); Assert.Equal(action, boundary.Action);
            return grants.PrepareAsync(new(SyntheticSearchTaskControl.Schema, start, sequence, boundary, input), _ => Task.FromResult(true), token);
        }
        async Task Sources(int trial, string document, long epoch, ImmutableArray<CompletedCaptureSource> originals, CancellationToken token)
        {
            var frames = ImmutableArray.CreateBuilder<FixtureSourceFrame>(); var size = 0;
            async Task Flush()
            {
                if (frames.Count == 0) return;
                await grants.RetainSourcesAsync(new(SyntheticSearchSourceProtocol.Schema, start.ExecutionId, trial, ++sourceSequence, document, epoch, frames.ToImmutable()),
                    _ => Task.FromResult(true), token);
                frames.Clear(); size = 0;
            }
            foreach (var original in originals)
            {
                if (frames.Count == 64 || original.Content.Length > 512 * 1024 - size) await Flush();
                frames.Add(new(original.Frame.Sequence, original.Frame.File, original.Frame.PresentationUnixMilliseconds, original.Frame.Sha256, original.Content));
                size += original.Content.Length;
            }
            await Flush();
        }
        var result = await ControlledSearchRecordings.RecordAsync(Path.Combine(root, "collection"), start,
            async (trial, action, token) => { if (action == "FocusMode") modes++; await Prepare(trial, action, null, token); },
            async (trial, input, token) =>
            {
                nativeCalls++;
                Assert.True(grants.HasSources(trial, input.DocumentId, input.CaptureEpoch));
                return (await Prepare(trial, "Native", input, token)).Decision;
            }, Sources, CancellationToken.None);
        Assert.Equal(observation, result.Observation);
        Assert.Equal(4, result.KeyboardRuns);
        Assert.Equal(77, nativeCalls);
        Assert.Equal(2, modes);
        Assert.Equal(85, grants.PreparedResponses);
        grants.RequireComplete();
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(result.Folder, SyntheticSearchTaskManifest.ManifestFile));
        Assert.Equal(result.ManifestSha256, TraversalArchive.Hash(manifestBytes));
        var manifest = JsonSerializer.Deserialize<SyntheticSearchTaskManifest>(manifestBytes, ReaderWorkerWire.Json)!;
        Assert.Equal(start, manifest.Start);
        Assert.Equal(new[] { "FieldEnter", "FieldEnter", "ButtonEnter", "ButtonSpace" }, manifest.KeyboardTrials.Select(trial => trial.Submission));
        Assert.Equal(new[] { 15, 15, 16, 16 }, manifest.KeyboardTrials.Select(trial => trial.CompletedKeys));
        Assert.Equal(5, manifest.KeyboardTrials.Select(trial => trial.RunId).Append(manifest.PointerRunId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("NotAssessed", manifest.WebsiteEvidenceAssessment);
        Assert.Equal("UnverifiedWithoutCalibrationMarkers", manifest.VideoAlignment);
        Assert.False(manifest.LegalCaseDetermined);
        var retained = grants.ReadRetainedSources();
        Assert.Equal(5, retained.Files.Select(file => file.DocumentId).Distinct(StringComparer.Ordinal).Count());
        Assert.False(retained.HasUncommittedFiles);
        foreach (var original in retained.Files)
        {
            var prefix = original.Trial == 5 ? "pointer-media/" : $"route-{original.Trial}-media/";
            var declared = Assert.Single(manifest.Files, file => file.Path == prefix + original.Image.File);
            Assert.Equal(original.Image.Sha256, declared.Sha256);
            Assert.Equal(original.Image.Bytes, declared.Bytes);
        }
        Assert.Equal(5, manifest.Files.Count(file => file.Path.EndsWith("/original.webm", StringComparison.Ordinal)));
        var archiveBytes = await File.ReadAllBytesAsync(result.Archive);
        Assert.Equal(result.ArchiveSha256, TraversalArchive.Hash(archiveBytes));
        var bundle = new SyntheticSearchTaskBundle(new(SyntheticSearchTaskCollectionReceipt.CurrentSchema, start,
            result.ManifestSha256, result.ArchiveSha256, 85), archiveBytes);
        bundle.Validate(start);
        bundle.MatchRetainedSources(retained);
        var encoded = bundle.Encode(start);
        var decoded = SyntheticSearchTaskBundle.Decode(encoded, start);
        Assert.Equal(archiveBytes, decoded.Originals);
        Assert.Equal(bundle.Receipt, decoded.Receipt);
        // Storage accepts the trusted lifecycle contract; actual container absence is a separate native-runtime gate.
        var localObjects = new FileTraversalObjectStore(Path.Combine(root, "local-objects"));
        var remoteObjects = new FileTraversalObjectStore(Path.Combine(root, "provider-objects"));
        var storage = new SyntheticSearchTaskArchive(localObjects, remoteObjects);
        var prepared = await storage.PrepareAsync(new(start, encoded, true, grants.DirectoryPath), _ => Task.FromResult(true), CancellationToken.None);
        Assert.False(prepared.OriginalsVerified);
        Assert.Equal("SEARCH_TASK_PUBLICATION_INVALID", Assert.Throws<BrowserOperationException>(prepared.Validate).Code);
        var publication = await storage.PublishAsync(start.Job, start.WorkerId, prepared, _ => Task.FromResult(true), CancellationToken.None);
        publication.Validate();
        var restored = await storage.ReadAsync(start.Job, start.WorkerId, publication, CancellationToken.None);
        Assert.Equal(start, restored.Start);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(manifest, ReaderWorkerWire.Json),
            JsonSerializer.SerializeToElement(restored.Manifest, ReaderWorkerWire.Json)));
        Assert.Equal(manifest.Files.Length + 1, restored.Files.Count);
        foreach (var file in manifest.Files)
        {
            Assert.Equal(file.Bytes, restored.Files[file.Path].Length);
            Assert.Equal(file.Sha256, TraversalArchive.Hash(restored.Files[file.Path]));
        }
        Assert.Equal(archiveBytes, restored.Archive);
        await PortalSearchTasksTests.AssertOriginalDeliveryAsync(storage, start, publication, restored.Files);
        Assert.Equal("SEARCH_TASK_ARCHIVE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            storage.ReadAsync(start.Job, Guid.NewGuid(), publication, CancellationToken.None))).Code);
        Assert.Equal("SEARCH_TASK_AUTHORITY_LOST", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            storage.PublishAsync(start.Job, start.WorkerId, prepared, _ => Task.FromResult(false), CancellationToken.None))).Code);
        Assert.Equal(77, nativeCalls);
        foreach (var mutation in new[] { "legal-claim", "outcome", "entry-value", "field-plan", "graph-keys", "original-video", "pointer-clock" })
        {
            var changed = Change(bundle, mutation);
            Assert.Equal("SEARCH_TASK_BUNDLE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => changed.Validate(start)).Code);
        }
        Assert.Equal("SEARCH_TASK_BUNDLE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => bundle.MatchRetainedSources(retained with { HasUncommittedFiles = true })).Code);
        var first = retained.Files[0];
        var changedClock = retained with { Files = retained.Files.SetItem(0, first with { Image = first.Image with { PresentationUnixMilliseconds = first.Image.PresentationUnixMilliseconds + 1 } }) };
        Assert.Equal("SEARCH_TASK_BUNDLE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => bundle.MatchRetainedSources(changedClock)).Code);
        using var archive = ZipFile.OpenRead(result.Archive);
        Assert.Equal(manifest.Files.Length + 1, archive.Entries.Count);
        foreach (var original in manifest.Files)
        {
            var entry = Assert.Single(archive.Entries, entry => entry.FullName == original.Path);
            Assert.Equal(original.Bytes, entry.Length);
            await using var input = entry.Open();
            using var copy = new MemoryStream(); await input.CopyToAsync(copy);
            Assert.Equal(original.Sha256, TraversalArchive.Hash(copy.ToArray()));
        }
    }

    private static SyntheticSearchTaskBundle Change(SyntheticSearchTaskBundle original, string mutation)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var input = new MemoryStream(original.Originals))
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open(); using var copy = new MemoryStream(); stream.CopyTo(copy); files.Add(entry.FullName, copy.ToArray());
            }
        var manifest = JsonNode.Parse(files[SyntheticSearchTaskManifest.ManifestFile])!;
        void Save(string path, JsonNode node) => files[path] = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
        if (mutation == "legal-claim") manifest["LegalCaseDetermined"] = true;
        else if (mutation == "outcome") manifest["Observation"] = manifest["Observation"]!.GetValue<string>() == "AllTestedKeyboardGoalsObserved"
            ? "KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved" : "AllTestedKeyboardGoalsObserved";
        else if (mutation == "pointer-clock")
        {
            var pointer = JsonNode.Parse(files["pointer.json"])!; pointer["Preparation"]!["ReaderClock"] = "InventedVideoAlignment"; Save("pointer.json", pointer);
        }
        else if (mutation == "graph-keys")
        {
            var graph = JsonNode.Parse(files["route-1.json"])!;
            graph["Graph"]!["Edges"]![1]!["KeyboardInputs"]!.AsArray().RemoveAt(0); Save("route-1.json", graph);
        }
        else
        {
            var recording = JsonNode.Parse(files["route-1-recording.json"])!;
            var projection = JsonNode.Parse(files["route-1.json"])!;
            if (mutation == "entry-value") recording["Receipt"]!["Attempts"]![1]!["Step"]!["SearchInput"]!["ValueVerified"] = false;
            else if (mutation == "field-plan") recording["Receipt"]!["Plan"]!["Instructions"]![2]!["Command"] = "Activate";
            else
            {
                var video = files["route-1-media/original.webm"].ToArray(); video[0] ^= 1; files["route-1-media/original.webm"] = video;
                var digest = TraversalArchive.Hash(video);
                recording["Recording"]!["Alignment"]!["VideoSha256"] = digest;
                var declaredVideo = recording["Recording"]!["Media"]!["Artifacts"]!.AsArray().Single(item => item!["File"]!.GetValue<string>() == "original.webm")!;
                declaredVideo["Sha256"] = digest;
            }
            projection["Receipt"] = recording["Receipt"]!.DeepClone(); projection["Recording"] = recording["Recording"]!.DeepClone();
            Save("route-1-recording.json", recording); Save("route-1.json", projection);
        }
        foreach (var file in manifest["Files"]!.AsArray())
        {
            var bytes = files[file!["Path"]!.GetValue<string>()]; file["Bytes"] = bytes.Length; file["Sha256"] = TraversalArchive.Hash(bytes);
        }
        Save(SyntheticSearchTaskManifest.ManifestFile, manifest);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var pair in files) { using var entry = zip.CreateEntry(pair.Key, CompressionLevel.NoCompression).Open(); entry.Write(pair.Value); }
        var archive = output.ToArray();
        return new(original.Receipt with { ManifestSha256 = TraversalArchive.Hash(files[SyntheticSearchTaskManifest.ManifestFile]), ArchiveSha256 = TraversalArchive.Hash(archive) }, archive);
    }
}
