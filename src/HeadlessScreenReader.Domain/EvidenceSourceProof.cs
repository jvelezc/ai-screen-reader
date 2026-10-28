using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Trusted source-reader facts, separate from uploaded assertions and file hashes.</summary>
public sealed record EvidenceSourceProof(string State, string SubjectKind, FixtureJob? Job, Guid? WorkerId,
    TraversalArchiveIdentity? Archive, string? RunId, string? DocumentId,
    ImmutableArray<string> VerifiedEventIds, ImmutableArray<string> VerifiedAttemptIds,
    ImmutableArray<string> AccessibilityFailureEventIds, bool OutcomeVerified, bool EnvironmentVerified,
    ImmutableArray<string> Issues)
{
    public string Schema => "hsr-recorded-claim-proof-v1";
    public static EvidenceSourceProof Missing => new("Missing", "Unverified", null, null, null, null, null,
        [], [], [], false, false, ["Link the saved test to its admitted recording. Uploaded descriptions and file fingerprints alone do not verify what happened."]);
}

/// <summary>Environment read in the actual credential-free collector, never guessed by an importer.</summary>
public sealed record RecordedCollectorEnvironment(string Browser, string Playwright, string OperatingSystem, string Viewport);
