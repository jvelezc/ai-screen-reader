using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public enum TraversalArchiveFailure { Missing, Invalid, Unavailable, WriteUnconfirmed }
public sealed class TraversalArchiveException(TraversalArchiveFailure failure) : Exception("TRAVERSAL_ARCHIVE_" + failure.ToString().ToUpperInvariant())
{
    public TraversalArchiveFailure Failure { get; } = failure;
}

/// <summary>Infrastructure source, opened afresh for each integrity pass; never persisted in the archive.</summary>
public sealed record TraversalArchiveSource(string Path, long Bytes, string Sha256, Func<Stream> Open);

/// <summary>Fixed scoped object addresses, without listing, delete, URL or arbitrary-path authority.</summary>
public interface ITraversalObjectStore
{
    Task<byte[]?> ReadAsync(TraversalArchiveIdentity identity, string? partSha256, CancellationToken token);
    Task WriteAsync(TraversalArchiveIdentity identity, string? partSha256, byte[] bytes, CancellationToken token);
}

/// <summary>Manifest-last publication of original files. Every returned page rechecks the actual index and part.</summary>
public sealed class TraversalArchive(ITraversalObjectStore store)
{
    public const string Schema = "hsr-traversal-archive-v1";
    public const string SourceManifest = "evidence-manifest.json";
    public const int ObjectBytes = 4 * 1024 * 1024;
    public const int PageBytes = 64 * 1024;
    public const int MaximumFiles = 4097;
    public const long MaximumFileBytes = 128L * 1024 * 1024;
    public const long MaximumTotalBytes = 516L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false };

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigitLower);
    public static void ValidateIdentity(TraversalArchiveIdentity identity)
    {
        if (identity is null || identity.OrganizationId == Guid.Empty || identity.ArchiveId == Guid.Empty || !IsHash(identity.IndexSha256))
            throw Invalid();
    }
    public static string ObjectKey(TraversalArchiveIdentity identity, string? partSha256)
    {
        ValidateIdentity(identity);
        if (partSha256 is not null && !IsHash(partSha256)) throw Invalid();
        return $"traversal-v1/{identity.OrganizationId:N}/{identity.ArchiveId:N}/{identity.IndexSha256}/"
            + (partSha256 is null ? "index.json" : "objects/" + partSha256);
    }
    internal static void ValidateObject(TraversalArchiveIdentity identity, string? partSha256, byte[] bytes)
    {
        _ = ObjectKey(identity, partSha256);
        if (bytes.Length is < 1 or > ObjectBytes || Hash(bytes) != (partSha256 ?? identity.IndexSha256)) throw Invalid();
    }
    private static bool SafePath(string? path) => path is { Length: > 0 and <= 240 }
        && path.Split('/') is { Length: <= 8 } parts && parts.All(part => part is not "" and not "." and not ".."
            && Regex.IsMatch(part, "\\A[A-Za-z0-9_-][A-Za-z0-9_.-]*\\z") && !part.EndsWith('.')
            && !Regex.IsMatch(part.Split('.')[0], "\\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\\z", RegexOptions.IgnoreCase));
    private static void ValidateFiles(IEnumerable<(string Path, long Bytes, string Sha256)> files)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in files)
        {
            if (!SafePath(file.Path) || !names.Add(file.Path) || !IsHash(file.Sha256)
                || file.Bytes is < 0 or > MaximumFileBytes || names.Count > MaximumFiles) throw Invalid();
            if (file.Path == SourceManifest && file.Bytes is < 1 or > ObjectBytes) throw Invalid();
            total += file.Bytes;
            if (total > MaximumTotalBytes) throw Invalid();
        }
        if (!names.Contains(SourceManifest)) throw Invalid();
    }

    public async Task<TraversalArchiveReceipt> FinalizeAsync(Guid organization, Guid archive, string sourceManifestSha256,
        IReadOnlyList<TraversalArchiveSource> files, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ValidateIdentity(new(organization, archive, sourceManifestSha256));
        if (files is null || files.Count is < 1 or > MaximumFiles || files.Any(f => f is null || f.Open is null)) throw Invalid();
        // Snapshot the caller's collection before any asynchronous source or transport operation.
        var sources = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        ValidateFiles(sources.Select(f => (f.Path, f.Bytes, f.Sha256)));
        if (sources.SingleOrDefault(f => f.Path == SourceManifest)?.Sha256 != sourceManifestSha256) throw Invalid();
        var inventory = ImmutableArray.CreateBuilder<TraversalArchiveFile>();
        foreach (var source in sources)
            inventory.Add(new(source.Path, source.Bytes, source.Sha256, await ReadSourceAsync(source, null, token).ConfigureAwait(false)));
        var index = new TraversalArchiveIndex(Schema, organization, archive, sourceManifestSha256, inventory.ToImmutable());
        var encoded = JsonSerializer.SerializeToUtf8Bytes(index, Json);
        if (encoded.Length > ObjectBytes) throw Invalid();
        var identity = new TraversalArchiveIdentity(organization, archive, Hash(encoded));
        foreach (var (source, number) in sources.Select((source, i) => (source, i)))
        {
            var partNumber = 0;
            var actual = await ReadSourceAsync(source, async bytes =>
            {
                var expected = index.Files[number].Parts[partNumber++];
                if (bytes.Length != expected.Bytes || Hash(bytes) != expected.Sha256) throw Invalid();
                await PutAndVerifyAsync(identity, expected.Sha256, bytes, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            if (!actual.SequenceEqual(index.Files[number].Parts)) throw Invalid();
        }
        // No published index before all original bytes have independently matched their expected digests.
        await PutAndVerifyAsync(identity, null, encoded, token).ConfigureAwait(false);
        return new(identity, sourceManifestSha256, sources.Length, sources.Sum(f => f.Bytes), true, true);
    }

    private async Task PutAndVerifyAsync(TraversalArchiveIdentity identity, string? part, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await store.WriteAsync(identity, part, bytes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var readback = await store.ReadAsync(identity, part, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (readback is null || !bytes.AsSpan().SequenceEqual(readback)) throw new TraversalArchiveException(TraversalArchiveFailure.WriteUnconfirmed);
    }

    private static async Task<ImmutableArray<TraversalArchivePart>> ReadSourceAsync(TraversalArchiveSource source,
        Func<byte[], Task>? consume, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var input = source.Open();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var parts = ImmutableArray.CreateBuilder<TraversalArchivePart>();
        var buffer = new byte[(int)Math.Min(ObjectBytes, Math.Max(1, source.Bytes))];
        long remaining = source.Bytes;
        while (remaining > 0)
        {
            var length = (int)Math.Min(buffer.Length, remaining); var received = 0;
            while (received < length)
            {
                var read = await input.ReadAsync(buffer.AsMemory(received, length - received), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (read == 0) throw Invalid();
                received += read;
            }
            digest.AppendData(buffer, 0, length);
            var bytes = buffer.AsSpan(0, length).ToArray();
            parts.Add(new(length, Hash(bytes)));
            if (consume is not null) await consume(bytes).ConfigureAwait(false);
            remaining -= length;
        }
        if (await input.ReadAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false) != 0) throw Invalid();
        token.ThrowIfCancellationRequested();
        if (Convert.ToHexStringLower(digest.GetHashAndReset()) != source.Sha256) throw Invalid();
        return parts.ToImmutable();
    }

    public async Task<TraversalArchiveIndex> ReadIndexAsync(TraversalArchiveIdentity identity, CancellationToken token = default)
    {
        ValidateIdentity(identity); token.ThrowIfCancellationRequested();
        var bytes = await store.ReadAsync(identity, null, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (bytes is null) throw new TraversalArchiveException(TraversalArchiveFailure.Missing);
        ValidateObject(identity, null, bytes);
        TraversalArchiveIndex? index;
        try { index = JsonSerializer.Deserialize<TraversalArchiveIndex>(bytes, Json); }
        catch (JsonException) { throw Invalid(); }
        if (index is null || index.Schema != Schema || index.OrganizationId != identity.OrganizationId || index.ArchiveId != identity.ArchiveId
            || !IsHash(index.SourceManifestSha256) || index.Files.IsDefaultOrEmpty || index.Files.Length > MaximumFiles
            || index.Files.Any(f => f is null)) throw Invalid();
        ValidateFiles(index.Files.Select(f => (f.Path, f.Bytes, f.Sha256)));
        if (index.Files.SingleOrDefault(f => f.Path == SourceManifest)?.Sha256 != index.SourceManifestSha256
            || !index.Files.Select(f => f.Path).SequenceEqual(index.Files.Select(f => f.Path).Order(StringComparer.Ordinal))) throw Invalid();
        foreach (var file in index.Files)
        {
            if (file.Parts.IsDefault || file.Parts.Length != (file.Bytes + ObjectBytes - 1) / ObjectBytes) throw Invalid();
            for (var i = 0; i < file.Parts.Length; i++)
                if (file.Parts[i] is not { } part || !IsHash(part.Sha256)
                    || part.Bytes != Math.Min(ObjectBytes, file.Bytes - (long)i * ObjectBytes)) throw Invalid();
            if (file.Bytes == 0 && file.Sha256 != Hash([])) throw Invalid();
        }
        return index;
    }

    public async Task<TraversalArchiveFilePage> ReadFileAsync(TraversalArchiveIdentity identity, string path, long offset,
        CancellationToken token = default)
    {
        if (!SafePath(path) || offset < 0) throw Invalid();
        var index = await ReadIndexAsync(identity, token).ConfigureAwait(false);
        var file = index.Files.SingleOrDefault(f => f.Path == path) ?? throw new TraversalArchiveException(TraversalArchiveFailure.Missing);
        if (offset > file.Bytes || offset != file.Bytes && offset % PageBytes != 0) throw Invalid();
        if (offset == file.Bytes) return new(identity, path, file.Bytes, file.Sha256, offset, [], null);
        var part = file.Parts[(int)(offset / ObjectBytes)];
        var bytes = await store.ReadAsync(identity, part.Sha256, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (bytes is null) throw new TraversalArchiveException(TraversalArchiveFailure.Missing);
        ValidateObject(identity, part.Sha256, bytes);
        if (bytes.Length != part.Bytes) throw Invalid();
        var start = (int)(offset % ObjectBytes); var length = Math.Min(PageBytes, bytes.Length - start);
        var next = offset + length;
        return new(identity, path, file.Bytes, file.Sha256, offset, bytes.AsSpan(start, length).ToArray(), next < file.Bytes ? next : null);
    }

    public async Task<TraversalArchiveOriginalVerification> VerifyOriginalAsync(TraversalArchiveIdentity identity, string path,
        string expectedSha256, long maximumBytes, CancellationToken token = default)
    {
        if (!SafePath(path) || !IsHash(expectedSha256) || maximumBytes is < 1 or > MaximumFileBytes) throw Invalid();
        var index = await ReadIndexAsync(identity, token).ConfigureAwait(false);
        var file = index.Files.SingleOrDefault(f => f.Path == path) ?? throw new TraversalArchiveException(TraversalArchiveFailure.Missing);
        if (file.Bytes < 1 || file.Bytes > maximumBytes || file.Sha256 != expectedSha256) throw Invalid();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ImmutableArray<byte> header = [];
        long total = 0;
        foreach (var part in file.Parts)
        {
            var bytes = await store.ReadAsync(identity, part.Sha256, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (bytes is null) throw new TraversalArchiveException(TraversalArchiveFailure.Missing);
            ValidateObject(identity, part.Sha256, bytes);
            if (bytes.Length != part.Bytes) throw Invalid();
            if (total == 0) header = bytes.AsSpan(0, Math.Min(16, bytes.Length)).ToArray().ToImmutableArray();
            total += bytes.Length;
            digest.AppendData(bytes);
        }
        if (total != file.Bytes || Convert.ToHexStringLower(digest.GetHashAndReset()) != expectedSha256) throw Invalid();
        return new(path, total, expectedSha256, header);
    }

    /// <summary>Bounded original bytes for trusted semantic validation; each object is read once.</summary>
    public async Task<byte[]> ReadOriginalAsync(TraversalArchiveIdentity identity, string path,
        string expectedSha256, long maximumBytes, CancellationToken token = default)
    {
        if (!SafePath(path) || !IsHash(expectedSha256) || maximumBytes is < 1 or > MaximumFileBytes) throw Invalid();
        var index = await ReadIndexAsync(identity, token).ConfigureAwait(false);
        var file = index.Files.SingleOrDefault(f => f.Path == path) ?? throw new TraversalArchiveException(TraversalArchiveFailure.Missing);
        if (file.Bytes < 1 || file.Bytes > maximumBytes || file.Sha256 != expectedSha256) throw Invalid();
        using var output = new MemoryStream(checked((int)file.Bytes));
        foreach (var part in file.Parts)
        {
            var bytes = await store.ReadAsync(identity, part.Sha256, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (bytes is null) throw new TraversalArchiveException(TraversalArchiveFailure.Missing);
            ValidateObject(identity, part.Sha256, bytes);
            if (bytes.Length != part.Bytes || output.Length + bytes.Length > maximumBytes) throw Invalid();
            output.Write(bytes);
        }
        var original = output.ToArray();
        if (original.LongLength != file.Bytes || Hash(original) != expectedSha256) throw Invalid();
        token.ThrowIfCancellationRequested();
        return original;
    }

    public async Task<TraversalArchiveVerification> VerifyAsync(TraversalArchiveIdentity identity, CancellationToken token = default)
    {
        var index = await ReadIndexAsync(identity, token).ConfigureAwait(false);
        foreach (var file in index.Files)
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long offset = 0;
            while (true)
            {
                var page = await ReadFileAsync(identity, file.Path, offset, token).ConfigureAwait(false);
                digest.AppendData(page.Data);
                if (page.NextOffset is not { } next) break;
                offset = next;
            }
            if (Convert.ToHexStringLower(digest.GetHashAndReset()) != file.Sha256) throw Invalid();
        }
        return new(identity, index.Files.Length, index.Files.Sum(f => f.Bytes), true);
    }

    private static TraversalArchiveException Invalid() => new(TraversalArchiveFailure.Invalid);
}
