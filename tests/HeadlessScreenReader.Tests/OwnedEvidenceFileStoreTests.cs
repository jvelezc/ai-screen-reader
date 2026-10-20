using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class OwnedEvidenceFileStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrStaleEvidence_IsRejectedBeforeCreatingPayloadFiles(bool foreign)
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "owned-store-tests"));
        var intent = new OwnedCommandIntent(foreign ? Guid.NewGuid().ToString("N") : store.ScanId, 1, 1, ReaderCommand.ReadCurrent);
        var evidence = Evidence(intent, snapshotEpoch: foreign ? 1 : 2);
        await Assert.ThrowsAsync<IOException>(() => store.WriteResultAsync(evidence));
        Assert.Single(Directory.EnumerateFiles(store.Folder));
        Assert.Empty(Directory.EnumerateFiles(store.Folder, "*.pending"));
    }

    [Fact]
    public async Task OversizedOwnedResult_IsRejectedBeforePendingPublication()
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "owned-store-tests"));
        var intent = new OwnedCommandIntent(store.ScanId, 1, 1, ReaderCommand.ReadCurrent);
        var evidence = Evidence(intent, 1);
        evidence = evidence with { Step = evidence.Step with { Announcement = new string('x', OwnedEvidenceFileStore.MaximumFileBytes + 1) } };
        Assert.Equal("OWNED_STORE_LIMIT", (await Assert.ThrowsAsync<IOException>(() => store.WriteResultAsync(evidence))).Message);
        Assert.Single(Directory.EnumerateFiles(store.Folder));
        Assert.Empty(Directory.EnumerateFiles(store.Folder, "*.pending"));
    }

    private static OwnedCommandEvidence Evidence(OwnedCommandIntent intent, long snapshotEpoch) =>
        new(intent, new(1, ReaderCommand.ReadCurrent, ReaderMode.Browse, ReaderOutcome.Observed, "Safe", null, null, 0),
            new AccessibilitySnapshot("document", [], []) { CaptureEpoch = snapshotEpoch }, new(0, 1), [], false,
            new(0, TimeSpan.TicksPerSecond, new(0, 0)));
}
