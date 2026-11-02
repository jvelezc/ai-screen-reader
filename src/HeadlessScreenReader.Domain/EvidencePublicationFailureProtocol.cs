namespace HeadlessScreenReader.Domain;

/// <summary>A closed publication diagnostic, without original-file, completion or input authority.</summary>
public enum EvidencePublicationFailureKind { StorageUnavailable, WriteUnconfirmed, ContentInvalid, OriginalMissing }

public static class EvidencePublicationFailureProtocol
{
    public const string StorageUnavailable = "WORKER_PUBLICATION_STORAGE_UNAVAILABLE";
    public const string WriteUnconfirmed = "WORKER_PUBLICATION_WRITE_UNCONFIRMED";
    public const string ContentInvalid = "WORKER_PUBLICATION_CONTENT_INVALID";
    public const string OriginalMissing = "WORKER_PUBLICATION_ORIGINAL_MISSING";

    public static EvidencePublicationFailureKind? Parse(string? code) => code switch
    {
        StorageUnavailable => EvidencePublicationFailureKind.StorageUnavailable,
        WriteUnconfirmed => EvidencePublicationFailureKind.WriteUnconfirmed,
        ContentInvalid => EvidencePublicationFailureKind.ContentInvalid,
        OriginalMissing => EvidencePublicationFailureKind.OriginalMissing,
        _ => null
    };

    public static string EvidenceState(EvidencePublicationFailureKind failure) => failure switch
    {
        EvidencePublicationFailureKind.ContentInvalid => "Invalid",
        EvidencePublicationFailureKind.StorageUnavailable or EvidencePublicationFailureKind.WriteUnconfirmed
            or EvidencePublicationFailureKind.OriginalMissing => "Unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };

    public static string NextAction(EvidencePublicationFailureKind failure) => failure switch
    {
        EvidencePublicationFailureKind.ContentInvalid =>
            "Preserve all originals and conflicting bytes. Inspect this same scan's retained recording if available; resolve integrity before storage-only publication. Do not overwrite originals or resend website input.",
        EvidencePublicationFailureKind.StorageUnavailable or EvidencePublicationFailureKind.WriteUnconfirmed
            or EvidencePublicationFailureKind.OriginalMissing =>
            "Inspect this same scan's retained recording if available. Await verified storage-only recovery under fresh authority; do not resend website input or create a replacement attempt. This diagnostic does not prove that originals are retained or recoverable.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };
}
