using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public sealed record TraversalPackageFile(string Path, long Bytes, string Sha256);
public sealed record TraversalPackageRoute(int Number, string Objective, string? RunId,
    string Execution, string Reachability, string Barrier, string RecordingStage, string? RecordingError,
    bool HasRecording, bool TimingCalibrated, string EvidenceFile);
public sealed record TraversalPackageManifest(string Schema, DateTimeOffset CreatedAtUtc,
    string Scope, string Operator, string NvdaComparison, string HumanValidation,
    string ProducerVersion, string ProducerSha256, string Runtime, string Platform,
    string FixtureSource, string FixtureSha256, int PlannedRoutes,
    ImmutableArray<TraversalPackageRoute> Routes, ImmutableArray<TraversalPackageFile> Files);
public sealed record TraversalPackageExport(string Folder, string Archive, string ArchiveSha256,
    string ManifestSha256, int Files, long Bytes);
public sealed record TraversalPackageVerification(bool IntegrityMatches, string ManifestSha256, int Files, long Bytes);

/// <summary>Local synthetic evidence export. Hash matching is not operator authentication or legal acceptance.</summary>
public static class TraversalEvidencePackage
{
    public const string ManifestFile = "evidence-manifest.json";
    public const string Schema = "hsr-recorded-traversal-package-v1";
    public const int MaximumFiles = 4096;
    public const long MaximumFileBytes = 128L * 1024 * 1024;
    public const long MaximumTotalBytes = 512L * 1024 * 1024;
    public const int MaximumManifestBytes = 4 * 1024 * 1024;
    private const string FixtureSource = "fixture-source.txt";
    private static readonly JsonSerializerOptions Options = new(TraversalDemo.JsonOptions)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static async Task<TraversalPackageExport> CreateAsync(string folder, IReadOnlyList<RecordedTraversalResult> results,
        DateTimeOffset createdAtUtc)
    {
        folder = Path.GetFullPath(folder);
        var archivePath = folder + ".zip";
        if (results.Count is < 1 or > 4) throw new InvalidDataException("EVIDENCE_ROUTES_INVALID");
        foreach (var path in new[] { Path.Combine(folder, ManifestFile), Path.Combine(folder, FixtureSource),
            Path.Combine(folder, "PACKAGE.txt"), archivePath })
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".pending"))
                throw new IOException("EVIDENCE_PACKAGE_ALREADY_EXISTS");
        // Inspect the owned tree before adding anything. Never follow a link into unrelated files.
        _ = EnumerateFiles(folder);
        var routes = ImmutableArray.CreateBuilder<TraversalPackageRoute>();
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var receipt = result.Receipt;
            var evidenceFile = receipt is null ? $"route-{i + 1}-failure.json" : $"route-{i + 1}.json";
            var path = Resolve(folder, evidenceFile);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumManifestBytes) throw new InvalidDataException("EVIDENCE_ROUTE_INVALID");
            using var document = JsonDocument.Parse(await ReadDocumentAsync(path).ConfigureAwait(false));
            var root = document.RootElement;
            if (!JsonElement.DeepEquals(root.GetProperty("Receipt"), JsonSerializer.SerializeToElement(receipt, TraversalDemo.JsonOptions))
                || !JsonElement.DeepEquals(root.GetProperty("Recording"), JsonSerializer.SerializeToElement(result.Recording, TraversalDemo.JsonOptions))
                || !JsonElement.DeepEquals(root.GetProperty("Environment"), JsonSerializer.SerializeToElement(result.Environment, TraversalDemo.JsonOptions))
                || receipt is not null && !JsonElement.DeepEquals(root.GetProperty("Graph"),
                    JsonSerializer.SerializeToElement(TraversalProjection.Build(receipt), TraversalDemo.JsonOptions)))
                throw new InvalidDataException("EVIDENCE_ROUTE_CHANGED");
            var fixtureIdentity = BundledFixtureCatalog.Describe(BundledFixture.TraversalRecording);
            if (receipt is not null && result.Environment?.Content != fixtureIdentity
                || result.Environment?.Content is { } content && content != fixtureIdentity)
                throw new InvalidDataException("EVIDENCE_FIXTURE_MISMATCH");
            routes.Add(new(i + 1, receipt?.Plan.Name ?? TraversalDemo.Plans[i].Name, receipt?.RunId,
                receipt?.Execution.ToString() ?? "NotStarted", receipt?.Reachability.ToString() ?? "Unknown",
                receipt?.Barrier.ToString() ?? "NotAssessed", result.Recording.Stage, result.Recording.ErrorCode,
                receipt?.HasRecording == true, result.Recording.Alignment?.Calibrated == true, evidenceFile));
        }
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, FixtureSource),
            BundledFixtureCatalog.ReadHtml(BundledFixture.TraversalRecording)).ConfigureAwait(false);
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "PACKAGE.txt"),
            "Recorded synthetic shop evidence\n\nExtract the complete ZIP before opening route-N.html. "
            + "The viewer is passive: selecting a state or action seeks its original recording.\n"
            + "Keep the printed manifest SHA-256 separately from this package. Verify the extracted directory with:\n"
            + "dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --verify-evidence <directory> <trusted-manifest-sha256>\n\n"
            + "Scope: authored HeadlessScreenReader commands in a controlled synthetic fixture. "
            + "No human operation, NVDA speech capture, real-site finding or complete use-case coverage is asserted.\n"
            + "Route receipts retain actual inputs, observed states, independent goal verification, timing uncertainty and failures. "
            + "The fixture source is saved as text, not an interactive replay.\n"
            + "Hashes establish correspondence to a separately retained digest. They do not establish collector identity, "
            + "trusted time, chain of custody, accessibility compliance or court admissibility. "
            + "A changed package and a newly calculated digest are not proof of the original collection.\n").ConfigureAwait(false);
        var files = await InventoryAsync(folder).ConfigureAwait(false);
        foreach (var (result, number) in results.Select((value, index) => (value, index + 1)))
        {
            if (result.Recording.Media is not { } media) continue;
            foreach (var original in media.Artifacts)
            {
                var retained = files.SingleOrDefault(f => f.Path == $"route-{number}-media/{original.File}");
                if (retained is null || retained.Bytes != original.Bytes || retained.Sha256 != original.Sha256)
                    throw new InvalidDataException("EVIDENCE_ORIGINAL_CHANGED");
            }
        }
        var assembly = typeof(TraversalEvidencePackage).Assembly;
        var manifest = new TraversalPackageManifest(Schema, createdAtUtc.ToUniversalTime(),
            "ControlledSyntheticFixture", "HeadlessScreenReaderAuthoredCommands", "NotPerformedForThisPackage", "NotPerformedForThisPackage",
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            await HashAsync(assembly.Location).ConfigureAwait(false), RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, FixtureSource, BundledFixtureCatalog.Describe(BundledFixture.TraversalRecording).ContentSha256,
            4, routes.ToImmutable(), files);
        var manifestJson = JsonSerializer.Serialize(manifest, Options);
        if (Encoding.UTF8.GetByteCount(manifestJson) > MaximumManifestBytes) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, ManifestFile), manifestJson).ConfigureAwait(false);
        var digest = await HashAsync(Path.Combine(folder, ManifestFile)).ConfigureAwait(false);
        var verified = await VerifyAsync(folder, digest).ConfigureAwait(false);
        // Rehash the exact bytes being archived. A concurrent change must not publish a valid-looking export.
        await using (var output = new FileStream(archivePath + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var file in files.Append(new(ManifestFile, new FileInfo(Path.Combine(folder, ManifestFile)).Length, digest)))
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest);
                await using var destination = entry.Open();
                var actual = await CopyAndHashAsync(Resolve(folder, file.Path), destination).ConfigureAwait(false);
                if (actual.Bytes != file.Bytes || actual.Sha256 != file.Sha256) throw new InvalidDataException("EVIDENCE_ARTIFACT_CHANGED");
            }
        }
        await VerifyAsync(folder, digest).ConfigureAwait(false);
        File.Move(archivePath + ".pending", archivePath);
        return new(folder, archivePath, await HashAsync(archivePath).ConfigureAwait(false), digest, verified.Files, verified.Bytes);
    }

    public static async Task<TraversalPackageVerification> VerifyAsync(string folder, string expectedManifestSha256)
    {
        if (!IsHash(expectedManifestSha256)) throw new InvalidDataException("EVIDENCE_DIGEST_INVALID");
        folder = Path.GetFullPath(folder);
        var paths = EnumerateFiles(folder);
        var manifestPath = Path.Combine(folder, ManifestFile);
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumManifestBytes) throw new InvalidDataException("EVIDENCE_MANIFEST_INVALID");
        var manifestBytes = await ReadDocumentAsync(manifestPath).ConfigureAwait(false);
        var digest = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
        if (!digest.Equals(expectedManifestSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("EVIDENCE_MANIFEST_CHANGED");
        var manifest = JsonSerializer.Deserialize<TraversalPackageManifest>(manifestBytes, Options);
        if (manifest is null || manifest.Schema != Schema || manifest.Files.IsDefaultOrEmpty || manifest.Files.Length > MaximumFiles
            || manifest.FixtureSource != FixtureSource || !IsHash(manifest.FixtureSha256)
            || manifest.Routes.IsDefaultOrEmpty || manifest.Routes.Length > 4 || manifest.PlannedRoutes != 4
            || manifest.Scope != "ControlledSyntheticFixture" || manifest.Operator != "HeadlessScreenReaderAuthoredCommands"
            || manifest.NvdaComparison != "NotPerformedForThisPackage" || manifest.HumanValidation != "NotPerformedForThisPackage")
            throw new InvalidDataException("EVIDENCE_MANIFEST_INVALID");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || !SafePath(file.Path) || file.Path.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase)
                || !names.Add(file.Path) || !IsHash(file.Sha256) || file.Bytes is < 0 or > MaximumFileBytes)
                throw new InvalidDataException("EVIDENCE_MANIFEST_INVALID");
            total += file.Bytes;
            if (total > MaximumTotalBytes) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
        }
        var actualPaths = paths.Where(p => p != ManifestFile).ToArray();
        if (!actualPaths.ToHashSet(StringComparer.Ordinal).SetEquals(manifest.Files.Select(f => f.Path)))
            throw new InvalidDataException("EVIDENCE_FILE_SET_CHANGED");
        foreach (var file in manifest.Files)
        {
            var actual = await CopyAndHashAsync(Resolve(folder, file.Path), Stream.Null).ConfigureAwait(false);
            if (actual.Bytes != file.Bytes || actual.Sha256 != file.Sha256) throw new InvalidDataException("EVIDENCE_ARTIFACT_CHANGED");
        }
        if (!manifest.Files.Any(f => f.Path == FixtureSource && f.Sha256 == manifest.FixtureSha256)
            || manifest.Routes.Where((r, i) => r is null || r.Number != i + 1 || !names.Contains(r.EvidenceFile)).Any())
            throw new InvalidDataException("EVIDENCE_MANIFEST_INVALID");
        return new(true, digest, manifest.Files.Length, total);
    }

    public static async Task<int> VerifyCommandAsync(string folder, string expectedManifestSha256, TextWriter output)
    {
        try
        {
            var result = await VerifyAsync(folder, expectedManifestSha256).ConfigureAwait(false);
            await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            var code = exception is InvalidDataException && exception.Message.StartsWith("EVIDENCE_", StringComparison.Ordinal)
                ? exception.Message : "EVIDENCE_PACKAGE_UNREADABLE";
            await output.WriteLineAsync(JsonSerializer.Serialize(new { IntegrityMatches = false, ErrorCode = code })).ConfigureAwait(false);
            return 3;
        }
    }

    private static async Task<ImmutableArray<TraversalPackageFile>> InventoryAsync(string folder)
    {
        var files = ImmutableArray.CreateBuilder<TraversalPackageFile>();
        long total = 0;
        foreach (var path in EnumerateFiles(folder))
        {
            var file = await CopyAndHashAsync(Resolve(folder, path), Stream.Null).ConfigureAwait(false);
            total += file.Bytes;
            if (total > MaximumTotalBytes) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
            files.Add(file with { Path = path });
        }
        return files.ToImmutable();
    }

    private static string[] EnumerateFiles(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException();
        RejectLink(folder);
        var pending = new Stack<string>();
        pending.Push(folder);
        var paths = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = 0;
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > MaximumFiles * 2) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
                RejectLink(entry);
                var relative = Path.GetRelativePath(folder, entry).Replace('\\', '/');
                if (!SafePath(relative) || !names.Add(relative)) throw new InvalidDataException("EVIDENCE_PATH_INVALID");
                if (relative.EndsWith(".pending", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("EVIDENCE_PENDING_OUTPUT");
                if (Directory.Exists(entry)) pending.Push(entry);
                else
                {
                    if (paths.Count >= MaximumFiles + 1) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
                    paths.Add(relative);
                }
            }
        }
        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool SafePath(string? path) => path is { Length: > 0 and <= 240 } && path.Split('/') is { Length: <= 8 } parts
        && parts.All(p => p.Length is > 0 and <= 120 && p is not "." and not ".." && !p.EndsWith('.')
            && !IsDeviceName(p) && p.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));
    private static bool IsDeviceName(string part)
    {
        var name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL"
            || name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))
                && name[3] is >= '1' and <= '9';
    }
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("EVIDENCE_LINK_NOT_ALLOWED");
    }
    private static string Resolve(string folder, string relative)
    {
        if (!SafePath(relative)) throw new InvalidDataException("EVIDENCE_PATH_INVALID");
        var path = folder;
        foreach (var part in relative.Split('/'))
        {
            path = Path.Combine(path, part);
            RejectLink(path);
        }
        return path;
    }
    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
    }
    private static async Task<byte[]> ReadDocumentAsync(string path)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (input.Length > MaximumManifestBytes) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
        var bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes).ConfigureAwait(false);
        if (await input.ReadAsync(new byte[1]).ConfigureAwait(false) != 0) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
        return bytes;
    }
    private static async Task<TraversalPackageFile> CopyAndHashAsync(string path, Stream destination)
    {
        RejectLink(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (input.Length > MaximumFileBytes) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > MaximumFileBytes) throw new InvalidDataException("EVIDENCE_LIMIT_EXCEEDED");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }
        return new("", total, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
