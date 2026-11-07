using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed partial class MockTaskWorkerTests
{
    private static readonly Lazy<Task<(MockTaskStart Start, MockTaskCollectionBundle Bundle)>> Original = new(async () =>
    {
        var result = await Child("checkout-working");
        return (result.Start, MockTaskCollectionBundle.Decode(result.Output, result.Start));
    });

    private static Dictionary<string, byte[]> Unzip(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var input = e.Open(); using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
    }
    private static byte[] Zip(Dictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var item in files) { using var target = zip.CreateEntry(item.Key, CompressionLevel.NoCompression).Open(); target.Write(item.Value); }
        return output.ToArray();
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("legal")]
    [InlineData("at")]
    [InlineData("timing")]
    [InlineData("source")]
    [InlineData("goal")]
    [InlineData("fixture")]
    [InlineData("extra-claim")]
    [InlineData("classification")]
    [InlineData("readonly-count")]
    [InlineData("graph")]
    [InlineData("pointer-mechanism")]
    [InlineData("pointer-document")]
    [InlineData("media-hash")]
    [InlineData("media-count")]
    [InlineData("changed-video")]
    [InlineData("pending")]
    [InlineData("case-rename")]
    [InlineData("invented-calibration")]
    [InlineData("foreign-alignment")]
    public async Task ParentRejectsResealedInventedScopeChangedOriginalsAndInconsistentExperimentRecords(string mutation)
    {
        var (start, original) = await Original.Value; original.Validate(start);
        var files = Unzip(original.Originals); var manifest = JsonNode.Parse(files["mock-task-manifest.json"])!.AsObject();
        var row = JsonNode.Parse(files["route-1.json"])!.AsObject(); var pointer = JsonNode.Parse(files["pointer.json"])!.AsObject();
        switch (mutation)
        {
            case "scope": manifest["Scope"] = "PublicWebsite"; break;
            case "legal": manifest["LegalCaseDetermined"] = true; break;
            case "at": manifest["ExternalAssistiveTechnologyComparison"] = "NVDAConfirmed"; break;
            case "timing": manifest["VideoAlignment"] = "Verified"; break;
            case "source": manifest["Source"]!["SourceSha256"] = new string('a', 64); break;
            case "goal": manifest["Source"]!["GoalName"] = "Invented goal"; break;
            case "fixture": manifest["Source"]!["Fixture"] = "Foundation"; break;
            case "extra-claim": manifest["HostedTaskAdmitted"] = true; break;
            case "classification": manifest["Observation"] = "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved"; break;
            case "readonly-count": row["Receipt"]!["CompletedKeystrokes"] = 999; break;
            case "graph": row["Graph"]!["Edges"]![0]!["SourceObservationId"] = "missing"; break;
            case "pointer-mechanism": pointer["Receipt"]!["Mechanism"] = "Keyboard"; break;
            case "pointer-document": pointer["Receipt"]!["Before"]!["DocumentId"] = row["Receipt"]!["Observations"]![0]!["Snapshot"]!["DocumentId"]!.GetValue<string>(); break;
            case "media-hash": row["Recording"]!["Media"]!["Artifacts"]![0]!["Sha256"] = new string('a', 64); break;
            case "media-count": row["Recording"]!["Media"]!["Video"] = new JsonArray(); break;
            case "invented-calibration": row["Recording"]!["Alignment"]!["Calibrated"] = true; break;
            case "foreign-alignment": row["Recording"]!["Alignment"]!["RunId"] = Guid.NewGuid().ToString("N"); break;
            case "changed-video": files["route-1-media/original.webm"][0] ^= 1; break;
            case "pending": files["unfinished.pending"] = [1]; break;
            case "case-rename": files["ROUTE-1.json"] = files["route-1.json"]; files.Remove("route-1.json"); break;
        }
        if (mutation is "readonly-count" or "graph" or "media-hash" or "media-count" or "invented-calibration" or "foreign-alignment") files["route-1.json"] = JsonSerializer.SerializeToUtf8Bytes(row);
        if (mutation is "pointer-mechanism" or "pointer-document") files["pointer.json"] = JsonSerializer.SerializeToUtf8Bytes(pointer);
        // Recompute inventory hashes deliberately: the parent must verify meaning as well as byte correspondence.
        foreach (var item in manifest["Files"]!.AsArray())
        {
            if (!files.TryGetValue(item!["Path"]!.GetValue<string>(), out var bytes)) continue;
            item["Bytes"] = bytes.Length; item["Sha256"] = TraversalArchive.Hash(bytes);
        }
        files["mock-task-manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var archive = Zip(files);
        var changed = new MockTaskCollectionBundle(original.Receipt with { ManifestSha256 = TraversalArchive.Hash(files["mock-task-manifest.json"]), ArchiveSha256 = TraversalArchive.Hash(archive) }, archive);
        Assert.Equal("MOCK_TASK_BUNDLE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => changed.Validate(start)).Code);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("a\\b")]
    [InlineData("a//b")]
    public void ArchivePathsCannotEscapeOrIntroduceAmbiguousNames(string path)
    {
        var bytes = Zip(new() { [path] = [1] });
        Assert.Throws<BrowserOperationException>(() => MockTaskCollectionBundle.Decode(bytes, Start()));
    }

    [Fact]
    public void CompressedOversizedEntryIsRejectedBeforeItsPayloadCanBeAllocated()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using var target = zip.CreateEntry("originals.zip", CompressionLevel.SmallestSize).Open();
            var buffer = new byte[8192];
            for (var bytes = 0; bytes <= MockTaskCollectionBundle.MaximumBundleBytes; bytes += buffer.Length) target.Write(buffer);
        }
        Assert.True(output.Length < 64 * 1024);
        Assert.Throws<BrowserOperationException>(() => MockTaskCollectionBundle.Decode(output.ToArray(), Start()));
    }

    [Fact]
    public async Task DeniedAuthorityOrInvalidJobCannotAllocateANativeIntentOrInputFence()
    {
        var root = Folder(); var journal = new OwnershipJournal(root);
        var executor = new IsolatedFixtureExecutor("podman", "sha256:" + new string('a', 64), journal: journal);
        var job = Start().Job;
        Assert.Equal("MOCK_TASK_AUTHORITY_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => executor.ExecuteMockTaskAsync(job, Guid.NewGuid(), _ => Task.FromResult(false)))).Code);
        Assert.Empty(journal.ReadPending()); Assert.False(Directory.Exists(Path.Combine(root, "mock-task-control")));
        var calls = 0;
        await Assert.ThrowsAsync<BrowserOperationException>(() => executor.ExecuteMockTaskAsync(job with { Generation = 2 }, Guid.NewGuid(), _ => { calls++; return Task.FromResult(true); }));
        Assert.Equal(0, calls); Assert.Empty(journal.ReadPending());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteMockTaskAsync(job, Guid.NewGuid(), _ => { calls++; return Task.FromResult(true); }, cancelled.Token));
        Assert.Equal(0, calls); Assert.Empty(journal.ReadPending()); Assert.False(Directory.Exists(Path.Combine(root, "mock-task-control")));
    }

    [Fact]
    public async Task EarlierOriginalsCannotBeRelabeledByChangingOnlyTheTransportExecution()
    {
        var (start, original) = await Original.Value; original.Validate(start);
        var later = start with { ExecutionId = Guid.NewGuid(), WorkerId = Guid.NewGuid() };
        var relabeled = original with { Receipt = original.Receipt with { Start = later } };
        Assert.Equal("MOCK_TASK_BUNDLE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => relabeled.Validate(later)).Code);
        var anotherJob = start with { Job = start.Job with { JobId = Guid.NewGuid() } };
        Assert.Throws<BrowserOperationException>(() => (original with { Receipt = original.Receipt with { Start = anotherJob } }).Validate(anotherJob));
    }
}
