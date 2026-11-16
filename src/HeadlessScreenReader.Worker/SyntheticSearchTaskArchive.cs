using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record SearchTaskArchivePart(int Number, int Bytes, string Sha256);
public sealed record SearchTaskArchiveIndex(string Schema, SyntheticSearchTaskStart Start, string ArchiveSha256,
    int ArchiveBytes, string ManifestSha256, ImmutableArray<SearchTaskArchivePart> Parts);
public sealed record SearchTaskStoredOriginals(SyntheticSearchTaskStart Start, SyntheticSearchTaskPublication Publication,
    SyntheticSearchTaskManifest Manifest, ImmutableDictionary<string, byte[]> Files)
{
    internal byte[] Archive { get; init; } = [];
    internal byte[] IndexBytes { get; init; } = [];
}

/// <summary>Own logical schema over existing bounded private object storage. No browser or input replay authority.</summary>
public sealed class SyntheticSearchTaskArchive(ITraversalObjectStore local, ITraversalObjectStore? remote = null)
{
    private const string Schema = "hsr-search-task-archive-v1";
    private const int ParallelParts = 4;

    public async Task<SyntheticSearchTaskPublication> PrepareAsync(IsolatedSearchTaskResult result,
        Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        if (!result.ContainerAbsenceConfirmed) throw new BrowserOperationException("SEARCH_TASK_CLEANUP_UNCONFIRMED");
        var bundle = SyntheticSearchTaskBundle.Decode(result.Bundle.ToArray(), result.Start);
        var files = bundle.ReadVerifiedOriginals(result.Start);
        var manifestBytes = files[SyntheticSearchTaskManifest.ManifestFile];
        var manifest = JsonSerializer.Deserialize<SyntheticSearchTaskManifest>(manifestBytes, ReaderWorkerWire.Json)!;
        var parts = Split(bundle.Originals);
        var index = new SearchTaskArchiveIndex(Schema, result.Start, bundle.Receipt.ArchiveSha256, bundle.Originals.Length,
            bundle.Receipt.ManifestSha256, [.. parts.Select((bytes, number) => new SearchTaskArchivePart(number, bytes.Length, TraversalArchive.Hash(bytes)))]);
        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(index, ReaderWorkerWire.Json);
        var prepared = new SyntheticSearchTaskPublication(SyntheticSearchTaskPublication.CurrentSchema, result.Start.ExecutionId,
            index.ManifestSha256, manifestBytes.Length, index.ArchiveSha256, index.ArchiveBytes, manifest.Files.Length,
            manifest.Files.Sum(file => file.Bytes), manifest.Observation, true, false, TraversalArchive.Hash(indexBytes));
        prepared.ValidatePrepared();
        await WriteAsync(local, result.Start.Job, prepared, indexBytes, index, parts, authorize, token).ConfigureAwait(false);
        _ = await ReadCoreAsync(local, result.Start.Job, result.Start.WorkerId, prepared, token).ConfigureAwait(false);
        await RequireAuthority(authorize, token).ConfigureAwait(false);
        return prepared;
    }

    public async Task<SyntheticSearchTaskPublication> PublishAsync(SyntheticSearchTaskJob job, Guid producer,
        SyntheticSearchTaskPublication prepared, Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        prepared.ValidatePrepared();
        await RequireAuthority(authorize, token).ConfigureAwait(false);
        var retained = await ReadCoreAsync(local, job, producer, prepared, token).ConfigureAwait(false);
        if (remote is not null)
        {
            var indexBytes = retained.IndexBytes;
            var index = Read<SearchTaskArchiveIndex>(indexBytes);
            // Copy only the already verified original, never start a new browser to reconstruct missing media.
            var parts = Split(retained.Archive);
            await WriteAsync(remote, job, prepared, indexBytes, index, parts, authorize, token).ConfigureAwait(false);
            _ = await ReadCoreAsync(remote, job, producer, prepared, token).ConfigureAwait(false);
        }
        await RequireAuthority(authorize, token).ConfigureAwait(false);
        return retained.Publication with { OriginalsVerified = true };
    }

    public Task<SearchTaskStoredOriginals> ReadAsync(SyntheticSearchTaskJob job, Guid producer,
        SyntheticSearchTaskPublication publication, CancellationToken token)
    {
        publication.Validate();
        return ReadCoreAsync(remote ?? local, job, producer, publication, token);
    }

    private static async Task WriteAsync(ITraversalObjectStore store, SyntheticSearchTaskJob job, SyntheticSearchTaskPublication publication,
        byte[] indexBytes, SearchTaskArchiveIndex index, ImmutableArray<byte[]> parts,
        Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        var identity = Identity(job, publication);
        for (var offset = 0; offset < parts.Length; offset += ParallelParts)
        {
            await RequireAuthority(authorize, token).ConfigureAwait(false);
            await Task.WhenAll(Enumerable.Range(offset, Math.Min(ParallelParts, parts.Length - offset)).Select(async number =>
            {
                await store.WriteAsync(identity, index.Parts[number].Sha256, parts[number], token).ConfigureAwait(false);
                var actual = await store.ReadAsync(identity, index.Parts[number].Sha256, token).ConfigureAwait(false);
                if (actual is null || !actual.AsSpan().SequenceEqual(parts[number])) throw Invalid();
            })).ConfigureAwait(false);
        }
        await RequireAuthority(authorize, token).ConfigureAwait(false);
        await store.WriteAsync(identity, null, indexBytes, token).ConfigureAwait(false);
        var actualIndex = await store.ReadAsync(identity, null, token).ConfigureAwait(false);
        if (actualIndex is null || !actualIndex.AsSpan().SequenceEqual(indexBytes)) throw Invalid();
    }

    private static async Task<SearchTaskStoredOriginals> ReadCoreAsync(ITraversalObjectStore store, SyntheticSearchTaskJob job,
        Guid producer, SyntheticSearchTaskPublication publication, CancellationToken token)
    {
        job.Validate();
        if (producer == Guid.Empty) throw Invalid();
        if (publication.OriginalsVerified) publication.Validate(); else publication.ValidatePrepared();
        var identity = Identity(job, publication);
        var bytes = await store.ReadAsync(identity, null, token).ConfigureAwait(false) ?? throw Invalid();
        if (bytes.Length > 16384 || TraversalArchive.Hash(bytes) != publication.IndexSha256) throw Invalid();
        var index = Read<SearchTaskArchiveIndex>(bytes);
        var expected = new SyntheticSearchTaskStart(SyntheticSearchTaskControl.Schema, job, producer, publication.ExecutionId);
        expected.Validate();
        if (index.Schema != Schema || index.Start != expected || index.ArchiveSha256 != publication.ArchiveSha256
            || index.ArchiveBytes != publication.ArchiveBytes || index.ManifestSha256 != publication.ManifestSha256
            || index.Parts.IsDefaultOrEmpty || index.Parts.Length != (index.ArchiveBytes + TraversalArchive.ObjectBytes - 1) / TraversalArchive.ObjectBytes)
            throw Invalid();
        for (var number = 0; number < index.Parts.Length; number++)
            if (index.Parts[number] is not { } part || part.Number != number || !TraversalArchive.IsHash(part.Sha256)
                || part.Bytes != Math.Min(TraversalArchive.ObjectBytes, index.ArchiveBytes - number * TraversalArchive.ObjectBytes)) throw Invalid();
        var parts = await ReadPartsAsync(store, identity, index, token).ConfigureAwait(false);
        var archive = new byte[index.ArchiveBytes]; var position = 0;
        foreach (var part in parts) { part.CopyTo(archive, position); position += part.Length; }
        if (position != archive.Length || TraversalArchive.Hash(archive) != publication.ArchiveSha256) throw Invalid();
        var bundle = new SyntheticSearchTaskBundle(new(SyntheticSearchTaskCollectionReceipt.CurrentSchema, expected,
            publication.ManifestSha256, publication.ArchiveSha256, 85), archive);
        var files = bundle.ReadVerifiedOriginals(expected);
        var manifestBytes = files[SyntheticSearchTaskManifest.ManifestFile];
        var manifest = Read<SyntheticSearchTaskManifest>(manifestBytes);
        if (manifestBytes.Length != publication.ManifestBytes || manifest.Files.Length != publication.Files
            || manifest.Files.Sum(file => file.Bytes) != publication.Bytes || manifest.Observation != publication.Observation) throw Invalid();
        return new(expected, publication, manifest, files) { Archive = archive, IndexBytes = bytes };
    }

    private static async Task<ImmutableArray<byte[]>> ReadPartsAsync(ITraversalObjectStore store, TraversalArchiveIdentity identity,
        SearchTaskArchiveIndex index, CancellationToken token)
    {
        var parts = new byte[index.Parts.Length][];
        for (var offset = 0; offset < parts.Length; offset += ParallelParts)
            await Task.WhenAll(Enumerable.Range(offset, Math.Min(ParallelParts, parts.Length - offset)).Select(async number =>
            {
                var expected = index.Parts[number];
                var actual = await store.ReadAsync(identity, expected.Sha256, token).ConfigureAwait(false) ?? throw Invalid();
                if (actual.Length != expected.Bytes || TraversalArchive.Hash(actual) != expected.Sha256) throw Invalid();
                parts[number] = actual;
            })).ConfigureAwait(false);
        return [.. parts];
    }

    private static ImmutableArray<byte[]> Split(byte[] archive)
    {
        var parts = ImmutableArray.CreateBuilder<byte[]>();
        for (var offset = 0; offset < archive.Length; offset += TraversalArchive.ObjectBytes)
            parts.Add(archive.AsSpan(offset, Math.Min(TraversalArchive.ObjectBytes, archive.Length - offset)).ToArray());
        return parts.ToImmutable();
    }
    private static TraversalArchiveIdentity Identity(SyntheticSearchTaskJob job, SyntheticSearchTaskPublication publication) =>
        new(job.OrganizationId, job.JobId, publication.IndexSha256);
    private static T Read<T>(byte[] bytes)
    {
        var value = JsonSerializer.Deserialize<T>(bytes, ReaderWorkerWire.Json);
        if (value is null || !JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(bytes), JsonSerializer.SerializeToElement(value, ReaderWorkerWire.Json))) throw Invalid();
        return value;
    }
    private static async Task RequireAuthority(Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("SEARCH_TASK_AUTHORITY_LOST");
    }
    private static BrowserOperationException Invalid() => new("SEARCH_TASK_ARCHIVE_INVALID");
}
