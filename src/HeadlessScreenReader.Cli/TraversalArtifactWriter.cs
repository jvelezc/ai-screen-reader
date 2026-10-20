using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>Local append-only files. Unfinished writes retain a .pending marker; no hosted durability claim.</summary>
public static class TraversalArtifactWriter
{
    public static async Task WriteAsync(string folder, int route, TraversalReceipt receipt,
        FixtureBrowserEnvironment? environment = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(route, 1);
        var graph = TraversalProjection.Build(receipt);
        Directory.CreateDirectory(folder);
        foreach (var extension in new[] { "json", "md", "svg" })
        {
            var path = Path.Combine(folder, $"route-{route}.{extension}");
            if (File.Exists(path) || File.Exists(path + ".pending"))
                throw new IOException("TRAVERSAL_ARTIFACT_ALREADY_EXISTS");
        }
        await WriteNewAsync(Path.Combine(folder, $"route-{route}.json"),
            JsonSerializer.Serialize(new { Receipt = receipt, Graph = graph, Environment = environment }, TraversalDemo.JsonOptions));
        await WriteNewAsync(Path.Combine(folder, $"route-{route}.md"), TraversalReport.Render(receipt, route));
        await WriteNewAsync(Path.Combine(folder, $"route-{route}.svg"), TraversalTraceDiagram.Render(receipt, route));
    }

    internal static async Task WriteNewAsync(string path, string content)
    {
        // Cancellation of browser work must not discard the completed/uncertain input receipt.
        var pending = path + ".pending";
        if (File.Exists(path)) throw new IOException("TRAVERSAL_ARTIFACT_ALREADY_EXISTS");
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, FileOptions.Asynchronous))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            await writer.WriteAsync(content);
        File.Move(pending, path);
    }
}
