using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalArtifactTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hsr-traversal-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Writer_RetainsJsonGraphAndReadableReportWithoutOverwritingOriginals()
    {
        var receipt = await TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)));
        await TraversalArtifactWriter.WriteAsync(_folder, 1, receipt);
        var path = Path.Combine(_folder, "route-1.json");
        var original = await File.ReadAllTextAsync(path);
        using var json = JsonDocument.Parse(original);
        Assert.Equal(receipt.RunId, json.RootElement.GetProperty("Receipt").GetProperty("RunId").GetString());
        Assert.Equal("Unknown", json.RootElement.GetProperty("Receipt").GetProperty("Reachability").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("Graph").GetProperty("Edges").GetArrayLength());
        Assert.Contains("TimedOut", await File.ReadAllTextAsync(Path.Combine(_folder, "route-1.md")));
        Assert.Empty(Directory.GetFiles(_folder, "*.pending"));
        await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(_folder, 1, receipt with { ErrorCode = "replacement" }));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Writer_PreservesCancelledInputReceiptAfterBrowserCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var browser = new TraversalRunnerTests.ControlledBrowser { AfterKey = cancellation.Cancel };
        var receipt = await new TraversalRunner().RunAsync(browser,
            TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.Activate)), cancellationToken: cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(TraversalExecution.Cancelled, receipt.Execution);
        await TraversalArtifactWriter.WriteAsync(_folder, 1, receipt);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_folder, "route-1.json")));
        var saved = json.RootElement.GetProperty("Receipt");
        Assert.Equal("Cancelled", saved.GetProperty("Execution").GetString());
        Assert.Equal(1, saved.GetProperty("CompletedKeystrokes").GetInt32());
        Assert.True(saved.GetProperty("Attempts")[0].GetProperty("Step").GetProperty("InputMayHaveOccurred").GetBoolean());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("Attempts")[0].GetProperty("ResultObservationId").ValueKind);
    }

    [Fact]
    public async Task ExistingPendingFile_IsNotOverwrittenOrPublishedAsComplete()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "route-1.json.pending");
        await File.WriteAllTextAsync(path, "incomplete original");
        var receipt = await TraversalRunnerTests.Run(new(), TraversalRunnerTests.Plan(new TraversalInstruction(ReaderCommand.ReadCurrent)));
        await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(_folder, 1, receipt));
        Assert.Equal("incomplete original", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(Path.Combine(_folder, "route-1.json")));
        Assert.False(File.Exists(Path.Combine(_folder, "route-1.md")));
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hsr-traversal-tests")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(_folder);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
