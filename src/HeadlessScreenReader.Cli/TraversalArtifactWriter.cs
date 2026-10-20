using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>Local append-only files. Unfinished writes retain a .pending marker; no hosted durability claim.</summary>
public static class TraversalArtifactWriter
{
    public static async Task WriteAsync(string folder, int route, TraversalReceipt receipt,
        FixtureBrowserEnvironment? environment = null, TraversalRecordingEvidence? recording = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(route, 1);
        var graph = TraversalProjection.Build(receipt);
        Directory.CreateDirectory(folder);
        foreach (var extension in recording is null ? new[] { "json", "md", "svg" } : new[] { "json", "md", "svg", "html" })
        {
            var path = Path.Combine(folder, $"route-{route}.{extension}");
            if (File.Exists(path) || File.Exists(path + ".pending"))
                throw new IOException("TRAVERSAL_ARTIFACT_ALREADY_EXISTS");
        }
        if (receipt.HasRecording != (recording?.Media is not null))
            throw new IOException("TRAVERSAL_RECORDING_DECLARATION_INVALID");
        if (recording?.Alignment is { } alignment)
        {
            if (alignment.RunId != receipt.RunId || receipt.Observations.Any(o => o.Snapshot.DocumentId != alignment.DocumentId)
                || recording.Media?.Artifacts.SingleOrDefault(a => a.File == "original.webm")?.Sha256 != alignment.VideoSha256)
                throw new IOException("TRAVERSAL_RECORDING_BINDING_INVALID");
        }
        if (recording?.Media is { } media)
        {
            if (media.Artifacts.IsDefaultOrEmpty || media.Artifacts.Count(a => a.File == "original.webm") != 1)
                throw new IOException("TRAVERSAL_RECORDING_ARTIFACT_MISSING");
            var root = Path.GetFullPath(Path.Combine(folder, $"route-{route}-media")) + Path.DirectorySeparatorChar;
            foreach (var artifact in media.Artifacts)
            {
                var path = Path.GetFullPath(Path.Combine(root, artifact.File));
                if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
                    throw new IOException("TRAVERSAL_RECORDING_ARTIFACT_MISSING");
                var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                if (bytes.LongLength != artifact.Bytes || Convert.ToHexStringLower(SHA256.HashData(bytes)) != artifact.Sha256)
                    throw new IOException("TRAVERSAL_RECORDING_ARTIFACT_CHANGED");
            }
        }
        var viewer = recording is null ? null : TraversalEvidenceViewer.Render(receipt, route, recording);
        await WriteNewAsync(Path.Combine(folder, $"route-{route}.json"),
            JsonSerializer.Serialize(new
            {
                Receipt = receipt,
                Graph = graph,
                Environment = environment,
                Recording = recording,
                ViewerFile = viewer is null ? null : $"route-{route}.html"
            }, TraversalDemo.JsonOptions));
        await WriteNewAsync(Path.Combine(folder, $"route-{route}.md"), TraversalReport.Render(receipt, route, recording));
        await WriteNewAsync(Path.Combine(folder, $"route-{route}.svg"), TraversalTraceDiagram.Render(receipt, route));
        if (viewer is not null) await WriteNewAsync(Path.Combine(folder, $"route-{route}.html"), viewer);
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
