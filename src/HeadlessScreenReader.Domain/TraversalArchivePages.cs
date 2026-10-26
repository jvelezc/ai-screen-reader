using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Bounded original-file metadata; internal object addresses are not a consumer contract.</summary>
public sealed record TraversalArchiveEntry(string Path, long Bytes, string Sha256);
/// <summary>A hash-fenced inventory page. Metadata availability does not prove original-file availability.</summary>
public sealed record TraversalArchiveManifestPage(TraversalArchiveIdentity Identity, string SourceManifestSha256,
    int TotalFiles, long TotalBytes, int Offset, ImmutableArray<TraversalArchiveEntry> Files, int? NextOffset);
/// <summary>Actual bytes verified by core against the addressed archive part; whole-file verification requires every page.</summary>
public sealed record TraversalArchiveReadPage(TraversalArchiveFilePage Page, string PageSha256);
