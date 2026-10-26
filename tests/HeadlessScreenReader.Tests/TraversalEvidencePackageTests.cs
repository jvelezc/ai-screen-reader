using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "FileSystem")]
public sealed class TraversalEvidencePackageTests
{
    private static readonly DateTimeOffset CollectedAt = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Export_PreservesOriginalBytesGraphsAndHonestScopeInPortableArchive()
    {
        var (folder, result) = await Inputs();
        var original = await File.ReadAllBytesAsync(Path.Combine(folder, "route-1-media", "original.webm"));
        var export = await TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt);
        var manifest = await Manifest(folder);
        Assert.Equal("hsr-recorded-traversal-package-v1", manifest.Schema);
        Assert.Equal(CollectedAt, manifest.CreatedAtUtc);
        Assert.Equal("ControlledSyntheticFixture", manifest.Scope);
        Assert.Equal("HeadlessScreenReaderAuthoredCommands", manifest.Operator);
        Assert.Equal("NotPerformedForThisPackage", manifest.NvdaComparison);
        Assert.Equal("NotPerformedForThisPackage", manifest.HumanValidation);
        Assert.Equal(4, manifest.PlannedRoutes);
        var route = Assert.Single(manifest.Routes);
        Assert.Equal(result.Receipt!.RunId, route.RunId);
        Assert.Equal("Unknown", route.Reachability);
        Assert.Equal("NoneObserved", route.Barrier);
        Assert.True(route.HasRecording);
        Assert.True(route.TimingCalibrated);
        Assert.Equal("route-1.json", route.EvidenceFile);
        Assert.Equal(BundledFixtureCatalog.Describe(BundledFixture.TraversalRecording).ContentSha256, manifest.FixtureSha256);
        Assert.Equal(BundledFixtureCatalog.ReadHtml(BundledFixture.TraversalRecording), await File.ReadAllTextAsync(Path.Combine(folder, manifest.FixtureSource)));
        Assert.Equal(Hash(await File.ReadAllBytesAsync(typeof(TraversalEvidencePackage).Assembly.Location)), manifest.ProducerSha256);
        Assert.Equal(Hash(await File.ReadAllBytesAsync(Path.Combine(folder, TraversalEvidencePackage.ManifestFile))), export.ManifestSha256);
        Assert.Equal(Hash(await File.ReadAllBytesAsync(export.Archive)), export.ArchiveSha256);
        Assert.Contains(manifest.Files, f => f.Path == "route-1.svg");
        Assert.Contains(manifest.Files, f => f.Path == "route-1.html");
        Assert.Contains(manifest.Files, f => f.Path == "route-1.json");
        Assert.Equal(manifest.Files.Sum(f => f.Bytes), export.Bytes);
        Assert.Equal(manifest.Files.Length, export.Files);
        using var archive = ZipFile.OpenRead(export.Archive);
        Assert.Equal(manifest.Files.Length + 1, archive.Entries.Count);
        foreach (var file in manifest.Files)
        {
            var entry = Assert.Single(archive.Entries, e => e.FullName == file.Path);
            await using var stream = entry.Open();
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes);
            Assert.Equal(file.Bytes, bytes.Length);
            Assert.Equal(file.Sha256, Hash(bytes.ToArray()));
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(folder, "route-1-media", "original.webm")));
        var extracted = folder + "-extracted";
        ZipFile.ExtractToDirectory(export.Archive, extracted);
        var verified = await TraversalEvidencePackage.VerifyAsync(extracted, export.ManifestSha256.ToUpperInvariant());
        Assert.True(verified.IntegrityMatches);
        Assert.Equal(export.Files, verified.Files);
        Assert.Equal(export.Bytes, verified.Bytes);
    }

    [Theory]
    [InlineData("changed-media", "EVIDENCE_ARTIFACT_CHANGED")]
    [InlineData("changed-graph", "EVIDENCE_ARTIFACT_CHANGED")]
    [InlineData("missing", "EVIDENCE_FILE_SET_CHANGED")]
    [InlineData("extra", "EVIDENCE_FILE_SET_CHANGED")]
    [InlineData("manifest", "EVIDENCE_MANIFEST_CHANGED")]
    public async Task Verify_DetectsTamperingAgainstSeparatelyRetainedDigest(string scenario, string code)
    {
        var (folder, result) = await Inputs();
        var export = await TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt);
        switch (scenario)
        {
            case "changed-media": await File.WriteAllBytesAsync(Path.Combine(folder, "route-1-media", "original.webm"), [3, 2, 1]); break;
            case "changed-graph": await File.AppendAllTextAsync(Path.Combine(folder, "route-1.svg"), "changed"); break;
            case "missing": File.Delete(Path.Combine(folder, "route-1.html")); break;
            case "extra": await File.WriteAllTextAsync(Path.Combine(folder, "unlisted.txt"), "unlisted"); break;
            case "manifest": await File.AppendAllTextAsync(Path.Combine(folder, TraversalEvidencePackage.ManifestFile), " "); break;
        }
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TraversalEvidencePackage.VerifyAsync(folder, export.ManifestSha256));
        Assert.Equal(code, error.Message);
        using var output = new StringWriter();
        Assert.Equal(3, await TraversalEvidencePackage.VerifyCommandAsync(folder, export.ManifestSha256, output));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("IntegrityMatches").GetBoolean());
        Assert.Equal(code, json.RootElement.GetProperty("ErrorCode").GetString());
        Assert.DoesNotContain(folder, output.ToString());
    }

    [Theory]
    [InlineData("traversal")]
    [InlineData("absolute")]
    [InlineData("backslash")]
    [InlineData("drive")]
    [InlineData("empty-segment")]
    [InlineData("dot")]
    [InlineData("duplicate")]
    [InlineData("self")]
    [InlineData("null-file")]
    [InlineData("device")]
    public async Task Verify_RejectsUnsafeOrAmbiguousInventoryEvenWithItsMatchingDigest(string scenario)
    {
        var (folder, result) = await Inputs();
        await TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt);
        var manifest = await Manifest(folder);
        var replacement = manifest.Files[0] with
        {
            Path = scenario switch
            {
                "traversal" => "../outside.txt",
                "absolute" => "/outside.txt",
                "backslash" => "route-1-media\\original.webm",
                "drive" => "C:outside.txt",
                "empty-segment" => "route-1-media//original.webm",
                "dot" => "./index.md",
                "duplicate" => manifest.Files[1].Path.ToUpperInvariant(),
                "self" => TraversalEvidencePackage.ManifestFile,
                "device" => "con.txt",
                _ => manifest.Files[0].Path
            }
        };
        var files = manifest.Files.SetItem(0, scenario == "null-file" ? null! : replacement);
        var digest = await ReplaceManifest(folder, manifest with { Files = files });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TraversalEvidencePackage.VerifyAsync(folder, digest));
        Assert.Equal("EVIDENCE_MANIFEST_INVALID", error.Message);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("scope")]
    [InlineData("empty-files")]
    [InlineData("empty-routes")]
    [InlineData("route-number")]
    [InlineData("route-file")]
    [InlineData("file-size")]
    [InlineData("total-size")]
    [InlineData("fixture-hash")]
    [InlineData("file-count")]
    public async Task Verify_RejectsInvalidSchemaScopeReferencesAndResourceDeclarations(string scenario)
    {
        var (folder, result) = await Inputs();
        await TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt);
        var manifest = await Manifest(folder);
        var changed = scenario switch
        {
            "schema" => manifest with { Schema = "unknown" },
            "scope" => manifest with { HumanValidation = "Passed" },
            "empty-files" => manifest with { Files = [] },
            "empty-routes" => manifest with { Routes = [] },
            "route-number" => manifest with { Routes = [manifest.Routes[0] with { Number = 4 }] },
            "route-file" => manifest with { Routes = [manifest.Routes[0] with { EvidenceFile = "absent.json" }] },
            "file-size" => manifest with { Files = manifest.Files.SetItem(0, manifest.Files[0] with { Bytes = TraversalEvidencePackage.MaximumFileBytes + 1 }) },
            "total-size" => manifest with { Files = [.. manifest.Files.Select(f => f with { Bytes = TraversalEvidencePackage.MaximumFileBytes })] },
            "file-count" => manifest with { Files = [.. Enumerable.Repeat(manifest.Files[0], TraversalEvidencePackage.MaximumFiles + 1)] },
            _ => manifest with { FixtureSha256 = new string('a', 64) }
        };
        var digest = await ReplaceManifest(folder, changed);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TraversalEvidencePackage.VerifyAsync(folder, digest));
        Assert.Equal(scenario == "total-size" ? "EVIDENCE_LIMIT_EXCEEDED" : "EVIDENCE_MANIFEST_INVALID", error.Message);
    }

    [Fact]
    public async Task Export_RetainsFailedRecordingAndPartialCoverageWithoutCreatingSuccessClaims()
    {
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.md"), "Interrupted synthetic collection");
        var result = new RecordedTraversalResult(null, new("Launch", "TRAVERSAL_RECORDING_CANCELLED", null, [], null, null), null);
        await File.WriteAllTextAsync(Path.Combine(folder, "route-1-failure.json"), JsonSerializer.Serialize(result, TraversalDemo.JsonOptions));
        var export = await TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt);
        var manifest = await Manifest(folder);
        Assert.Equal(4, manifest.PlannedRoutes);
        var route = Assert.Single(manifest.Routes);
        Assert.Equal("NotStarted", route.Execution);
        Assert.Equal("Unknown", route.Reachability);
        Assert.Equal("NotAssessed", route.Barrier);
        Assert.Equal("TRAVERSAL_RECORDING_CANCELLED", route.RecordingError);
        Assert.False(route.HasRecording);
        Assert.False(route.TimingCalibrated);
        Assert.DoesNotContain(manifest.Files, f => f.Path.EndsWith(".webm", StringComparison.Ordinal));
        Assert.True((await TraversalEvidencePackage.VerifyAsync(folder, export.ManifestSha256)).IntegrityMatches);
    }

    [Theory]
    [InlineData("transcript", "EVIDENCE_ROUTE_CHANGED")]
    [InlineData("original", "EVIDENCE_ORIGINAL_CHANGED")]
    [InlineData("pending", "EVIDENCE_PENDING_OUTPUT")]
    [InlineData("existing-archive", "EVIDENCE_PACKAGE_ALREADY_EXISTS")]
    [InlineData("fixture", "EVIDENCE_FIXTURE_MISMATCH")]
    public async Task Export_RefusesChangedSourcesOrUnfinishedAndExistingOutputs(string scenario, string code)
    {
        var (folder, result) = await Inputs();
        switch (scenario)
        {
            case "transcript":
                var path = Path.Combine(folder, "route-1.json");
                var json = await File.ReadAllTextAsync(path);
                await File.WriteAllTextAsync(path, json.Replace(result.Receipt!.RunId, "foreign-run", StringComparison.Ordinal));
                break;
            case "original": await File.WriteAllBytesAsync(Path.Combine(folder, "route-1-media", "original.webm"), [3, 2, 1]); break;
            case "pending": await File.WriteAllTextAsync(Path.Combine(folder, "route-2.json.pending"), "unfinished"); break;
            case "existing-archive": await File.WriteAllTextAsync(folder + ".zip", "existing"); break;
            case "fixture":
                result = result with { Environment = result.Environment! with { Content = null } };
                var routePath = Path.Combine(folder, "route-1.json");
                var routeJson = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(routePath))!;
                routeJson["Environment"] = JsonSerializer.SerializeToNode(result.Environment, TraversalDemo.JsonOptions);
                await File.WriteAllTextAsync(routePath, routeJson.ToJsonString(TraversalDemo.JsonOptions));
                break;
        }
        var error = await Assert.ThrowsAnyAsync<Exception>(() => TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt));
        if (scenario == "existing-archive") Assert.IsType<IOException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.Equal(code, error.Message);
        Assert.False(File.Exists(Path.Combine(folder, TraversalEvidencePackage.ManifestFile)));
        if (scenario == "existing-archive") Assert.Equal("existing", await File.ReadAllTextAsync(folder + ".zip"));
        else Assert.False(File.Exists(folder + ".zip"));
    }

    [Fact]
    public async Task Export_IsAppendOnlyAndVerifierRequiresAnExternalValidDigest()
    {
        var (folder, result) = await Inputs();
        var export = await TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt);
        var original = await File.ReadAllBytesAsync(export.Archive);
        var error = await Assert.ThrowsAsync<IOException>(() => TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt));
        Assert.Equal("EVIDENCE_PACKAGE_ALREADY_EXISTS", error.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(export.Archive));
        using var output = new StringWriter();
        Assert.Equal(3, await TraversalEvidencePackage.VerifyCommandAsync(folder, "", output));
        Assert.Contains("EVIDENCE_DIGEST_INVALID", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await TraversalEvidencePackage.VerifyCommandAsync(folder, export.ManifestSha256, output));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("IntegrityMatches").GetBoolean());
        Assert.Equal(export.ManifestSha256, json.RootElement.GetProperty("ManifestSha256").GetString());
    }

    [Fact]
    public async Task Export_StreamsMultipleBlocksAndRetainsEmptyArtifactsWithoutChangingTheirBytes()
    {
        var bytes = Enumerable.Range(0, 131077).Select(i => (byte)(i % 251)).ToArray();
        var (folder, result) = await Inputs(bytes);
        await File.WriteAllBytesAsync(Path.Combine(folder, "route-1-media", "empty.jpg"), []);
        var export = await TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt);
        var manifest = await Manifest(folder);
        var video = Assert.Single(manifest.Files, f => f.Path == "route-1-media/original.webm");
        Assert.Equal(131077, video.Bytes);
        Assert.Equal(Hash(bytes), video.Sha256);
        var empty = Assert.Single(manifest.Files, f => f.Path == "route-1-media/empty.jpg");
        Assert.Equal(0, empty.Bytes);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", empty.Sha256);
        using var archive = ZipFile.OpenRead(export.Archive);
        using var contents = new MemoryStream();
        await using var originalEntry = archive.GetEntry(video.Path)!.Open();
        await originalEntry.CopyToAsync(contents);
        Assert.Equal(bytes, contents.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Export_RejectsBatchesOutsideTheFourPlannedRoutesBeforeCreatingFiles(int count)
    {
        var folder = NewFolder();
        var result = new RecordedTraversalResult(null, new("Launch", "CANCELLED", null, [], null, null), null);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TraversalEvidencePackage.CreateAsync(folder,
            Enumerable.Repeat(result, count).ToArray(), CollectedAt));
        Assert.Equal("EVIDENCE_ROUTES_INVALID", error.Message);
        Assert.False(Directory.Exists(folder));
        Assert.False(File.Exists(folder + ".zip"));
    }

    [Fact]
    public async Task Verify_RejectsOversizedManifestBeforeReadingItsContents()
    {
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        await using (var stream = File.Create(Path.Combine(folder, TraversalEvidencePackage.ManifestFile)))
            stream.SetLength(TraversalEvidencePackage.MaximumManifestBytes + 1);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TraversalEvidencePackage.VerifyAsync(folder, new string('a', 64)));
        Assert.Equal("EVIDENCE_MANIFEST_INVALID", error.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Verify_AcceptsValidManifestImmediatelyBelowAndAtTheByteLimit(int adjustment)
    {
        var (folder, result) = await Inputs();
        await TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt);
        var path = Path.Combine(folder, TraversalEvidencePackage.ManifestFile);
        var original = await File.ReadAllBytesAsync(path);
        var padded = Enumerable.Repeat((byte)' ', TraversalEvidencePackage.MaximumManifestBytes + adjustment).ToArray();
        original.CopyTo(padded, 0);
        await File.WriteAllBytesAsync(path, padded);
        var verified = await TraversalEvidencePackage.VerifyAsync(folder, Hash(padded));
        Assert.True(verified.IntegrityMatches);
        Assert.Equal(Hash(padded), verified.ManifestSha256);
    }

    [Fact]
    public async Task Export_RejectsOversizedOriginalBeforeReadingOrPublishingManifest()
    {
        var (folder, result) = await Inputs();
        await using (var stream = File.OpenWrite(Path.Combine(folder, "route-1-media", "original.webm")))
            stream.SetLength(TraversalEvidencePackage.MaximumFileBytes + 1);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt));
        Assert.Equal("EVIDENCE_LIMIT_EXCEEDED", error.Message);
        Assert.False(File.Exists(Path.Combine(folder, TraversalEvidencePackage.ManifestFile)));
        Assert.False(File.Exists(folder + ".zip"));
        // The deliberately oversized sparse file is not a useful CI diagnostic artifact.
        await File.WriteAllBytesAsync(Path.Combine(folder, "route-1-media", "original.webm"), [1, 2, 3]);
    }

    [Fact]
    public async Task VerifyCommand_UnreadablePackageReturnsFailureWithoutExposingLocalPaths()
    {
        var folder = NewFolder();
        using var output = new StringWriter();
        Assert.Equal(3, await TraversalEvidencePackage.VerifyCommandAsync(folder, new string('a', 64), output));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("IntegrityMatches").GetBoolean());
        Assert.Equal("EVIDENCE_PACKAGE_UNREADABLE", json.RootElement.GetProperty("ErrorCode").GetString());
        Assert.DoesNotContain(folder, output.ToString());
    }

    [Fact]
    public async Task Export_RejectsDirectoryLinksInsteadOfIncludingOutsideFiles()
    {
        var (folder, result) = await Inputs();
        var outside = NewFolder();
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "outside.txt"), "must not be exported");
        var link = Path.Combine(folder, "linked");
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "/c", "mklink", "/J", link, outside }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => TraversalEvidencePackage.CreateAsync(folder, [result], CollectedAt));
        Assert.Equal("EVIDENCE_LINK_NOT_ALLOWED", error.Message);
        Assert.Equal("must not be exported", await File.ReadAllTextAsync(Path.Combine(outside, "outside.txt")));
        Assert.False(File.Exists(folder + ".zip"));
        Directory.Delete(link);
    }

    private static async Task<(string Folder, RecordedTraversalResult Result)> Inputs(byte[]? originalBytes = null)
    {
        var receipt = await TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.ReadCurrent)));
        var folder = NewFolder();
        Directory.CreateDirectory(Path.Combine(folder, "route-1-media"));
        byte[] original = originalBytes ?? [1, 2, 3]; // Integrity only; real decoded video is tested in ChromiumTraversalRecordingTests.
        await File.WriteAllBytesAsync(Path.Combine(folder, "route-1-media", "original.webm"), original);
        var recording = new TraversalRecordingEvidence("Complete", null,
            new(receipt.RunId, "doc", Hash(original), "TRAVERSAL_RECORDING_CALIBRATED", 1000, 41, []), [],
            new([], [], [new("original.webm", original.Length, Hash(original), 1)], []), null);
        var environment = new FixtureBrowserEnvironment("integrity-test-only", "integrity-test-only", false,
            BundledFixtureCatalog.Describe(BundledFixture.TraversalRecording));
        var result = new RecordedTraversalResult(receipt with { HasRecording = true }, recording, environment);
        await TraversalArtifactWriter.WriteAsync(folder, 1, result.Receipt!, environment, recording);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.md"), "Synthetic test batch");
        return (folder, result);
    }

    private static string NewFolder() => Path.GetFullPath(Path.Combine("artifacts", "evidence-package-tests", Guid.NewGuid().ToString("N")));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task<TraversalPackageManifest> Manifest(string folder) =>
        JsonSerializer.Deserialize<TraversalPackageManifest>(await File.ReadAllBytesAsync(Path.Combine(folder, TraversalEvidencePackage.ManifestFile)), TraversalDemo.JsonOptions)!;
    private static async Task<string> ReplaceManifest(string folder, TraversalPackageManifest manifest)
    {
        var path = Path.Combine(folder, TraversalEvidencePackage.ManifestFile);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, TraversalDemo.JsonOptions));
        return Hash(await File.ReadAllBytesAsync(path));
    }
}
