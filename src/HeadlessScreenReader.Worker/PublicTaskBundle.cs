using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Bound original inventory and media; no wrapper-only relabeling or synthetic task substitution.</summary>
public sealed record PublicTaskBundle(PublicTaskReport Report, ImmutableDictionary<string, byte[]> Files)
{
    public byte[] Encode()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var file in Files) { using var entry = zip.CreateEntry(file.Key, CompressionLevel.NoCompression).Open(); entry.Write(file.Value); }
        if (output.Length > PublicTaskReport.MaximumArchiveBytes) throw new BrowserOperationException("PUBLIC_TASK_ARCHIVE_TOO_LARGE");
        var bytes = output.ToArray(); Decode(bytes, Report.Start); return bytes;
    }

    public static PublicTaskBundle Decode(byte[] bytes, PublicTaskStart expected)
    {
        expected.Validate();
        void Require(bool value) { if (!value) throw new BrowserOperationException("PUBLIC_TASK_ARCHIVE_INVALID"); }
        Require(bytes.Length is > 0 and <= PublicTaskReport.MaximumArchiveBytes);
        using var source = new MemoryStream(bytes, false); using var zip = new ZipArchive(source, ZipArchiveMode.Read);
        Require(zip.Entries.Count is >= 1 and <= 10 && zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() == zip.Entries.Count);
        var files = ImmutableDictionary.CreateBuilder<string, byte[]>(); long total = 0;
        foreach (var entry in zip.Entries)
        {
            Require(PublicTaskRules.FileName(entry.FullName) && entry.Length > 0);
            var limit = entry.FullName == "report.json" ? PublicTaskReport.MaximumReportBytes
                : entry.FullName.EndsWith(".json", StringComparison.Ordinal) ? PublicWebsiteObservation.MaximumSupportedBytes : 12 * 1024 * 1024;
            Require(entry.Length <= limit);
            using var input = entry.Open(); using var content = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                var read = input.Read(buffer); if (read == 0) break;
                total += read; Require(content.Length + read <= entry.Length && total <= PublicTaskReport.MaximumArchiveBytes);
                content.Write(buffer, 0, read);
            }
            Require(content.Length == entry.Length); files.Add(entry.FullName, content.ToArray());
        }
        Require(files.ContainsKey("report.json"));
        using var document = JsonDocument.Parse(files["report.json"]);
        var raw = document.RootElement;
        Require(raw.GetProperty("Scope").GetString() == "PublicServiceNavigationOnly"
            && raw.GetProperty("VideoAlignment").GetString() == "EventSeekingNotVerified"
            && raw.GetProperty("AssistiveTechnologyComparison").GetString() == "NotPerformed"
            && raw.GetProperty("LegalCaseDetermined").ValueKind == JsonValueKind.False);
        var report = JsonSerializer.Deserialize<PublicTaskReport>(files["report.json"], ReaderWorkerWire.Json)
            ?? throw new BrowserOperationException("PUBLIC_TASK_REPORT_INVALID");
        report.Validate(expected);
        Require(files.Count == report.Files.Length + 1);
        var documents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in report.Files)
        {
            Require(files.TryGetValue(file.Path, out var content) && content.Length == file.Bytes && PublicTaskRules.Hash(content) == file.Sha256);
            if (file.MediaType == "video/webm") Require(content!.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }));
            else
            {
                var snapshot = JsonSerializer.Deserialize<AccessibilitySnapshot>(content!, ReaderWorkerWire.Json);
                Require(snapshot is not null && expected.Job.Source.AllowsDocument(snapshot.DocumentUrl)
                    && snapshot.CaptureEpoch == 1 && !snapshot.Nodes.IsDefaultOrEmpty && snapshot.Nodes.Length <= ReaderWorkerRequest.MaximumNodes);
                var attempt = report.Attempts.Single(a => a.BeforeSnapshot == file.Path || a.AfterSnapshot == file.Path);
                if (attempt.BeforeSnapshot == file.Path) Require(snapshot!.DocumentUrl == expected.Job.Source.TargetUrl && documents.Add(snapshot.DocumentId));
                if (attempt.AfterSnapshot == file.Path && attempt.FinalUrl is { } url) Require(snapshot!.DocumentUrl == url);
            }
        }
        return new(report, files.ToImmutable());
    }
}
