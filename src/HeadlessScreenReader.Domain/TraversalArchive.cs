using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Caller-retained archive identity. Possession does not grant tenant access.</summary>
public sealed record TraversalArchiveIdentity(Guid OrganizationId, Guid ArchiveId, string IndexSha256);
public sealed record TraversalArchivePart(int Bytes, string Sha256);
public sealed record TraversalArchiveFile(string Path, long Bytes, string Sha256, ImmutableArray<TraversalArchivePart> Parts);
/// <summary>Original files remain unchanged, including the source package's collection and uncertainty facts.</summary>
public sealed record TraversalArchiveIndex(string Schema, Guid OrganizationId, Guid ArchiveId,
    string SourceManifestSha256, ImmutableArray<TraversalArchiveFile> Files);
/// <summary>Readback at finalization is separate from future availability, custody or accessibility acceptance.</summary>
public sealed record TraversalArchiveReceipt(TraversalArchiveIdentity Identity, string SourceManifestSha256,
    int Files, long Bytes, bool ObjectReadbackVerified, bool IndexReadbackVerified);
public sealed record TraversalArchiveFilePage(TraversalArchiveIdentity Identity, string Path, long FileBytes,
    string FileSha256, long Offset, byte[] Data, long? NextOffset);
public sealed record TraversalArchiveVerification(TraversalArchiveIdentity Identity, int Files, long Bytes, bool IntegrityMatches);
/// <summary>Complete original hash verification plus a bounded format prefix; not an authenticity or custody attestation.</summary>
public sealed record TraversalArchiveOriginalVerification(string Path, long Bytes, string Sha256, ImmutableArray<byte> Header);
