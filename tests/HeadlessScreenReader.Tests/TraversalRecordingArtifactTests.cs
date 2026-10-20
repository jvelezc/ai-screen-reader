using System.Security.Cryptography;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalRecordingArtifactTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("document")]
    [InlineData("hash")]
    public async Task ForeignRecordingBinding_IsRejectedBeforeAnyReportIsWritten(string mismatch)
    {
        var (folder, receipt, evidence) = await Inputs();
        var alignment = evidence.Alignment!;
        alignment = mismatch switch
        {
            "run" => alignment with { RunId = "foreign" },
            "document" => alignment with { DocumentId = "foreign" },
            _ => alignment with { VideoSha256 = new string('b', 64) }
        };
        evidence = evidence with { Alignment = alignment };
        var error = await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(folder, 1, receipt, recording: evidence));
        Assert.Equal("TRAVERSAL_RECORDING_BINDING_INVALID", error.Message);
        Assert.Throws<ArgumentException>(() => TraversalReport.Render(receipt, 1, evidence));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("escape")]
    [InlineData("empty")]
    public async Task MissingEmptyOrEscapedArtifact_NeverPublishesARecordingLink(string failure)
    {
        var (folder, receipt, evidence) = await Inputs();
        var media = evidence.Media!;
        if (failure == "missing") File.Delete(Path.Combine(folder, "route-1-media", "original.webm"));
        else evidence = evidence with
        {
            Alignment = null,
            Media = media with { Artifacts = failure == "empty" ? [] : [media.Artifacts[0], media.Artifacts[0] with { File = "../outside.webm" }] }
        };
        var error = await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(folder, 1, receipt, recording: evidence));
        Assert.Equal("TRAVERSAL_RECORDING_ARTIFACT_MISSING", error.Message);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task RecordingDeclaration_RequiresTheCorrespondingFinalizedMediaManifest()
    {
        var (folder, receipt, evidence) = await Inputs();
        var error = await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(folder, 1, receipt));
        Assert.Equal("TRAVERSAL_RECORDING_DECLARATION_INVALID", error.Message);
        Assert.Throws<ArgumentException>(() => TraversalReport.Render(receipt));
        error = await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(folder, 1,
            receipt with { HasRecording = false }, recording: evidence));
        Assert.Equal("TRAVERSAL_RECORDING_DECLARATION_INVALID", error.Message);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task FailedMediaAndUnknownOrPartialTiming_RemainVisibleAndDoNotUpgradeTheRoute()
    {
        var (folder, receipt, evidence) = await Inputs();
        var failed = evidence with { Stage = "Decode", ErrorCode = "DECODE_FAILED", Alignment = null, Media = null };
        await TraversalArtifactWriter.WriteAsync(folder, 1, receipt with { HasRecording = false }, recording: failed);
        var failedReport = await File.ReadAllTextAsync(Path.Combine(folder, "route-1.md"));
        Assert.Contains("DECODE\\_FAILED", failedReport);
        Assert.DoesNotContain("original.webm", failedReport);
        Assert.Contains("| Completed | Unknown | NoneObserved |", failedReport);
        var partial = evidence with
        {
            Alignment = evidence.Alignment! with
            { Cues = [new("o1", RecordingCueCoverage.Partial, 0, 0.1, 0, 2), new("o2", RecordingCueCoverage.Unknown, null, null, null, null)] }
        };
        var report = TraversalReport.Render(receipt, 1, partial);
        Assert.Contains("Video 0.000–0.100s (Partial)", report);
        Assert.Contains("Capture interval: Unknown", report);
        Assert.Contains("| Completed | Unknown | NoneObserved |", report);
        var unknown = partial with { Alignment = partial.Alignment! with { ReasonCode = "TRAVERSAL_RECORDING_ANCHOR_MISSING" } };
        Assert.DoesNotContain("#t=", TraversalReport.Render(receipt, 1, unknown));
    }

    private static async Task<(string Folder, TraversalReceipt Receipt, TraversalRecordingEvidence Evidence)> Inputs()
    {
        var receipt = await TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.ReadCurrent)));
        var folder = Path.GetFullPath(Path.Combine("artifacts", "traversal-recording-artifact-tests", Guid.NewGuid().ToString("N")));
        var mediaFolder = Path.Combine(folder, "route-1-media");
        Directory.CreateDirectory(mediaFolder);
        // Writer contract only; real decoded-video acceptance is in ChromiumTraversalRecordingTests.
        byte[] bytes = [1, 2, 3];
        await File.WriteAllBytesAsync(Path.Combine(mediaFolder, "original.webm"), bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var alignment = new TraversalRecordingAlignment(receipt.RunId, "doc", hash, "TRAVERSAL_RECORDING_CALIBRATED", 1000, 41, []);
        var media = new RecordingProbeMedia([], [], [new("original.webm", bytes.Length, hash, 1)], []);
        return (folder, receipt with { HasRecording = true }, new("Complete", null, alignment, [], media, null));
    }
}
