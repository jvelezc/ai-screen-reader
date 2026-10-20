using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalEvidenceViewerTests
{
    [Fact]
    public async Task UntrustedNamesAndEvents_AreEncodedInMarkupAndInertJsonWithExactCspHashes()
    {
        var receipt = await Receipt();
        var payload = "</script><script>globalThis.injected=true</script><img src=https://example.com/leak>";
        receipt = receipt with
        {
            Plan = receipt.Plan with { Name = payload },
            Timeline = [receipt.Timeline[0] with { Detail = payload }]
        };
        var html = TraversalEvidenceViewer.Render(receipt, 1, Failed);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<script>globalThis", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("\\u003C/script\\u003E", html);
        var style = Regex.Match(html, @"<style>(.*?)</style>", RegexOptions.Singleline).Groups[1].Value;
        var script = Regex.Match(html, @"<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("style-src 'sha256-" + Hash(style) + "'", html);
        Assert.Contains("script-src 'sha256-" + Hash(script) + "'", html);
        Assert.Contains("connect-src 'none'", html);
        Assert.Contains("form-action 'none'", html);
        Assert.DoesNotContain("unsafe-inline", html);
        Assert.DoesNotContain("unsafe-eval", html);
        Assert.DoesNotContain("fetch(", script);
        Assert.DoesNotContain("WebSocket", script);
        Assert.DoesNotContain("reader_execute", html);
    }

    [Fact]
    public async Task FailedRecording_RendersFailureStageWithoutInventedMediaOrGoal()
    {
        var receipt = await Receipt();
        var html = TraversalEvidenceViewer.Render(receipt, 1, Failed);
        Assert.DoesNotContain("<video", html);
        Assert.Contains("Recording unavailable. Stage: Decode. Code: DECODE_FAILED.", html);
        Assert.Contains("Destination Unknown", html);
        Assert.Contains("Source o1; actual result o2.", html);
        Assert.Contains("goal verification TimedOut", html);
        Assert.Contains("Reader observations", html);
        Assert.Contains("Structured evidence and hashes", html);
    }

    [Fact]
    public async Task InterruptedInput_RetainsUncertaintyAndNeverCreatesTheIntendedResultNode()
    {
        using var cancellation = new CancellationTokenSource();
        var browser = new TraversalRunnerTests.ControlledBrowser { AfterKey = cancellation.Cancel };
        var receipt = await new TraversalRunner().RunAsync(browser,
            TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)), cancellationToken: cancellation.Token);
        Assert.Equal(TraversalExecution.Cancelled, receipt.Execution);
        Assert.Null(receipt.Attempts[0].ResultObservationId);
        Assert.True(receipt.Attempts[0].Step!.InputMayHaveOccurred);
        var html = TraversalEvidenceViewer.Render(receipt, 1, Failed);
        Assert.Contains("actual result not reliably observed", html);
        Assert.Contains("Input may have occurred despite an incomplete or uncertain result", html);
        Assert.Contains("no reliable result node", html);
        Assert.DoesNotContain("Objective verified here", html);
    }

    [Fact]
    public async Task EmptyFailedReceipt_ShowsExecutionPolicyAndMissingEvidenceWithoutInventedStates()
    {
        var receipt = await Receipt();
        receipt = receipt with
        {
            Execution = TraversalExecution.SystemError,
            Policy = TraversalPolicy.Denied,
            ErrorCode = "<POLICY_DENIED>",
            Observations = [],
            Attempts = [],
            Timeline = [],
            TimelineTruncated = true,
            UnexecutedInstructions = 1
        };
        var html = TraversalEvidenceViewer.Render(receipt, 1, Failed);
        Assert.Contains("Execution SystemError", html);
        Assert.Contains("Policy Denied", html);
        Assert.Contains("Engine/policy code: &lt;POLICY_DENIED&gt;", html);
        Assert.Contains("Unexecuted route instructions: 1. Timeline truncated: True.", html);
        Assert.Contains("No reliable observations were retained", html);
        Assert.DoesNotContain("data-evidence=", html);
        Assert.DoesNotContain("Objective verified here", html);
        Assert.Contains("Destination Unknown", html);
    }

    [Fact]
    public async Task OversizedViewer_FallsBackToBoundedReadableEvidenceWithoutLosingTheReceipt()
    {
        var receipt = await Receipt();
        receipt = receipt with { Plan = receipt.Plan with { Name = new string('x', TraversalEvidenceViewer.MaximumDocumentBytes) } };
        var html = TraversalEvidenceViewer.Render(receipt, 1, Failed);
        Assert.True(Encoding.UTF8.GetByteCount(html) < TraversalEvidenceViewer.MaximumDocumentBytes);
        Assert.Contains("Evidence viewer display limit reached", html);
        Assert.Contains("route-1.md", html);
        Assert.Contains("route-1.json", html);
        Assert.DoesNotContain("<video", html);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
    }

    [Theory]
    [InlineData("route-1.html")]
    [InlineData("route-1.html.pending")]
    public async Task ExistingViewerOrPendingViewer_PreventsPublishingAnUnrelatedBundle(string existing)
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "viewer-writer-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, existing);
        await File.WriteAllTextAsync(path, "prior viewer");
        var receipt = await Receipt();
        var error = await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(folder, 1, receipt, recording: Failed));
        Assert.Equal("TRAVERSAL_ARTIFACT_ALREADY_EXISTS", error.Message);
        Assert.Equal("prior viewer", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(Path.Combine(folder, "route-1.json")));
        Assert.False(File.Exists(Path.Combine(folder, "route-1.md")));
        Assert.False(File.Exists(Path.Combine(folder, "route-1.svg")));
    }

    [Fact]
    public async Task InvalidRouteOrRecordingDeclaration_IsRejectedBeforeRendering()
    {
        var receipt = await Receipt();
        Assert.Throws<ArgumentOutOfRangeException>(() => TraversalEvidenceViewer.Render(receipt, 0, Failed));
        Assert.Throws<ArgumentException>(() => TraversalEvidenceViewer.Render(receipt with { HasRecording = true }, 1, Failed));
    }

    internal static readonly TraversalRecordingEvidence Failed = new("Decode", "DECODE_FAILED", null, [], null, null);
    internal static Task<TraversalReceipt> Receipt() =>
        TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.ReadCurrent)));
    private static string Hash(string text) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
