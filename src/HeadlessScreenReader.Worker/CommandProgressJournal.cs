using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record CommandProgressHeader(string Schema, NativeOwnership Native, FixtureJob Job, Guid WorkerId);
public sealed record CommandProgressEntry(string Schema, string PreviousSha256, FixtureProgressMessage Message);
public sealed record CommandProgressOriginal(int Sequence, string SourceSha256, TraversalCheckpoint Checkpoint);
public sealed record CommandProgressSnapshot(CommandProgressSummary Summary, ImmutableArray<CommandProgressOriginal> Originals);
public sealed record CommandProgressSummary(string Schema, CommandProgressHeader Header, int Records, int Intents,
    int Results, TraversalCheckpoint? Last, string LastSha256, bool HasUncommittedWrite = false)
{
    public bool IsCompletionReceipt => false;
    public bool CanReplay => false;
    public bool InputMayHaveOccurred => Intents > 0;
}

/// <summary>Append-only worker-local diagnostics. Process-exit retention is distinct from power-loss durability or custody.</summary>
public sealed class CommandProgressJournal
{
    private const string Schema = "hsr-worker-command-journal-v1";
    private const int MaximumTotalBytes = 8 * 1024 * 1024;
    private readonly string _root;
    private readonly CommandProgressHeader _header;
    private readonly FixtureProgressStart _start;
    private string _hash;
    private FixtureProgressMessage? _prior;
    private int _bytes;
    private CommandProgressJournal(string root, CommandProgressHeader header, string hash, int bytes)
    { _root = root; _header = header; _start = new(FixtureProgressProtocol.Schema, header.Job, header.Native.Owner); _hash = hash; _bytes = bytes; }

    public static async Task<CommandProgressJournal> BeginAsync(string root, NativeOwnership native, FixtureJob job,
        Guid worker, CancellationToken token)
    {
        var header = new CommandProgressHeader(Schema, native, job, worker);
        Validate(header);
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); CheckRegular(root, directory: true);
        root = Path.Combine(root, Prefix(job)); Directory.CreateDirectory(root); CheckRegular(root, directory: true);
        var bytes = FixtureProgressProtocol.Encode(header);
        await WriteAsync(Path.Combine(root, "start.json"), bytes, token).ConfigureAwait(false);
        return new(root, header, TraversalArchive.Hash(bytes), bytes.Length);
    }
    public async Task AppendAsync(FixtureProgressMessage message, CancellationToken token)
    {
        FixtureProgressProtocol.Validate(message, _start, _prior);
        var bytes = FixtureProgressProtocol.Encode(new CommandProgressEntry(Schema, _hash, message));
        if (_bytes + bytes.Length > MaximumTotalBytes) throw new BrowserOperationException("WORKER_PROGRESS_TOO_LARGE");
        var hash = TraversalArchive.Hash(bytes);
        await WriteAsync(Path.Combine(_root, $"{message.Sequence:D4}-{hash}.json"), bytes, token).ConfigureAwait(false);
        _prior = message; _hash = hash; _bytes += bytes.Length;
    }
    public static CommandProgressSummary Read(string root, Guid organization, Guid job, long generation)
        => ReadSnapshot(root, organization, job, generation).Summary;

    public static CommandProgressSnapshot ReadSnapshot(string root, Guid organization, Guid job, long generation, CancellationToken token = default)
    {
        try { return ReadCore(root, organization, job, generation, token); }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        { throw new BrowserOperationException("WORKER_PROGRESS_INVALID"); }
    }
    private static CommandProgressSnapshot ReadCore(string root, Guid organization, Guid job, long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        FixtureProgressProtocol.Require(organization != Guid.Empty && job != Guid.Empty && generation >= 1);
        root = Path.GetFullPath(root); CheckRegular(root, directory: true);
        root = Path.Combine(root, Prefix(organization, job, generation)); CheckRegular(root, directory: true);
        var files = Directory.EnumerateFileSystemEntries(root).Take(13).ToArray();
        FixtureProgressProtocol.Require(files.Length <= 11);
        var headerBytes = ReadBytes(Path.Combine(root, "start.json"), 8192);
        var header = JsonSerializer.Deserialize<CommandProgressHeader>(headerBytes, ReaderWorkerWire.Json)
            ?? throw new BrowserOperationException("WORKER_PROGRESS_INVALID");
        Validate(header);
        FixtureProgressProtocol.Require(header.Job.OrganizationId == organization && header.Job.JobId == job && header.Job.Generation == generation);
        var start = new FixtureProgressStart(FixtureProgressProtocol.Schema, header.Job, header.Native.Owner);
        var hash = TraversalArchive.Hash(headerBytes); var total = headerBytes.Length; var intents = 0; var results = 0;
        FixtureProgressMessage? prior = null;
        var originals = ImmutableArray.CreateBuilder<CommandProgressOriginal>();
        foreach (var file in files.Where(p => Path.GetFileName(p) != "start.json" && !p.EndsWith(".pending", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var bytes = ReadBytes(file, FixtureProgressProtocol.MaximumFrameBytes + 4096); total += bytes.Length;
            FixtureProgressProtocol.Require(total <= MaximumTotalBytes);
            var entry = JsonSerializer.Deserialize<CommandProgressEntry>(bytes, ReaderWorkerWire.Json)
                ?? throw new BrowserOperationException("WORKER_PROGRESS_INVALID");
            FixtureProgressProtocol.Validate(entry.Message, start, prior);
            var actualHash = TraversalArchive.Hash(bytes);
            FixtureProgressProtocol.Require(entry.Schema == Schema && entry.PreviousSha256 == hash
                && Path.GetFileName(file) == $"{entry.Message.Sequence:D4}-{actualHash}.json");
            prior = entry.Message; hash = actualHash;
            originals.Add(new(prior.Sequence, actualHash, prior.Checkpoint));
            if (prior.Checkpoint.Phase == TraversalCheckpointPhase.Intent) intents++;
            if (prior.Checkpoint.Phase == TraversalCheckpointPhase.Observed) results++;
        }
        var pending = files.Where(p => p.EndsWith(".pending", StringComparison.Ordinal)).ToArray();
        FixtureProgressProtocol.Require(pending.Length <= 1);
        if (pending.Length != 0)
        {
            CheckRegular(pending[0]);
            var name = Path.GetFileName(pending[0]); var expected = $"{(prior?.Sequence ?? 0) + 1:D4}-";
            FixtureProgressProtocol.Require(name.Length == expected.Length + 64 + ".json.pending".Length
                && name.StartsWith(expected, StringComparison.Ordinal) && name.EndsWith(".json.pending", StringComparison.Ordinal)
                && name.AsSpan(expected.Length, 64).ToString().All(char.IsAsciiHexDigitLower)
                && new FileInfo(pending[0]).Length <= FixtureProgressProtocol.MaximumFrameBytes + 4096);
        }
        token.ThrowIfCancellationRequested();
        return new(new(Schema, header, prior?.Sequence ?? 0, intents, results, prior?.Checkpoint, hash, pending.Length != 0), originals.ToImmutable());
    }
    private static void Validate(CommandProgressHeader header)
    {
        FixtureProgressProtocol.Require(header.Native is not null && header.Job is not null);
        OwnershipJournal.Validate(header.Native);
        FixtureProgressProtocol.Validate(new(FixtureProgressProtocol.Schema, header.Job, header.Native.Owner));
        FixtureProgressProtocol.Require(header.Schema == Schema && header.WorkerId != Guid.Empty
            && header.Native.Job == (header.Job with { RecordObservation = false, TraversalRoute = 0 }));
    }
    private static string Prefix(FixtureJob job) => Prefix(job.OrganizationId, job.JobId, job.Generation);
    private static string Prefix(Guid organization, Guid job, long generation) => $"{organization:N}-{job:N}-{generation}";
    private static void CheckRegular(string path, bool directory = false)
    {
        var attributes = File.GetAttributes(path);
        FixtureProgressProtocol.Require(!attributes.HasFlag(FileAttributes.ReparsePoint)
            && attributes.HasFlag(FileAttributes.Directory) == directory);
    }
    private static byte[] ReadBytes(string path, int limit)
    {
        CheckRegular(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        FixtureProgressProtocol.Require(file.Length is > 0 && file.Length <= limit);
        var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
        FixtureProgressProtocol.Require(file.ReadByte() == -1); return bytes;
    }
    private static async Task WriteAsync(string path, byte[] bytes, CancellationToken token)
    {
        if (File.Exists(path)) throw new IOException("WORKER_PROGRESS_ALREADY_RETAINED");
        var pending = path + ".pending";
        await using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await file.WriteAsync(bytes, token).ConfigureAwait(false);
            await file.FlushAsync(token).ConfigureAwait(false); file.Flush(flushToDisk: true);
        }
        token.ThrowIfCancellationRequested();
        File.Move(pending, path, overwrite: false);
    }
}
