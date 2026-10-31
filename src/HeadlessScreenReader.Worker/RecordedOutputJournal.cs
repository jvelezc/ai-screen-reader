using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record RecordedOutputHeader(string Schema, NativeOwnership Native, FixtureJob Job, Guid WorkerId);
public sealed record RecordedOutputSeal(string Schema, string HeaderSha256, int Bytes, string BundleSha256);
public sealed record RecordedOutputRemoval(string Schema, string HeaderSha256, NativeRemoval Removal);
public sealed record RecordedOutputRecovery(int Examined, int Repaired, int AlreadyAcknowledged, int AwaitingRemoval)
{
    public bool IsCompletionReceipt => false;
    public bool CanReplay => false;
}
public sealed record RecordedOutputSnapshot(RecordedOutputHeader Header, RecordedOutputSeal Seal, byte[] Bundle,
    NativeRemoval? NativeRemoval)
{
    public bool IsCompletionReceipt => false;
    public bool CanReplay => false;
    public string GoalVerification => "NotAssessed";
}

/// <summary>Private host staging of complete original output, before native removal. Not database publication or live-media recovery.</summary>
public sealed class RecordedOutputJournal
{
    private const string Schema = "hsr-worker-recorded-output-v1";
    private readonly string _directory;
    private readonly RecordedOutputHeader _header;
    private readonly string _headerHash;
    private RecordedOutputJournal(string directory, RecordedOutputHeader header, string hash)
    { _directory = directory; _header = header; _headerHash = hash; }

    public static async Task<RecordedOutputJournal> BeginAsync(string root, NativeOwnership native, FixtureJob job,
        Guid worker, CancellationToken token)
    {
        var header = new RecordedOutputHeader(Schema, native, job, worker); Validate(header);
        token.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); Regular(root, true);
        var directory = DirectoryFor(root, job); Directory.CreateDirectory(directory); Regular(directory, true);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(header, ReaderWorkerWire.Json);
        await WriteNewAsync(directory, "start.json", bytes, token).ConfigureAwait(false);
        return new(directory, header, TraversalArchive.Hash(bytes));
    }

    public async Task SealAsync(byte[] bundle, Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        Require(bundle.Length is > 0 and <= PassiveObservationRecording.MaximumBundleBytes);
        bundle = bundle.ToArray(); ValidateBundle(bundle, _header.Job);
        await AuthorizeAsync(authorize, token).ConfigureAwait(false);
        Require(ReadHeader(_directory) == _header && TraversalArchive.Hash(ReadBytes(_directory, "start.json", 8192)) == _headerHash);
        await WriteNewAsync(_directory, "bundle.bin", bundle, token).ConfigureAwait(false);
        await AuthorizeAsync(authorize, token).ConfigureAwait(false);
        var seal = new RecordedOutputSeal(Schema, _headerHash, bundle.Length, TraversalArchive.Hash(bundle));
        await WriteNewAsync(_directory, "seal.json", JsonSerializer.SerializeToUtf8Bytes(seal, ReaderWorkerWire.Json), token).ConfigureAwait(false);
        var retained = Read(Path.GetDirectoryName(_directory)!, _header.Job, _header.WorkerId, token);
        Require(retained.Bundle.AsSpan().SequenceEqual(bundle));
        await AuthorizeAsync(authorize, token).ConfigureAwait(false);
    }

    /// <summary>Copy the actual ownership journal's recorded removal, never a caller-supplied success flag.</summary>
    public async Task AcknowledgeRemovalAsync(OwnershipJournal journal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ValidateInventory(_directory);
        Require(ReadHeader(_directory) == _header && TraversalArchive.Hash(ReadBytes(_directory, "start.json", 8192)) == _headerHash);
        var bytes = ReadBytes(journal.RemovalsDirectory, _header.Native.Owner.ToString("N") + ".json", 8192);
        var removal = Decode<NativeRemoval>(bytes);
        Require(removal.Schema == "hsr-native-removal-v1" && removal.Intent == _header.Native && removal.ContainerAbsenceConfirmed);
        var marker = new RecordedOutputRemoval(Schema, _headerHash, removal);
        var expected = JsonSerializer.SerializeToUtf8Bytes(marker, ReaderWorkerWire.Json);
        var destination = Path.Combine(_directory, "native-removal.json");
        var pending = destination + ".pending";
        if (Path.Exists(pending)) Require(ReadBytes(_directory, "native-removal.json.pending", 8192).AsSpan().SequenceEqual(expected));
        if (Path.Exists(destination)) Require(ReadBytes(_directory, "native-removal.json", 8192).AsSpan().SequenceEqual(expected));
        else if (Path.Exists(pending))
        {
            token.ThrowIfCancellationRequested(); File.Move(pending, destination, overwrite: false);
            Require(ReadBytes(_directory, "native-removal.json", 8192).AsSpan().SequenceEqual(expected));
        }
        else await WriteNewAsync(_directory, "native-removal.json", expected, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        Require(ReadHeader(_directory) == _header && TraversalArchive.Hash(ReadBytes(_directory, "start.json", 8192)) == _headerHash);
    }

    /// <summary>Repair historical cleanup acknowledgements, including retired intents. Does not seal, publish or execute work.</summary>
    public static async Task<RecordedOutputRecovery> RecoverRemovalsAsync(string root, OwnershipJournal journal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); root = Path.GetFullPath(root);
        try { Regular(root, true); }
        catch (DirectoryNotFoundException) { return new(0, 0, 0, 0); }
        catch (FileNotFoundException) { return new(0, 0, 0, 0); }
        Regular(journal.RemovalsDirectory, true);
        var examined = 0; var repaired = 0; var acknowledged = 0; var awaiting = 0;
        // Stream bounded per-attempt metadata; never load recording payloads during startup.
        foreach (var directory in Directory.EnumerateFileSystemEntries(root))
        {
            token.ThrowIfCancellationRequested(); Regular(directory, true); ValidateInventory(directory);
            var header = ReadHeader(directory);
            Require(Path.GetFileName(directory) == Path.GetFileName(DirectoryFor(root, header.Job)));
            examined++;
            var removalPath = Path.Combine(journal.RemovalsDirectory, header.Native.Owner.ToString("N") + ".json");
            var found = true;
            try { Regular(removalPath); }
            catch (FileNotFoundException) { found = false; }
            if (!found)
            {
                Require(!Path.Exists(Path.Combine(directory, "native-removal.json"))
                    && !Path.Exists(Path.Combine(directory, "native-removal.json.pending")));
                awaiting++; continue;
            }
            var existed = Path.Exists(Path.Combine(directory, "native-removal.json"));
            var output = new RecordedOutputJournal(directory, header, TraversalArchive.Hash(ReadBytes(directory, "start.json", 8192)));
            await output.AcknowledgeRemovalAsync(journal, token).ConfigureAwait(false);
            if (existed) acknowledged++; else repaired++;
        }
        token.ThrowIfCancellationRequested(); return new(examined, repaired, acknowledged, awaiting);
    }

    /// <summary>Operator-private exact-attempt read. Consumers additionally need current caller/producer checks before and after storage.</summary>
    public static RecordedOutputSnapshot ReadCandidate(string root, Guid organization, Guid job, long generation, CancellationToken token)
    {
        Require(organization != Guid.Empty && job != Guid.Empty && generation is >= 1 and <= 3);
        token.ThrowIfCancellationRequested(); root = Path.GetFullPath(root); Regular(root, true);
        var directory = Path.Combine(root, $"{organization:N}-{job:N}-{generation}"); Regular(directory, true);
        var header = ReadHeader(directory);
        Require(header.Job.OrganizationId == organization && header.Job.JobId == job && header.Job.Generation == generation);
        // A private candidate is not admission authority. The storage-only database claim must bind its actual producer.
        return Read(root, header.Job, header.WorkerId, token);
    }

    /// <summary>Operator-private exact-attempt read. Consumers additionally need current caller/producer checks before and after storage.</summary>
    public static RecordedOutputSnapshot Read(string root, FixtureJob expected, Guid worker, CancellationToken token)
    {
        expected.Validate(); Require(expected.RecordObservation && worker != Guid.Empty);
        token.ThrowIfCancellationRequested(); root = Path.GetFullPath(root); Regular(root, true);
        var directory = DirectoryFor(root, expected); Regular(directory, true); ValidateInventory(directory);
        var headerBytes = ReadBytes(directory, "start.json", 8192); var header = Decode<RecordedOutputHeader>(headerBytes); Validate(header);
        Require(header.Job == expected && header.WorkerId == worker);
        if (!File.Exists(Path.Combine(directory, "seal.json"))) throw new BrowserOperationException("WORKER_OUTPUT_NOT_SEALED");
        var seal = Decode<RecordedOutputSeal>(ReadBytes(directory, "seal.json", 8192));
        var hash = TraversalArchive.Hash(headerBytes);
        Require(seal.Schema == Schema && seal.HeaderSha256 == hash && seal.Bytes is > 0 and <= PassiveObservationRecording.MaximumBundleBytes
            && Digest(seal.BundleSha256));
        var bundle = ReadBytes(directory, "bundle.bin", PassiveObservationRecording.MaximumBundleBytes);
        Require(bundle.Length == seal.Bytes && TraversalArchive.Hash(bundle) == seal.BundleSha256); ValidateBundle(bundle, expected);
        NativeRemoval? removal = null;
        if (File.Exists(Path.Combine(directory, "native-removal.json")))
        {
            var marker = Decode<RecordedOutputRemoval>(ReadBytes(directory, "native-removal.json", 8192));
            Require(marker.Schema == Schema && marker.HeaderSha256 == hash && marker.Removal is not null
                && marker.Removal.Schema == "hsr-native-removal-v1" && marker.Removal.Intent == header.Native && marker.Removal.ContainerAbsenceConfirmed);
            removal = marker.Removal;
        }
        token.ThrowIfCancellationRequested(); return new(header, seal, bundle, removal);
    }

    private static void ValidateInventory(string directory)
    {
        string[] permitted = ["start.json", "bundle.bin", "seal.json", "native-removal.json"];
        var files = Directory.EnumerateFileSystemEntries(directory).Take(9).ToArray();
        Require(files.Length <= 8);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            Require(permitted.Contains(name) || name.EndsWith(".pending", StringComparison.Ordinal) && permitted.Contains(name[..^8]));
            Regular(file);
            Require(new FileInfo(file).Length <= (name.StartsWith("bundle.bin", StringComparison.Ordinal) ? PassiveObservationRecording.MaximumBundleBytes : 8192));
        }
    }

    private static void Validate(RecordedOutputHeader header)
    {
        Require(header is not null && header.Native is not null && header.Job is not null);
        OwnershipJournal.Validate(header!.Native); header.Job.Validate();
        Require(header.Schema == Schema && header.WorkerId != Guid.Empty && header.Job.RecordObservation
            && header.Native.Job == (header.Job with { RecordObservation = false, TraversalRoute = 0 }));
    }
    private static void ValidateBundle(byte[] bytes, FixtureJob job)
    {
        if (job.TraversalRoute > 0) KeyboardRecordingBundle.Decode(bytes, job);
        else PassiveRecordingBundle.Decode(bytes, job);
    }
    private static RecordedOutputHeader ReadHeader(string directory)
    { var header = Decode<RecordedOutputHeader>(ReadBytes(directory, "start.json", 8192)); Validate(header); return header; }
    private static T Decode<T>(byte[] bytes) where T : class => JsonSerializer.Deserialize<T>(bytes, ReaderWorkerWire.Json)
        ?? throw new BrowserOperationException("WORKER_OUTPUT_INVALID");
    private static string DirectoryFor(string root, FixtureJob job) => Path.Combine(root, $"{job.OrganizationId:N}-{job.JobId:N}-{job.Generation}");
    private static bool Digest(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    private static async Task AuthorizeAsync(Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("WORKER_OUTPUT_AUTHORITY_UNCONFIRMED"); token.ThrowIfCancellationRequested(); }
    private static void Regular(string path, bool directory = false)
    {
        var attributes = File.GetAttributes(path);
        Require((attributes & FileAttributes.ReparsePoint) == 0 && ((attributes & FileAttributes.Directory) != 0) == directory);
    }
    private static byte[] ReadBytes(string directory, string name, int limit)
    {
        Regular(directory, true); var path = Path.Combine(directory, name); Regular(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(input.Length is > 0 && input.Length <= limit);
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes); Require(input.ReadByte() == -1); return bytes;
    }
    private static async Task WriteNewAsync(string directory, string name, byte[] bytes, CancellationToken token)
    {
        Regular(directory, true); Require(bytes.Length is > 0 && bytes.Length <= (name == "bundle.bin" ? PassiveObservationRecording.MaximumBundleBytes : 8192));
        var pending = Path.Combine(directory, name + ".pending");
        await using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        { await file.WriteAsync(bytes, token).ConfigureAwait(false); await file.FlushAsync(token).ConfigureAwait(false); file.Flush(flushToDisk: true); }
        token.ThrowIfCancellationRequested(); File.Move(pending, Path.Combine(directory, name), overwrite: false);
        Require(ReadBytes(directory, name, name == "bundle.bin" ? PassiveObservationRecording.MaximumBundleBytes : 8192).AsSpan().SequenceEqual(bytes));
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    { if (!condition) throw new BrowserOperationException("WORKER_OUTPUT_INVALID"); }
}
