using System.Security.Cryptography;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class AssistanceEvidenceStoreTests
{
    [Fact]
    public async Task AssistanceFiles_AcknowledgeActualHashesAndNeverOverwriteExistingMetadata()
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "assistance-store-tests"));
        var intent = new OwnedAssistanceIntent(store.ScanId, 1, 1, 2, SyntheticInputAction.EnterCanary, 4);
        var first = await store.WriteAssistanceIntentAsync(intent);
        var completion = new OwnedAssistanceCompletion(intent, new(SyntheticInputAction.EnterCanary, 1, 1, false, false, null), 9);
        var second = await store.WriteAssistanceCompletionAsync(completion);
        foreach (var receipt in new[] { first, second })
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(store.Folder, receipt.ArtifactId));
            Assert.Equal(bytes.Length, receipt.Bytes); Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), receipt.Sha256);
        }
        var original = await File.ReadAllTextAsync(Path.Combine(store.Folder, first.ArtifactId));
        await Assert.ThrowsAsync<IOException>(() => store.WriteAssistanceIntentAsync(intent with { ControllerGeneration = 3 }));
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(store.Folder, first.ArtifactId)));
        Assert.DoesNotContain("Snapshot", original); Assert.DoesNotContain("Token", original);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("ordinal")]
    [InlineData("epoch")]
    [InlineData("generation")]
    [InlineData("action")]
    public async Task InvalidAssistanceIdentityOrAction_IsRejectedBeforeCreatingAnyMetadata(string caseId)
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "assistance-store-tests"));
        var intent = new OwnedAssistanceIntent(caseId == "scan" ? Guid.NewGuid().ToString("N") : store.ScanId,
            caseId == "ordinal" ? 0 : 1, caseId == "epoch" ? 0 : 1, caseId == "generation" ? 0 : 2,
            caseId == "action" ? (SyntheticInputAction)999 : SyntheticInputAction.EnterCanary, 0);
        await Assert.ThrowsAsync<IOException>(() => store.WriteAssistanceIntentAsync(intent));
        await Assert.ThrowsAsync<IOException>(() => store.WriteAssistanceCompletionAsync(new(intent,
            new(SyntheticInputAction.EnterCanary, 0, 0, false, true, "SYNTHETIC_INPUT_FENCED"), 1)));
        Assert.Single(Directory.EnumerateFiles(store.Folder));
    }
}
