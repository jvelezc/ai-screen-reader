using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalArchiveTests
{
    internal sealed class Objects : ITraversalObjectStore
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public List<string> Writes { get; } = [];
        public Func<TraversalArchiveIdentity, string?, byte[], CancellationToken, Task>? BeforeWrite { get; set; }
        public Func<TraversalArchiveIdentity, string?, CancellationToken, Task<byte[]?>>? ReadOverride { get; set; }
        public async Task WriteAsync(TraversalArchiveIdentity identity, string? part, byte[] bytes, CancellationToken token)
        {
            if (BeforeWrite is not null) await BeforeWrite(identity, part, bytes, token);
            var key = TraversalArchive.ObjectKey(identity, part); Writes.Add(key); Values[key] = bytes.ToArray();
        }
        public Task<byte[]?> ReadAsync(TraversalArchiveIdentity identity, string? part, CancellationToken token) =>
            ReadOverride is not null ? ReadOverride(identity, part, token)
                : Task.FromResult(Values.GetValueOrDefault(TraversalArchive.ObjectKey(identity, part))?.ToArray());
    }
    internal static TraversalArchiveSource Source(string path, byte[] bytes) =>
        new(path, bytes.Length, TraversalArchive.Hash(bytes), () => new MemoryStream(bytes, writable: false));
    internal static readonly byte[] Manifest = "original package manifest"u8.ToArray();
    internal static TraversalArchiveSource[] Sources(byte[]? media = null) =>
        [Source(TraversalArchive.SourceManifest, Manifest), Source("route-1-media/original.webm", media ?? [7, 4, 2]), Source("empty.txt", [])];
    internal static Task<TraversalArchiveReceipt> Finalize(TraversalArchive archive, TraversalArchiveSource[]? sources = null) =>
        archive.FinalizeAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
            TraversalArchive.Hash(Manifest), sources ?? Sources());

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(4194303)]
    [InlineData(4194304)]
    [InlineData(4194305)]
    public async Task FinalizeAndReconnect_PreserveEveryOriginalByteAtPageAndObjectBoundaries(int length)
    {
        var original = new byte[length];
        for (var i = 0; i < length; i++) original[i] = (byte)(i * 13 + 7);
        var objects = new Objects(); var receipt = await Finalize(new(objects), Sources(original));
        Assert.True(receipt.ObjectReadbackVerified); Assert.True(receipt.IndexReadbackVerified);
        Assert.Equal(3, receipt.Files); Assert.Equal(length + Manifest.Length, receipt.Bytes);
        Assert.EndsWith("/index.json", objects.Writes[^1]);
        Assert.All(objects.Values.Values, bytes => Assert.InRange(bytes.Length, 1, 4 * 1024 * 1024));
        var reader = new TraversalArchive(objects); using var restored = new MemoryStream(); long offset = 0;
        while (true)
        {
            var page = await reader.ReadFileAsync(receipt.Identity, "route-1-media/original.webm", offset);
            Assert.Equal(offset, page.Offset); Assert.Equal(length, page.FileBytes);
            Assert.Equal(TraversalArchive.Hash(original), page.FileSha256); Assert.InRange(page.Data.Length, 0, 65536);
            restored.Write(page.Data);
            if (page.NextOffset is not { } next) break;
            Assert.Equal(offset + 65536, next); offset = next;
        }
        Assert.Equal(original, restored.ToArray());
        var proof = await reader.VerifyAsync(receipt.Identity);
        Assert.True(proof.IntegrityMatches); Assert.Equal(receipt.Files, proof.Files); Assert.Equal(receipt.Bytes, proof.Bytes);
    }

    [Fact]
    public async Task ObjectAddressAndIndex_BindOrganizationArchiveAndExternallyRetainedDigest()
    {
        var objects = new Objects(); var archive = new TraversalArchive(objects); var receipt = await Finalize(archive);
        var id = receipt.Identity;
        Assert.Equal($"traversal-v1/{id.OrganizationId:N}/{id.ArchiveId:N}/{id.IndexSha256}/index.json", TraversalArchive.ObjectKey(id, null));
        Assert.Equal(TraversalArchive.Hash(objects.Values[TraversalArchive.ObjectKey(id, null)]), id.IndexSha256);
        foreach (var foreign in new[] { id with { OrganizationId = Guid.NewGuid() }, id with { ArchiveId = Guid.NewGuid() }, id with { IndexSha256 = new('a', 64) } })
            Assert.Equal(TraversalArchiveFailure.Missing, (await Assert.ThrowsAsync<TraversalArchiveException>(() => archive.ReadIndexAsync(foreign))).Failure);
        Assert.Equal(TraversalArchive.Hash(Manifest), (await archive.ReadIndexAsync(id)).SourceManifestSha256);
    }

    [Theory]
    [InlineData("empty-organization")]
    [InlineData("empty-archive")]
    [InlineData("upper-hash")]
    [InlineData("traversal-hash")]
    public void ObjectAddress_RejectsInvalidScopeBeforeAnyTransport(string scenario)
    {
        var id = new TraversalArchiveIdentity(Guid.NewGuid(), Guid.NewGuid(), new('a', 64));
        id = scenario switch
        {
            "empty-organization" => id with { OrganizationId = Guid.Empty },
            "empty-archive" => id with { ArchiveId = Guid.Empty },
            "upper-hash" => id with { IndexSha256 = new('A', 64) },
            _ => id with { IndexSha256 = "../private" }
        };
        Assert.Equal(TraversalArchiveFailure.Invalid, Assert.Throws<TraversalArchiveException>(() => TraversalArchive.ObjectKey(id, null)).Failure);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("absolute")]
    [InlineData("device")]
    [InlineData("duplicate-case")]
    [InlineData("missing-manifest")]
    [InlineData("bad-digest")]
    [InlineData("oversized-file")]
    [InlineData("too-many-files")]
    public async Task Finalize_RejectsInvalidInventoryBeforeSourceOrStorageAccess(string scenario)
    {
        var sources = Sources();
        var path = scenario switch { "parent" => "../outside", "absolute" => "/outside", "device" => "con.txt", _ => sources[1].Path };
        sources[1] = sources[1] with { Path = path };
        if (scenario == "duplicate-case") sources = [.. sources, sources[1] with { Path = sources[1].Path.ToUpperInvariant() }];
        if (scenario == "missing-manifest") sources = sources[1..];
        if (scenario == "bad-digest") sources[1] = sources[1] with { Sha256 = new('z', 64) };
        if (scenario == "oversized-file") sources[1] = sources[1] with { Bytes = 128L * 1024 * 1024 + 1 };
        if (scenario == "too-many-files") sources = [sources[0], .. Enumerable.Range(0, 4097).Select(i => Source($"file-{i}.txt", []))];
        sources = sources.Select(s => s with { Open = () => throw new InvalidOperationException("Source must not open") }).ToArray();
        var objects = new Objects();
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => Finalize(new(objects), sources))).Failure);
        Assert.Empty(objects.Values); Assert.Empty(objects.Writes);
    }

    [Theory]
    [InlineData("short-source")]
    [InlineData("long-source")]
    [InlineData("same-size-mutation")]
    public async Task Finalize_SourceMutationCannotPublishAnIndex(string scenario)
    {
        var sources = Sources(); var opens = 0; var bytes = new byte[] { 7, 4, 2 };
        sources[1] = sources[1] with
        {
            Open = () => new MemoryStream(++opens == 1 ? bytes : scenario switch
            { "short-source" => [7], "long-source" => [7, 4, 2, 0], _ => [7, 4, 3] })
        };
        var objects = new Objects();
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => Finalize(new(objects), sources))).Failure);
        Assert.DoesNotContain(objects.Values.Keys, key => key.EndsWith("/index.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Finalize_InterruptedWriteRetainsUnpublishedPartsAndExplicitRetryReusesIdentity()
    {
        var objects = new Objects(); TraversalArchiveIdentity? attempted = null; var writes = 0;
        objects.BeforeWrite = (id, _, _, _) =>
        {
            attempted = id;
            if (++writes == 2) throw new TraversalArchiveException(TraversalArchiveFailure.WriteUnconfirmed);
            return Task.CompletedTask;
        };
        Assert.Equal(TraversalArchiveFailure.WriteUnconfirmed, (await Assert.ThrowsAsync<TraversalArchiveException>(() => Finalize(new(objects)))).Failure);
        Assert.Single(objects.Values); Assert.DoesNotContain(objects.Values.Keys, key => key.EndsWith("/index.json", StringComparison.Ordinal));
        objects.BeforeWrite = null;
        var receipt = await Finalize(new(objects));
        Assert.Equal(attempted, receipt.Identity); Assert.True((await new TraversalArchive(objects).VerifyAsync(receipt.Identity)).IntegrityMatches);
    }

    [Theory]
    [InlineData("missing-index", TraversalArchiveFailure.Missing)]
    [InlineData("changed-index", TraversalArchiveFailure.Invalid)]
    [InlineData("missing-part", TraversalArchiveFailure.Missing)]
    [InlineData("changed-part", TraversalArchiveFailure.Invalid)]
    public async Task Read_ReportsMissingAndCorruptOriginalsWithoutRepair(string scenario, TraversalArchiveFailure failure)
    {
        var objects = new Objects(); var archive = new TraversalArchive(objects); var receipt = await Finalize(archive);
        var part = TraversalArchive.Hash([7, 4, 2]);
        var key = TraversalArchive.ObjectKey(receipt.Identity, scenario.EndsWith("index", StringComparison.Ordinal) ? null : part);
        if (scenario.StartsWith("missing", StringComparison.Ordinal)) objects.Values.Remove(key); else objects.Values[key] = [9];
        var writes = objects.Writes.Count;
        Assert.Equal(failure, (await Assert.ThrowsAsync<TraversalArchiveException>(() => archive.ReadFileAsync(receipt.Identity, "route-1-media/original.webm", 0))).Failure);
        Assert.Equal(writes, objects.Writes.Count);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("foreign-organization")]
    [InlineData("foreign-archive")]
    [InlineData("source-manifest")]
    [InlineData("part-length")]
    [InlineData("duplicate-path")]
    [InlineData("extra-property")]
    [InlineData("duplicate-property")]
    public async Task ReadIndex_RejectsMalformedMetadataEvenWithAMatchingEnvelopeDigest(string scenario)
    {
        var objects = new Objects(); var archive = new TraversalArchive(objects); var receipt = await Finalize(archive);
        var index = await archive.ReadIndexAsync(receipt.Identity);
        index = scenario switch
        {
            "schema" => index with { Schema = "unknown" },
            "foreign-organization" => index with { OrganizationId = Guid.NewGuid() },
            "foreign-archive" => index with { ArchiveId = Guid.NewGuid() },
            "source-manifest" => index with { SourceManifestSha256 = new('a', 64) },
            "part-length" => index with { Files = index.Files.SetItem(1, index.Files[1] with { Parts = [new(1, new('a', 64))] }) },
            "duplicate-path" => index with { Files = index.Files.Add(index.Files[0]) },
            _ => index
        };
        var json = JsonSerializer.Serialize(index);
        if (scenario == "extra-property") json = json[..^1] + ",\"Unknown\":true}";
        if (scenario == "duplicate-property") json = json[..^1] + ",\"Schema\":\"hsr-traversal-archive-v1\"}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json); var identity = receipt.Identity with { IndexSha256 = TraversalArchive.Hash(bytes) };
        objects.Values[TraversalArchive.ObjectKey(identity, null)] = bytes;
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => archive.ReadIndexAsync(identity))).Failure);
    }

    [Fact]
    public async Task ReadFile_RejectsUnalignedOrOutsideOffsetsAndUnlistedPaths()
    {
        var objects = new Objects(); var archive = new TraversalArchive(objects); var receipt = await Finalize(archive);
        foreach (var offset in new long[] { -1, 1, 4 })
            Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => archive.ReadFileAsync(receipt.Identity, "route-1-media/original.webm", offset))).Failure);
        Assert.Equal(TraversalArchiveFailure.Missing, (await Assert.ThrowsAsync<TraversalArchiveException>(() => archive.ReadFileAsync(receipt.Identity, "unlisted.webm", 0))).Failure);
        var eof = await archive.ReadFileAsync(receipt.Identity, "route-1-media/original.webm", 3);
        Assert.Empty(eof.Data); Assert.Null(eof.NextOffset);
    }

    [Fact]
    public async Task Cancellation_PreventsLateWriteOrReadFromBecomingConfirmed()
    {
        using var stop = new CancellationTokenSource(); var objects = new Objects();
        objects.BeforeWrite = (_, _, _, _) => { stop.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TraversalArchive(objects).FinalizeAsync(Guid.NewGuid(), Guid.NewGuid(),
            TraversalArchive.Hash(Manifest), Sources(), stop.Token));
        Assert.DoesNotContain(objects.Values.Keys, key => key.EndsWith("/index.json", StringComparison.Ordinal));
        objects.BeforeWrite = null; var receipt = await Finalize(new(objects));
        using var readStop = new CancellationTokenSource();
        objects.ReadOverride = (id, part, _) => { readStop.Cancel(); return Task.FromResult<byte[]?>(objects.Values[TraversalArchive.ObjectKey(id, part)]); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TraversalArchive(objects).ReadIndexAsync(receipt.Identity, readStop.Token));
    }

    [Fact]
    public async Task Finalize_AcknowledgementWithoutMatchingReadbackRemainsUnconfirmed()
    {
        var objects = new Objects { ReadOverride = (_, _, _) => Task.FromResult<byte[]?>(null) };
        Assert.Equal(TraversalArchiveFailure.WriteUnconfirmed, (await Assert.ThrowsAsync<TraversalArchiveException>(() => Finalize(new(objects)))).Failure);
        Assert.DoesNotContain(objects.Values.Keys, key => key.EndsWith("/index.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Finalize_TotalCapacityAndOriginalHashFailuresCannotWriteObjects()
    {
        var objects = new Objects(); var sources = Sources();
        sources[1] = sources[1] with { Sha256 = new('a', 64) };
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => Finalize(new(objects), sources))).Failure);
        Assert.Empty(objects.Writes);
        sources = [sources[0], .. Enumerable.Range(0, 5).Select(i => Source($"large-{i}.webm", []) with
        { Bytes = TraversalArchive.MaximumFileBytes, Open = () => throw new InvalidOperationException("Must reject before reading") })];
        Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => Finalize(new(objects), sources))).Failure);
        Assert.Empty(objects.Writes);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ReadIndex_TotalBytesBoundaryIsExactAndDoesNotClaimObjectAvailability(int delta, bool valid)
    {
        var hash = new string('a', 64); var organization = Guid.NewGuid(); var archiveId = Guid.NewGuid();
        var files = ImmutableArray.CreateBuilder<TraversalArchiveFile>();
        files.Add(new(TraversalArchive.SourceManifest, TraversalArchive.ObjectBytes, hash, [new(TraversalArchive.ObjectBytes, hash)]));
        for (var i = 0; i < 4; i++)
        {
            var length = TraversalArchive.MaximumFileBytes + (i == 3 ? delta : 0);
            var parts = Enumerable.Range(0, (int)((length + TraversalArchive.ObjectBytes - 1) / TraversalArchive.ObjectBytes))
                .Select(n => new TraversalArchivePart((int)Math.Min(TraversalArchive.ObjectBytes, length - (long)n * TraversalArchive.ObjectBytes), hash)).ToImmutableArray();
            files.Add(new($"large-{i}.webm", length, hash, parts));
        }
        // One extra byte exceeds the total bound while every individual media file stays within its own limit.
        if (delta > 0)
        {
            files[4] = files[4] with { Bytes = TraversalArchive.MaximumFileBytes, Parts = files[4].Parts.RemoveAt(32) };
            files.Add(new("overflow.txt", 1, hash, [new(1, hash)]));
        }
        var index = new TraversalArchiveIndex(TraversalArchive.Schema, organization, archiveId, hash, files.ToImmutable());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(index); var identity = new TraversalArchiveIdentity(organization, archiveId, TraversalArchive.Hash(bytes));
        var objects = new Objects(); objects.Values[TraversalArchive.ObjectKey(identity, null)] = bytes;
        var reader = new TraversalArchive(objects);
        if (valid)
        {
            var actual = await reader.ReadIndexAsync(identity);
            Assert.Equal(TraversalArchive.MaximumTotalBytes + delta, actual.Files.Sum(f => f.Bytes));
            Assert.Equal(TraversalArchiveFailure.Missing, (await Assert.ThrowsAsync<TraversalArchiveException>(() => reader.ReadFileAsync(identity, "large-0.webm", 0))).Failure);
        }
        else Assert.Equal(TraversalArchiveFailure.Invalid, (await Assert.ThrowsAsync<TraversalArchiveException>(() => reader.ReadIndexAsync(identity))).Failure);
    }

    [Fact]
    public async Task Finalize_AlreadyCancelledSourceCannotBeOpened()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); var objects = new Objects();
        var sources = Sources().Select(s => s with { Open = () => throw new InvalidOperationException("Must not open") }).ToArray();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TraversalArchive(objects).FinalizeAsync(Guid.NewGuid(), Guid.NewGuid(),
            TraversalArchive.Hash(Manifest), sources, stop.Token));
        Assert.Empty(objects.Values);
    }

    [Fact]
    public async Task Finalize_AcceptsExactlyTheMaximumFileInventory()
    {
        var sources = new[] { Sources()[0] }.Concat(Enumerable.Range(0, TraversalArchive.MaximumFiles - 1)
            .Select(i => Source($"file-{i}.txt", []))).ToArray();
        var objects = new Objects(); var reader = new TraversalArchive(objects);
        var receipt = await Finalize(reader, sources);
        Assert.Equal(4097, receipt.Files); Assert.Equal(Manifest.Length, receipt.Bytes);
        Assert.Equal(4097, (await reader.ReadIndexAsync(receipt.Identity)).Files.Length);
        Assert.All(objects.Values.Values, bytes => Assert.InRange(bytes.Length, 1, TraversalArchive.ObjectBytes));
    }
}
