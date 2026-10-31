using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record RecordedSourceHeader(string Schema, NativeOwnership Native, FixtureJob Job, Guid WorkerId);
public sealed record RecordedSourceFrame(int SourceSequence, string File, double PresentationUnixMilliseconds, int Bytes, string Sha256);
public sealed record RecordedSourceBatch(string Schema, string HeaderSha256, int Sequence, string WireSha256,
    ImmutableArray<RecordedSourceFrame> Frames);
public sealed record RecordedSourceSnapshot(RecordedSourceHeader Header, ImmutableArray<RecordedSourceFrame> Frames,
    long Bytes, bool HasUncommittedFiles)
{
    public string Scope => "RetainedOriginalSourceSubset";
    public string Completeness => "NotAssessed";
    public bool IsCompletedRecording => false;
    public bool CanReplay => false;
    public string GoalVerification => "NotAssessed";
}

/// <summary>Private JPEG originals can survive child interruption without becoming a complete recording.</summary>
public sealed class RecordedSourceJournal
{
    private const string Schema = "hsr-worker-source-journal-v1";
    private readonly string _directory;
    private readonly RecordedSourceHeader _header;
    private readonly string _headerHash;
    private readonly HashSet<int> _sources = [];
    private int _sequence;
    private long _bytes;
    public int RetainedFrames => _sources.Count;

    private RecordedSourceJournal(string directory, RecordedSourceHeader header, string headerHash)
    { _directory = directory; _header = header; _headerHash = headerHash; }

    public static async Task<RecordedSourceJournal> BeginAsync(string root, NativeOwnership native, FixtureJob job,
        Guid worker, CancellationToken token)
    {
        var header = new RecordedSourceHeader(Schema, native, job, worker);
        Validate(header);
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); Regular(root, true);
        var directory = Path.Combine(root, Prefix(job.OrganizationId, job.JobId, job.Generation));
        Directory.CreateDirectory(directory); Regular(directory, true);
        var bytes = FixtureProgressProtocol.Encode(header);
        await WriteAsync(Path.Combine(directory, "start.json"), bytes, token).ConfigureAwait(false);
        return new(directory, header, TraversalArchive.Hash(bytes));
    }

    public async Task AppendAsync(FixtureSourceBatch value, string wireSha256, CancellationToken token)
    {
        var start = new FixtureProgressStart(FixtureProgressProtocol.Schema, _header.Job, _header.Native.Owner) { RetainSourceFrames = true };
        var snapshot = FixtureSourceProtocol.Snapshot(value, start, _sequence + 1);
        FixtureSourceProtocol.Require(wireSha256 == TraversalArchive.Hash(FixtureProgressProtocol.Encode(snapshot))
            && snapshot.Frames.All(frame => !_sources.Contains(frame.SourceSequence))
            && _sources.Count + snapshot.Frames.Length <= FixtureSourceProtocol.MaximumFrames
            && snapshot.Frames.Sum(frame => (long)frame.Content.Length) <= FixtureSourceProtocol.MaximumTotalBytes - _bytes);
        Regular(_directory, true);
        var frames = ImmutableArray.CreateBuilder<RecordedSourceFrame>();
        foreach (var frame in snapshot.Frames)
        {
            await WriteAsync(Path.Combine(_directory, frame.File), frame.Content, token).ConfigureAwait(false);
            frames.Add(new(frame.SourceSequence, frame.File, frame.PresentationUnixMilliseconds, frame.Content.Length, frame.Sha256));
        }
        var batch = new RecordedSourceBatch(Schema, _headerHash, snapshot.Sequence, wireSha256, frames.ToImmutable());
        var metadata = FixtureProgressProtocol.Encode(batch);
        await WriteAsync(Path.Combine(_directory, $"batch-{batch.Sequence:D4}-{TraversalArchive.Hash(metadata)}.json"), metadata, token).ConfigureAwait(false);
        foreach (var frame in frames) { _sources.Add(frame.SourceSequence); _bytes += frame.Bytes; }
        _sequence = snapshot.Sequence;
    }

    public RecordedSourceSnapshot Read(CancellationToken token = default) =>
        Read(Path.GetDirectoryName(_directory)!, _header.Job.OrganizationId, _header.Job.JobId, _header.Job.Generation, _header.WorkerId, token);

    public static RecordedSourceSnapshot Read(string root, Guid organization, Guid job, long generation, Guid producer,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        FixtureSourceProtocol.Require(organization != Guid.Empty && job != Guid.Empty && generation is >= 1 and <= 3 && producer != Guid.Empty);
        root = Path.GetFullPath(root); Regular(root, true);
        var directory = Path.Combine(root, Prefix(organization, job, generation)); Regular(directory, true);
        var headerBytes = ReadBytes(Path.Combine(directory, "start.json"), 8192);
        var header = JsonSerializer.Deserialize<RecordedSourceHeader>(headerBytes, ReaderWorkerWire.Json)
            ?? throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_INVALID");
        Validate(header);
        FixtureSourceProtocol.Require(header.Job.OrganizationId == organization && header.Job.JobId == job
            && header.Job.Generation == generation && header.WorkerId == producer);
        var hash = TraversalArchive.Hash(headerBytes);
        var paths = Directory.EnumerateFileSystemEntries(directory).Take(2 * FixtureSourceProtocol.MaximumFrames + 4).ToArray();
        FixtureSourceProtocol.Require(paths.Length <= 2 * FixtureSourceProtocol.MaximumFrames + 2);
        foreach (var path in paths) Regular(path);
        var committed = new HashSet<string>(StringComparer.Ordinal) { "start.json" };
        var sources = new HashSet<int>();
        var retained = ImmutableArray.CreateBuilder<RecordedSourceFrame>();
        long total = 0; var sequence = 0;
        foreach (var path in paths.Where(path => Path.GetFileName(path).StartsWith("batch-", StringComparison.Ordinal)
            && path.EndsWith(".json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var bytes = ReadBytes(path, 64 * 1024);
            var batch = JsonSerializer.Deserialize<RecordedSourceBatch>(bytes, ReaderWorkerWire.Json)
                ?? throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_INVALID");
            FixtureSourceProtocol.Require(batch.Schema == Schema && batch.HeaderSha256 == hash && batch.Sequence == ++sequence
                && batch.WireSha256 is { Length: 64 } && batch.WireSha256.All(char.IsAsciiHexDigitLower)
                && !batch.Frames.IsDefaultOrEmpty && batch.Frames.Length <= 64
                && Path.GetFileName(path) == $"batch-{batch.Sequence:D4}-{TraversalArchive.Hash(bytes)}.json");
            var originalFrames = ImmutableArray.CreateBuilder<FixtureSourceFrame>();
            foreach (var frame in batch.Frames)
            {
                FixtureSourceProtocol.Require(frame is not null && frame.SourceSequence is >= 1 and <= FixtureSourceProtocol.MaximumFrames
                    && frame.File == $"source-{frame.SourceSequence:D4}.jpg" && sources.Add(frame.SourceSequence)
                    && sources.Count <= FixtureSourceProtocol.MaximumFrames);
                var content = ReadBytes(Path.Combine(directory, frame.File), FixtureSourceProtocol.MaximumBatchBytes);
                FixtureSourceProtocol.Require(content.Length == frame.Bytes);
                originalFrames.Add(new(frame.SourceSequence, frame.File, frame.PresentationUnixMilliseconds, frame.Sha256, content));
                total += content.Length;
                FixtureSourceProtocol.Require(total <= FixtureSourceProtocol.MaximumTotalBytes);
                retained.Add(frame); committed.Add(frame.File);
            }
            var original = new FixtureSourceBatch(FixtureSourceProtocol.Schema, header.Native.Owner, sequence, originalFrames.ToImmutable());
            FixtureSourceProtocol.Validate(original,
                new(FixtureProgressProtocol.Schema, header.Job, header.Native.Owner) { RetainSourceFrames = true }, sequence);
            FixtureSourceProtocol.Require(TraversalArchive.Hash(FixtureProgressProtocol.Encode(original)) == batch.WireSha256);
            committed.Add(Path.GetFileName(path));
        }
        token.ThrowIfCancellationRequested();
        return new(header, retained.ToImmutable(), total, paths.Any(path => !committed.Contains(Path.GetFileName(path))));
    }

    public void Match(KeyboardRecordingBundle bundle)
    {
        var source = Read();
        var recording = bundle.Validate(_header.Job);
        FixtureSourceProtocol.Require(source.Frames.Length > 0 && !source.HasUncommittedFiles
            && source.Frames.All(frame => recording.SourceFiles.Any(original => original.Path == frame.File
                && original.Bytes == frame.Bytes && original.Sha256 == frame.Sha256)
                && recording.SourceFrames.Any(original => original.File == frame.File
                    && original.PresentationUnixMilliseconds == frame.PresentationUnixMilliseconds)));
    }

    private static void Validate(RecordedSourceHeader header)
    {
        FixtureSourceProtocol.Require(header is not null && header.Native is not null && header.Job is not null);
        OwnershipJournal.Validate(header.Native);
        FixtureProgressProtocol.Validate(new(FixtureProgressProtocol.Schema, header.Job, header.Native.Owner));
        FixtureSourceProtocol.Require(header.Schema == Schema && header.WorkerId != Guid.Empty
            && header.Native.Job == (header.Job with { RecordObservation = false, TraversalRoute = 0 }));
    }
    private static string Prefix(Guid organization, Guid job, long generation) => $"{organization:N}-{job:N}-{generation}";
    private static void Regular(string path, bool directory = false)
    {
        var attributes = File.GetAttributes(path);
        FixtureSourceProtocol.Require(!attributes.HasFlag(FileAttributes.ReparsePoint)
            && attributes.HasFlag(FileAttributes.Directory) == directory);
    }
    private static byte[] ReadBytes(string path, int limit)
    {
        Regular(path);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        FixtureSourceProtocol.Require(source.Length is > 0 && source.Length <= limit);
        var bytes = new byte[checked((int)source.Length)]; source.ReadExactly(bytes);
        FixtureSourceProtocol.Require(source.ReadByte() == -1); return bytes;
    }
    private static async Task WriteAsync(string path, byte[] bytes, CancellationToken token)
    {
        var pending = path + ".pending";
        await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await output.WriteAsync(bytes, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false); output.Flush(flushToDisk: true);
        }
        token.ThrowIfCancellationRequested(); File.Move(pending, path, overwrite: false);
    }
}
