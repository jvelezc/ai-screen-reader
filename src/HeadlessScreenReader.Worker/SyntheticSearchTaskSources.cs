using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record SyntheticSearchSourceBatch(string Schema, Guid ExecutionId, int Trial, int Sequence, string DocumentId, long CaptureEpoch,
    ImmutableArray<FixtureSourceFrame> Frames);
public sealed record SyntheticSearchSourceAck(string Schema, Guid ExecutionId, int Trial, int Sequence, string BatchSha256);
public sealed record SyntheticSearchSourceFile(int Trial, string DocumentId, long CaptureEpoch, RetainedSourceImageFile Image);
public sealed record SyntheticSearchSourceReceipt(string Schema, string StartSha256, int Trial, int Sequence, string DocumentId, long CaptureEpoch,
    string WireSha256, ImmutableArray<RetainedSourceImageFile> Images);
public sealed record SyntheticSearchRetainedSources(SyntheticSearchTaskStart Start, ImmutableArray<SyntheticSearchSourceFile> Files,
    long Bytes, bool HasUncommittedFiles)
{
    public string Scope => "RetainedSyntheticSearchOriginalSubset";
    public string Completeness => "NotAssessed";
    public bool CanReplay => false;
    public bool IsCompletedRecording => false;
}

/// <summary>Original image bytes have their own stream; they do not acknowledge input or a completed video.</summary>
public static class SyntheticSearchSourceProtocol
{
    public const string Schema = "hsr-search-task-sources-v1";
    public const int MaximumFrameBytes = 1024 * 1024;
    public const int MaximumFrames = 1250;
    public const int MaximumOriginalBytes = 25 * 1024 * 1024;

    public static void Validate(SyntheticSearchSourceBatch batch, Guid execution, int sequence)
    {
        Require(batch.Schema == Schema && batch.ExecutionId == execution && execution != Guid.Empty
            && batch.Trial is >= 1 and <= 5 && batch.Sequence == sequence && sequence is >= 1 and <= MaximumFrames
            && batch.DocumentId is { Length: > 0 and <= 128 } && !batch.DocumentId.Any(char.IsControl) && batch.CaptureEpoch == 1
            && !batch.Frames.IsDefaultOrEmpty && batch.Frames.Length <= 64);
        long bytes = 0;
        var originals = new HashSet<int>();
        foreach (var frame in batch.Frames)
        {
            Require(frame is not null && frame.SourceSequence is >= 1 and <= 250 && originals.Add(frame.SourceSequence)
                && frame.File == $"source-{frame.SourceSequence:D4}.jpg" && double.IsFinite(frame.PresentationUnixMilliseconds)
                && frame.PresentationUnixMilliseconds > 0 && frame.Content is { Length: >= 4 and <= 512 * 1024 }
                && frame.Content.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })
                && frame.Sha256 == TraversalArchive.Hash(frame.Content));
            bytes += frame.Content.Length;
            Require(bytes <= 512 * 1024);
        }
    }

    internal static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    { if (!condition) throw new BrowserOperationException("SEARCH_TASK_SOURCE_FRAMES_INVALID"); }
}

public sealed partial class SyntheticSearchTaskGrants
{
    private int _sourceSequence;
    private long _sourceBytes;
    private readonly HashSet<(int Trial, int Sequence)> _sourceIds = [];

    internal bool HasSources(int trial, string document, long epoch) => epoch == 1
        && _documents.TryGetValue(trial, out var observed) && observed == document && _sourceIds.Any(source => source.Trial == trial);

    public async Task<SyntheticSearchSourceAck> RetainSourcesAsync(SyntheticSearchSourceBatch batch,
        Func<CancellationToken, Task<bool>> authorize, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        await _serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            SyntheticSearchSourceProtocol.Validate(batch, _start.ExecutionId, _sourceSequence + 1);
            // Snapshot mutable byte arrays before trusting fingerprints or starting any durable write.
            batch = batch with { Frames = [.. batch.Frames.Select(frame => frame with { Content = frame.Content.ToArray() })] };
            SyntheticSearchSourceProtocol.Validate(batch, _start.ExecutionId, _sourceSequence + 1);
            SyntheticSearchSourceProtocol.Require(!_stopped && _sequence < 85 && batch.Trial == _currentTrial
                && batch.Frames.All(frame => !_sourceIds.Contains((batch.Trial, frame.SourceSequence)))
                && _sourceIds.Count + batch.Frames.Length <= SyntheticSearchSourceProtocol.MaximumFrames
                && batch.Frames.Sum(frame => (long)frame.Content.Length) <= SyntheticSearchSourceProtocol.MaximumOriginalBytes - _sourceBytes);
            SyntheticSearchSourceProtocol.Require(_documents.TryGetValue(batch.Trial, out var document) ? document == batch.DocumentId
                : !_documents.Values.Contains(batch.DocumentId, StringComparer.Ordinal));
            if (await DecideAsync(authorize, token).ConfigureAwait(false) != SyntheticSearchInputDecision.Granted)
                throw new BrowserOperationException("SEARCH_TASK_SOURCE_AUTHORITY_UNCONFIRMED");
            RegularDirectory(_directory);
            var folder = Path.Combine(_directory, $"trial-{batch.Trial}");
            Directory.CreateDirectory(folder);
            RegularDirectory(folder);
            foreach (var frame in batch.Frames)
                await WriteNewAsync(Path.Combine(folder, frame.File), frame.Content, token).ConfigureAwait(false);
            var digest = TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(batch, ReaderWorkerWire.Json));
            var receipt = new SyntheticSearchSourceReceipt(SyntheticSearchSourceProtocol.Schema,
                TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(_start, ReaderWorkerWire.Json)), batch.Trial, batch.Sequence, batch.DocumentId, batch.CaptureEpoch, digest,
                [.. batch.Frames.Select(frame => new RetainedSourceImageFile(frame.SourceSequence, frame.File, frame.PresentationUnixMilliseconds,
                    frame.Content.Length, frame.Sha256))]);
            // The commit record follows its originals; interrupted uncommitted files are retained explicitly.
            var metadataPath = Path.Combine(_directory, $"sources-{batch.Sequence:D4}-{digest}.json");
            var pendingPath = Path.Combine(_directory, $"pending-sources-{batch.Sequence:D4}-{digest}.json");
            await WriteNewAsync(pendingPath,
                JsonSerializer.SerializeToUtf8Bytes(receipt, ReaderWorkerWire.Json), token).ConfigureAwait(false);
            File.Move(pendingPath, metadataPath, overwrite: false);
            foreach (var frame in batch.Frames) _sourceIds.Add((batch.Trial, frame.SourceSequence));
            _sourceBytes += batch.Frames.Sum(frame => (long)frame.Content.Length);
            _sourceSequence = batch.Sequence;
            _documents.TryAdd(batch.Trial, batch.DocumentId);
            token.ThrowIfCancellationRequested();
            if (await DecideAsync(authorize, token).ConfigureAwait(false) != SyntheticSearchInputDecision.Granted)
                throw new BrowserOperationException("SEARCH_TASK_SOURCE_AUTHORITY_UNCONFIRMED");
            return new(SyntheticSearchSourceProtocol.Schema, _start.ExecutionId, batch.Trial, batch.Sequence, digest);
        }
        catch (OperationCanceledException) { _stopped = true; throw; }
        catch (BrowserOperationException) { _stopped = true; throw; }
        catch (Exception) { _stopped = true; throw new BrowserOperationException("SEARCH_TASK_SOURCE_WRITE_UNCONFIRMED"); }
        finally { _serial.Release(); }
    }

    public SyntheticSearchRetainedSources ReadRetainedSources()
    {
        RegularDirectory(_directory);
        var startBytes = ReadBounded(Path.Combine(_directory, "start.json"), SyntheticSearchTaskControl.MaximumFrameBytes);
        SyntheticSearchSourceProtocol.Require(JsonSerializer.Deserialize<SyntheticSearchTaskStart>(startBytes, ReaderWorkerWire.Json) == _start);
        var headerHash = TraversalArchive.Hash(startBytes);
        var result = ImmutableArray.CreateBuilder<SyntheticSearchSourceFile>();
        var committed = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        var paths = Directory.EnumerateFiles(_directory, "sources-*.json").Take(SyntheticSearchSourceProtocol.MaximumFrames + 1).Order(StringComparer.Ordinal).ToArray();
        SyntheticSearchSourceProtocol.Require(paths.Length <= SyntheticSearchSourceProtocol.MaximumFrames);
        var sequence = 0;
        var documents = new Dictionary<int, string>();
        foreach (var path in paths)
        {
            var receipt = JsonSerializer.Deserialize<SyntheticSearchSourceReceipt>(ReadBounded(path, 64 * 1024), ReaderWorkerWire.Json);
            SyntheticSearchSourceProtocol.Require(receipt is not null && receipt.Schema == SyntheticSearchSourceProtocol.Schema
                && receipt.StartSha256 == headerHash && receipt.Trial is >= 1 and <= 5 && receipt.Sequence == ++sequence
                && !receipt.Images.IsDefaultOrEmpty && receipt.Images.Length <= 64
                && receipt.Images.All(image => image is not null)
                && Path.GetFileName(path) == $"sources-{receipt.Sequence:D4}-{receipt.WireSha256}.json");
            var frames = ImmutableArray.CreateBuilder<FixtureSourceFrame>();
            var folder = Path.Combine(_directory, $"trial-{receipt.Trial}");
            RegularDirectory(folder);
            foreach (var image in receipt.Images)
            {
                image.Validate();
                var originalPath = Path.Combine(folder, image.File);
                SyntheticSearchSourceProtocol.Require(committed.Add(originalPath));
                var original = ReadBounded(originalPath, 512 * 1024);
                SyntheticSearchSourceProtocol.Require(original.Length == image.Bytes && TraversalArchive.Hash(original) == image.Sha256);
                frames.Add(new(image.SourceSequence, image.File, image.PresentationUnixMilliseconds, image.Sha256, original));
                result.Add(new(receipt.Trial, receipt.DocumentId, receipt.CaptureEpoch, image));
                bytes += image.Bytes;
                SyntheticSearchSourceProtocol.Require(bytes <= SyntheticSearchSourceProtocol.MaximumOriginalBytes
                    && result.Count <= SyntheticSearchSourceProtocol.MaximumFrames);
            }
            var batch = new SyntheticSearchSourceBatch(SyntheticSearchSourceProtocol.Schema, _start.ExecutionId, receipt.Trial, receipt.Sequence,
                receipt.DocumentId, receipt.CaptureEpoch, frames.ToImmutable());
            SyntheticSearchSourceProtocol.Validate(batch, _start.ExecutionId, receipt.Sequence);
            SyntheticSearchSourceProtocol.Require(documents.TryGetValue(batch.Trial, out var document) ? document == batch.DocumentId
                : !documents.Values.Contains(batch.DocumentId, StringComparer.Ordinal));
            documents.TryAdd(batch.Trial, batch.DocumentId);
            SyntheticSearchSourceProtocol.Require(TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(batch, ReaderWorkerWire.Json)) == receipt.WireSha256);
        }
        var uncommitted = Directory.EnumerateFileSystemEntries(_directory, "pending-sources-*").Any();
        foreach (var trial in Enumerable.Range(1, 5))
        {
            var folder = Path.Combine(_directory, $"trial-{trial}");
            if (!Directory.Exists(folder)) continue;
            RegularDirectory(folder);
            var originals = Directory.EnumerateFiles(folder).Take(251).ToArray();
            SyntheticSearchSourceProtocol.Require(originals.Length <= 250);
            uncommitted |= originals.Any(path => !committed.Contains(path));
        }
        return new(_start, result.ToImmutable(), bytes, uncommitted);
    }

    public static SyntheticSearchRetainedSources ReadRetainedSources(string directory, SyntheticSearchTaskStart expected)
    {
        expected.Validate();
        return new SyntheticSearchTaskGrants(expected, Path.GetFullPath(directory), TimeProvider.System).ReadRetainedSources();
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        var file = new FileInfo(path);
        SyntheticSearchSourceProtocol.Require(file.Exists && file.LinkTarget is null && (file.Attributes & FileAttributes.ReparsePoint) == 0
            && file.Length is > 0 && file.Length <= maximum);
        using var input = file.OpenRead();
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = input.Read(buffer);
            if (count == 0) break;
            SyntheticSearchSourceProtocol.Require(output.Length + count <= maximum);
            output.Write(buffer, 0, count);
        }
        SyntheticSearchSourceProtocol.Require(output.Length == file.Length);
        return output.ToArray();
    }
}
