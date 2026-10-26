using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalArchiveCommandsTests
{
    private static async Task<TraversalPackageExport> Package()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "archive-import-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.md"), "Interrupted synthetic collection");
        var result = new RecordedTraversalResult(null, new("Launch", "TRAVERSAL_RECORDING_CANCELLED", null, [], null, null), null);
        await File.WriteAllTextAsync(Path.Combine(folder, "route-1-failure.json"), JsonSerializer.Serialize(result, TraversalDemo.JsonOptions));
        return await TraversalEvidencePackage.CreateAsync(folder, [result], new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Import_VerifiedPackageRetainsOriginalManifestAndHonestCancelledRouteFacts()
    {
        var package = await Package(); var objects = new TraversalArchiveTests.Objects();
        var receipt = await TraversalArchiveCommands.ImportAsync(package.Folder, package.ManifestSha256, Guid.NewGuid(), Guid.NewGuid(), objects);
        Assert.Equal(package.Files + 1, receipt.Files); Assert.Equal(package.ManifestSha256, receipt.SourceManifestSha256);
        var archive = new TraversalArchive(objects);
        var original = await File.ReadAllBytesAsync(Path.Combine(package.Folder, TraversalEvidencePackage.ManifestFile));
        var restored = await archive.ReadFileAsync(receipt.Identity, TraversalEvidencePackage.ManifestFile, 0);
        Assert.Equal(original, restored.Data); Assert.Null(restored.NextOffset);
        var manifest = JsonSerializer.Deserialize<TraversalPackageManifest>(restored.Data)!;
        var route = Assert.Single(manifest.Routes);
        Assert.Equal("NotStarted", route.Execution); Assert.Equal("Unknown", route.Reachability);
        Assert.Equal("TRAVERSAL_RECORDING_CANCELLED", route.RecordingError); Assert.False(route.HasRecording);
        Assert.Equal("NotPerformedForThisPackage", manifest.HumanValidation);
        Assert.True((await archive.VerifyAsync(receipt.Identity)).IntegrityMatches);
    }

    [Theory]
    [InlineData("wrong-retained-digest")]
    [InlineData("changed-original")]
    [InlineData("extra-original")]
    public async Task Import_RejectsUnverifiedSourceBeforeAnyArchiveObjectIsWritten(string scenario)
    {
        var package = await Package(); var objects = new TraversalArchiveTests.Objects(); var digest = package.ManifestSha256;
        if (scenario == "wrong-retained-digest") digest = new('a', 64);
        if (scenario == "changed-original") await File.AppendAllTextAsync(Path.Combine(package.Folder, "route-1-failure.json"), "changed");
        if (scenario == "extra-original") await File.WriteAllTextAsync(Path.Combine(package.Folder, "unlisted.txt"), "unexpected");
        await Assert.ThrowsAsync<InvalidDataException>(() => TraversalArchiveCommands.ImportAsync(package.Folder, digest, Guid.NewGuid(), Guid.NewGuid(), objects));
        Assert.Empty(objects.Writes); Assert.Empty(objects.Values);
    }

    [Fact]
    public async Task Command_LocalImportAndFreshVerificationReturnExactScopedReceipts()
    {
        var package = await Package(); var root = package.Folder + "-store";
        var organization = Guid.NewGuid(); var archive = Guid.NewGuid(); using var output = new StringWriter();
        Assert.Equal(0, await TraversalArchiveCommands.RunAsync(["--archive-evidence-local", package.Folder, package.ManifestSha256,
            organization.ToString("D"), archive.ToString("D"), root], output));
        var receipt = JsonSerializer.Deserialize<TraversalArchiveReceipt>(output.ToString())!;
        Assert.Equal(organization, receipt.Identity.OrganizationId); Assert.Equal(archive, receipt.Identity.ArchiveId);
        using var verified = new StringWriter();
        Assert.Equal(0, await TraversalArchiveCommands.RunAsync(["--verify-archive-local", root, organization.ToString("D"),
            archive.ToString("D"), receipt.Identity.IndexSha256], verified));
        var proof = JsonSerializer.Deserialize<TraversalArchiveVerification>(verified.ToString())!;
        Assert.True(proof.IntegrityMatches); Assert.Equal(receipt.Files, proof.Files); Assert.Equal(receipt.Bytes, proof.Bytes);
    }

    [Fact]
    public async Task Command_InvalidArgumentsDoNotDiscloseLocalPathsOrCredentials()
    {
        foreach (var args in new[] { Array.Empty<string>(), new[] { "--unknown" }, new[] { "--verify-archive-local", "private-path-canary" } })
        {
            using var output = new StringWriter();
            Assert.Equal(2, await TraversalArchiveCommands.RunAsync(args, output));
            Assert.Equal("TRAVERSAL_ARCHIVE_INPUT_INVALID_OR_UNREADABLE" + Environment.NewLine, output.ToString());
        }
    }
}
