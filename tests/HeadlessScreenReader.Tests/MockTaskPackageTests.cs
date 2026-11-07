using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed partial class MockTaskRecordingTests
{
    [Theory]
    [InlineData("wrong-digest")]
    [InlineData("extra-file")]
    [InlineData("pending-write")]
    [InlineData("legal-case")]
    [InlineData("public-scope")]
    [InlineData("invented-synchronization")]
    [InlineData("invented-at-comparison")]
    [InlineData("changed-source")]
    [InlineData("changed-key")]
    [Trait("Category", "Browser")]
    public async Task MockPackageVerifierRejectsChangedInventorySourceScopeAndInventedClaims(string change)
    {
        var original = await Recorded.Value; var folder = NewFolder(); Directory.CreateDirectory(folder);
        foreach (var file in await TraversalEvidencePackage.InventoryAsync(original.Folder))
        {
            var target = Path.Combine(folder, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(original.Folder, file.Path), target);
        }
        var manifestPath = Path.Combine(folder, MockTaskRecordings.ManifestFile);
        var bytes = await File.ReadAllBytesAsync(manifestPath); var digest = TraversalArchive.Hash(bytes);
        if (change == "wrong-digest") digest = new('f', 64);
        else if (change == "extra-file") await File.WriteAllTextAsync(Path.Combine(folder, "extra.txt"), "Not an admitted original.");
        else if (change == "pending-write") await File.WriteAllTextAsync(Path.Combine(folder, "unfinished.pending"), "Pending.");
        else
        {
            var json = JsonNode.Parse(bytes)!.AsObject();
            switch (change)
            {
                case "legal-case": json["LegalCaseDetermined"] = true; break;
                case "public-scope": json["Scope"] = "PublicWebsiteInvestigation"; break;
                case "invented-synchronization": json["VideoAlignment"] = "Calibrated"; break;
                case "invented-at-comparison": json["ExternalAssistiveTechnologyComparison"] = "Passed"; break;
                case "changed-source": json["Source"]!["SourceSha256"] = new string('f', 64); break;
                case "changed-key": json["KeyboardRuns"]![2]!["Key"] = "Enter"; break;
            }
            bytes = Encoding.UTF8.GetBytes(json.ToJsonString()); digest = TraversalArchive.Hash(bytes);
            await File.WriteAllBytesAsync(manifestPath, bytes);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => MockTaskRecordings.VerifyAsync(folder, digest));
        var untouched = await File.ReadAllBytesAsync(Path.Combine(original.Folder, MockTaskRecordings.ManifestFile));
        Assert.True((await MockTaskRecordings.VerifyAsync(original.Folder, TraversalArchive.Hash(untouched))).IntegrityMatches);
    }

    [Fact]
    public async Task AlreadyCancelledMockCollectionCannotAllocateOutputOrClaimARecording()
    {
        var folder = NewFolder(); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MockTaskRecordings.RecordAsync(folder,
            HeadlessScreenReader.Browser.Playwright.BundledFixture.MockCheckoutWorkingV1, stop.Token));
        Assert.False(Directory.Exists(folder)); Assert.False(File.Exists(folder + ".zip"));
    }

    [Fact]
    public async Task InvalidMockCliArgumentsCannotAllocateAnOutputOrAdmitAWebsite()
    {
        var folder = NewFolder(); using var output = new StringWriter();
        var exit = await MockTaskCommands.RunAsync(["--record-mock-tasks", "https://example.com", folder], output);
        Assert.Equal(2, exit); Assert.Equal("MOCK_TASK_SCENARIO_REQUIRED" + Environment.NewLine, output.ToString());
        Assert.False(Directory.Exists(folder));
    }
}
