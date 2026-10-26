using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>Operator import of an already collected package, not a new recording or tenant admission path.</summary>
public static class TraversalArchiveCommands
{
    private static readonly JsonSerializerOptions Json = new()
    { AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static async Task<TraversalArchiveReceipt> ImportAsync(string folder, string trustedManifestSha256,
        Guid organization, Guid archiveId, ITraversalObjectStore store, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        await TraversalEvidencePackage.VerifyAsync(folder, trustedManifestSha256).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        folder = Path.GetFullPath(folder);
        var manifestPath = Path.Combine(folder, TraversalEvidencePackage.ManifestFile);
        await using var input = OpenOriginal(manifestPath);
        if (input.Length is < 1 or > TraversalEvidencePackage.MaximumManifestBytes) throw new InvalidDataException("EVIDENCE_MANIFEST_INVALID");
        var bytes = new byte[(int)input.Length]; await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (TraversalArchive.Hash(bytes) != trustedManifestSha256.ToLowerInvariant()) throw new InvalidDataException("EVIDENCE_MANIFEST_CHANGED");
        var manifest = JsonSerializer.Deserialize<TraversalPackageManifest>(bytes, Json) ?? throw new InvalidDataException("EVIDENCE_MANIFEST_INVALID");
        var sources = manifest.Files.Select(file => new TraversalArchiveSource(file.Path, file.Bytes, file.Sha256.ToLowerInvariant(),
            () => OpenOriginal(Path.Combine(folder, file.Path.Replace('/', Path.DirectorySeparatorChar))))).ToList();
        sources.Add(new(TraversalArchive.SourceManifest, bytes.Length, TraversalArchive.Hash(bytes), () => OpenOriginal(manifestPath)));
        return await new TraversalArchive(store).FinalizeAsync(organization, archiveId,
            trustedManifestSha256.ToLowerInvariant(), sources, token).ConfigureAwait(false);
    }

    private static FileStream OpenOriginal(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null
                || Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("EVIDENCE_LINK_DENIED");
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        S3TraversalObjectStore? s3 = null;
        try
        {
            if (args.Length == 0) throw new ArgumentException();
            var import = args[0] is "--archive-evidence-local" or "--archive-evidence-s3";
            var local = args[0] is "--archive-evidence-local" or "--verify-archive-local";
            if (args[0] is not ("--archive-evidence-local" or "--archive-evidence-s3" or "--verify-archive-local" or "--verify-archive-s3")
                || args.Length != (import ? local ? 6 : 5 : local ? 5 : 4)) throw new ArgumentException();
            ITraversalObjectStore store;
            if (local) store = new FileTraversalObjectStore(args[import ? 5 : 1]);
            else
            {
                var configuration = S3EvidenceConfiguration.Optional(Environment.GetEnvironmentVariable("HSR_EVIDENCE_S3_ACCESS_KEY"),
                    Environment.GetEnvironmentVariable("HSR_EVIDENCE_S3_SECRET")) ?? throw new ArgumentException();
                store = s3 = new(configuration);
            }
            if (import)
            {
                var receipt = await ImportAsync(args[1], args[2], Guid.ParseExact(args[3], "D"), Guid.ParseExact(args[4], "D"), store, stop.Token).ConfigureAwait(false);
                await output.WriteLineAsync(JsonSerializer.Serialize(receipt, Json)).ConfigureAwait(false);
            }
            else
            {
                var offset = local ? 2 : 1;
                var identity = new TraversalArchiveIdentity(Guid.ParseExact(args[offset], "D"), Guid.ParseExact(args[offset + 1], "D"), args[offset + 2]);
                var proof = await new TraversalArchive(store).VerifyAsync(identity, stop.Token).ConfigureAwait(false);
                await output.WriteLineAsync(JsonSerializer.Serialize(proof, Json)).ConfigureAwait(false);
            }
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        { await output.WriteLineAsync("TRAVERSAL_ARCHIVE_CANCELLED_UNCONFIRMED").ConfigureAwait(false); return 3; }
        catch (TraversalArchiveException exception)
        { await output.WriteLineAsync(exception.Message).ConfigureAwait(false); return 3; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or IOException or UnauthorizedAccessException or JsonException)
        { await output.WriteLineAsync("TRAVERSAL_ARCHIVE_INPUT_INVALID_OR_UNREADABLE").ConfigureAwait(false); return 2; }
        finally { s3?.Dispose(); Console.CancelKeyPress -= cancel; }
    }
}
