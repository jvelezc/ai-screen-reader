using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Private owned directory, exact scoped paths and atomic no-overwrite publication.</summary>
public sealed class FileTraversalObjectStore : ITraversalObjectStore
{
    private readonly string _root;
    public FileTraversalObjectStore(string root)
    {
        _root = Path.GetFullPath(root);
        CheckLinks(_root); Directory.CreateDirectory(_root); CheckLinks(_root);
    }
    private static void CheckLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null
                || Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new TraversalArchiveException(TraversalArchiveFailure.Invalid);
    }
    private string Address(TraversalArchiveIdentity identity, string? part)
    {
        var path = Path.Combine(_root, TraversalArchive.ObjectKey(identity, part).Replace('/', Path.DirectorySeparatorChar));
        CheckLinks(path); return path;
    }
    public async Task<byte[]?> ReadAsync(TraversalArchiveIdentity identity, string? partSha256, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = Address(identity, partSha256);
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length is < 1 or > TraversalArchive.ObjectBytes) throw new TraversalArchiveException(TraversalArchiveFailure.Invalid);
            var bytes = new byte[(int)input.Length];
            await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (await input.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0) throw new TraversalArchiveException(TraversalArchiveFailure.Invalid);
            token.ThrowIfCancellationRequested(); TraversalArchive.ValidateObject(identity, partSha256, bytes);
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (TraversalArchiveException) { throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new TraversalArchiveException(TraversalArchiveFailure.Unavailable); }
    }
    public async Task WriteAsync(TraversalArchiveIdentity identity, string? partSha256, byte[] bytes, CancellationToken token)
    {
        bytes = bytes.ToArray(); TraversalArchive.ValidateObject(identity, partSha256, bytes);
        token.ThrowIfCancellationRequested();
        if (await ReadAsync(identity, partSha256, token).ConfigureAwait(false) is not null) return;
        var path = Address(identity, partSha256); string? pending = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); CheckLinks(path);
            pending = path + ".pending-" + Guid.NewGuid().ToString("N");
            await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                await output.WriteAsync(bytes, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false); output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested(); CheckLinks(path);
            try { File.Move(pending, path); }
            catch (IOException) when (File.Exists(path)) { /* Verify the concurrent publication below; never replace it. */ }
            var actual = await ReadAsync(identity, partSha256, token).ConfigureAwait(false);
            if (actual is null || !bytes.AsSpan().SequenceEqual(actual)) throw new TraversalArchiveException(TraversalArchiveFailure.WriteUnconfirmed);
        }
        catch (TraversalArchiveException) { throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new TraversalArchiveException(TraversalArchiveFailure.WriteUnconfirmed); }
        finally
        {
            if (pending is not null)
                try { File.Delete(pending); }
                catch (IOException) { /* This owned temporary file is not a published object. */ }
                catch (UnauthorizedAccessException) { }
        }
    }
}
