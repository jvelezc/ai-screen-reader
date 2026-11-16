using System.IO.Compression;
using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record SyntheticSearchTaskCollectionReceipt(string Schema, SyntheticSearchTaskStart Start,
    string ManifestSha256, string ArchiveSha256, int AuthorizedBoundaries)
{
    public const string CurrentSchema = "hsr-search-task-output-v1";
}

/// <summary>Integrity and semantic validation. Producer authentication additionally requires the owned immutable execution.</summary>
public sealed partial record SyntheticSearchTaskBundle(SyntheticSearchTaskCollectionReceipt Receipt, byte[] Originals)
{
    public const int MaximumArchiveBytes = 26 * 1024 * 1024;
    public const int MaximumBundleBytes = MaximumArchiveBytes + 64 * 1024;

    public ImmutableDictionary<string, byte[]> ReadVerifiedOriginals(SyntheticSearchTaskStart expected)
    {
        Validate(expected);
        return ReadFiles(Originals, MaximumArchiveBytes, 1400).ToImmutableDictionary(StringComparer.Ordinal);
    }

    public void Validate(SyntheticSearchTaskStart expected)
    {
        expected.Validate();
        Require(Receipt is not null && Receipt.Schema == SyntheticSearchTaskCollectionReceipt.CurrentSchema && Receipt.Start == expected
            && Receipt.AuthorizedBoundaries == 85 && Originals is { Length: > 0 and <= MaximumArchiveBytes }
            && Receipt.ArchiveSha256 == TraversalArchive.Hash(Originals));
        var files = ReadFiles(Originals, MaximumArchiveBytes, 1400);
        Require(files.TryGetValue("search-task-execution.json", out var binding) && binding.Length <= SyntheticSearchTaskControl.MaximumFrameBytes
            && JsonSerializer.Deserialize<SyntheticSearchTaskStart>(binding, ReaderWorkerWire.Json) == expected
            && files.TryGetValue("fixture-source.txt", out var source) && TraversalArchive.Hash(source) == expected.Job.Source.Sha256);
        Require(files.TryGetValue(SyntheticSearchTaskManifest.ManifestFile, out var manifestBytes) && manifestBytes.Length <= 512 * 1024
            && TraversalArchive.Hash(manifestBytes) == Receipt.ManifestSha256);
        using var document = JsonDocument.Parse(manifestBytes!, new() { AllowDuplicateProperties = false });
        var manifest = Read<SyntheticSearchTaskManifest>(document.RootElement);
        Require(manifest.Schema == SyntheticSearchTaskManifest.CurrentSchema && manifest.Start == expected && manifest.CreatedAtUtc != default
            && manifest.ProducerSha256 is { Length: 64 } && manifest.ProducerSha256.All(char.IsAsciiHexDigitLower)
            && manifest.Viewport == "320x240" && !manifest.Files.IsDefaultOrEmpty && manifest.Files.Length == files.Count - 1
            && !manifest.KeyboardTrials.IsDefault && manifest.KeyboardTrials.Length == 4
            && new[] { manifest.ProducerVersion, manifest.Runtime, manifest.Platform }.All(value => value is { Length: > 0 and <= 500 } && !value.Any(char.IsControl)));
        var names = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var original in manifest.Files)
        {
            Require(original is not null && original.Path != SyntheticSearchTaskManifest.ManifestFile && names.Add(original.Path)
                && files.TryGetValue(original.Path, out var content) && original.Bytes == content.Length
                && original.Sha256 == TraversalArchive.Hash(content));
            total += original.Bytes;
            Require(total <= SyntheticSearchSourceProtocol.MaximumOriginalBytes);
        }
        ValidateExperiments(manifest, files);
    }

    public void MatchRetainedSources(SyntheticSearchRetainedSources retained)
    {
        Validate(retained.Start);
        Require(!retained.HasUncommittedFiles && !retained.Files.IsDefaultOrEmpty);
        var files = ReadFiles(Originals, MaximumArchiveBytes, 1400);
        var jpegPaths = files.Keys.Where(path => path.EndsWith(".jpg", StringComparison.Ordinal)).ToArray();
        Require(jpegPaths.Length == retained.Files.Length);
        foreach (var original in retained.Files)
        {
            var prefix = original.Trial == 5 ? "pointer-media/" : $"route-{original.Trial}-media/";
            Require(files.TryGetValue(prefix + original.Image.File, out var bytes) && bytes.Length == original.Image.Bytes
                && TraversalArchive.Hash(bytes) == original.Image.Sha256);
            using var row = JsonDocument.Parse(files[original.Trial == 5 ? "pointer.json" : $"route-{original.Trial}-recording.json"]);
            var root = row.RootElement;
            var scope = original.Trial == 5 ? root.GetProperty("Preparation").GetProperty("Focus").GetProperty("Before")
                : root.GetProperty("Receipt").GetProperty("Observations")[0].GetProperty("Snapshot");
            Require(scope.GetProperty("DocumentId").GetString() == original.DocumentId && scope.GetProperty("CaptureEpoch").GetInt64() == original.CaptureEpoch);
            var media = original.Trial == 5 ? root.GetProperty("Media") : root.GetProperty("Recording").GetProperty("Media");
            var precise = media.GetProperty("PreciseFrames").EnumerateArray().Where(frame => frame.GetProperty("File").GetString() == original.Image.File).ToArray();
            Require(precise.Length == 1 && precise[0].GetProperty("Sequence").GetInt32() == original.Image.SourceSequence
                && precise[0].GetProperty("PresentationUnixMilliseconds").GetDouble() == original.Image.PresentationUnixMilliseconds);
        }
    }

    public byte[] Encode(SyntheticSearchTaskStart expected)
    {
        Validate(expected);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry("receipt.json", CompressionLevel.NoCompression).Open()) JsonSerializer.Serialize(entry, Receipt, ReaderWorkerWire.Json);
            using (var entry = zip.CreateEntry("originals.zip", CompressionLevel.NoCompression).Open()) entry.Write(Originals);
        }
        Require(output.Length <= MaximumBundleBytes);
        return output.ToArray();
    }

    public static SyntheticSearchTaskBundle Decode(byte[] bytes, SyntheticSearchTaskStart expected)
    {
        var files = ReadFiles(bytes, MaximumBundleBytes, 2);
        Require(files.Count == 2 && files.ContainsKey("originals.zip"));
        Require(files.TryGetValue("receipt.json", out var receipt) && receipt.Length <= 8192);
        using var document = JsonDocument.Parse(receipt!, new() { AllowDuplicateProperties = false });
        var result = new SyntheticSearchTaskBundle(Read<SyntheticSearchTaskCollectionReceipt>(document.RootElement), files["originals.zip"]);
        result.Validate(expected);
        return result;
    }

    private static T Read<T>(JsonElement element)
    {
        var value = JsonSerializer.Deserialize<T>(element, ReaderWorkerWire.Json);
        Require(value is not null && JsonElement.DeepEquals(element, JsonSerializer.SerializeToElement(value, ReaderWorkerWire.Json)));
        return value!;
    }

    private static Dictionary<string, byte[]> ReadFiles(byte[] bytes, int maximum, int limit)
    {
        Require(bytes is { Length: > 0 } && bytes.Length <= maximum);
        using var input = new MemoryStream(bytes, false); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        Require(zip.Entries.Count is > 0 && zip.Entries.Count <= limit);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            Require(entry.FullName.Length is > 0 and <= 240 && entry.FullName.All(character => char.IsAsciiLetterOrDigit(character) || character is '/' or '-' or '_' or '.')
                && entry.FullName.Split('/').All(part => part is not ("" or "." or "..")) && names.Add(entry.FullName) && entry.Length is > 0 && entry.Length <= maximum);
            total = checked(total + entry.Length); Require(total <= maximum);
            using var source = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                var count = source.Read(buffer); if (count == 0) break;
                Require(output.Length + count <= entry.Length); output.Write(buffer, 0, count);
            }
            Require(output.Length == entry.Length); files.Add(entry.FullName, output.ToArray());
        }
        return files;
    }

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    { if (!condition) throw new BrowserOperationException("SEARCH_TASK_BUNDLE_UNCONFIRMED"); }
}
