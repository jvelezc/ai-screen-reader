using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Explicit fictional examples for interface exploration. Never actual collected website evidence.</summary>
public static class EvidenceReadinessExamples
{
    public static EvidenceScanRecord Create(string scenario)
    {
        if (scenario is not ("complete" or "gaps" or "none")) throw new ArgumentException("EVIDENCE_EXAMPLE_INVALID");
        const string target = "https://shop.northstar.example/";
        const string url = target + "checkout";
        const string goal = "Complete the checkout task using keyboard navigation";
        var record = new EvidenceScanRecord
        {
            Demo = true,
            Target = target,
            Test = new()
            {
                Url = url,
                Goal = goal,
                ExpectedOutcome = "Checkout confirmation",
                ObservedOutcome = "Keyboard checkout blocked",
                KeyboardOutcome = "blocked",
                PointerOutcome = "completed",
                Repetitions = 2,
                Failures = 2,
                Mode = "ax-model",
                Completion = "completed"
            },
            Environment = new() { CapturedAt = "2026-10-26T18:42:11Z", Browser = "Chromium (illustrative)", Os = "Linux (illustrative)", Locale = "en-US", Viewport = "1440 × 900" },
            Instrumentation = new(true, true, true),
            Findings = [new() { Id = "F-01", Title = "Keyboard activation does not complete checkout", Location = "/checkout", Material = true,
                TestedTaskBlocked = true, Wcag = "2.1.1", Impact = "The recorded keyboard task fails while the pointer comparator succeeds.",
                Expected = "Checkout confirmation", Observed = "No confirmation", EventRef = "EV-02" }],
            Events =
            [
                new() { Id = "EV-01", Time = "00:00", Title = "Checkout loaded", Focus = "document", Ax = "Checkout heading", Result = "Page available", Tone = "pass", FailureKind = "none", Note = "Fictional example. AX text is not actual screen-reader speech." },
                new() { Id = "EV-02", Time = "00:12", Title = "First keyboard attempt blocked", Key = "Enter", Focus = "button#place-order", Ax = "button, name empty", Result = "No confirmation", Tone = "fail", InputStatus = "completed", FailureKind = "accessibility", AttemptId = "K-01", Note = "Illustrative completed input; the expected confirmation was absent." },
                new() { Id = "EV-03", Time = "00:36", Title = "Repeated keyboard attempt blocked", Key = "Space", Focus = "button#place-order", Ax = "button, name empty", Result = "No confirmation", Tone = "fail", InputStatus = "completed", FailureKind = "accessibility", AttemptId = "K-02", Note = "A distinct illustrative attempt of the same task." },
                new() { Id = "EV-04", Time = "00:59", Title = "Pointer comparator completes", Key = "Pointer click", Focus = "button#place-order", Ax = "button, name empty", Result = "Checkout confirmation", Tone = "pass", InputStatus = "completed", FailureKind = "none", AttemptId = "P-01", Note = "Illustrative comparator; no real order was submitted." }
            ],
            Attempts = [new("K-01", "keyboard", "blocked", "EV-02", url, goal), new("K-02", "keyboard", "blocked", "EV-03", url, goal), new("P-01", "pointer", "completed", "EV-04", url, goal)],
            Artifacts =
            [
                new() { Id = "EX-01", Name = "Illustrative session recording", Type = "Video capture", Path = "originals/session.webm", Sha256 = new('a', 64), Original = true, Purpose = "Example reference; no authentic recording is supplied." },
                new() { Id = "EX-02", Name = "Illustrative input and focus log", Type = "Machine event log", Path = "originals/events.json", Sha256 = new('b', 64), Original = true, Purpose = "Example event-log reference only." },
                new() { Id = "EX-03", Name = "Illustrative AX snapshots", Type = "AX snapshot bundle", Path = "originals/ax.json", Sha256 = new('c', 64), Original = true, Purpose = "Example AX reference, not actual NVDA/JAWS/VoiceOver speech." }
            ]
        };
        if (scenario == "gaps") return record with
        {
            Test = record.Test with { Repetitions = 1, Failures = 1, PointerOutcome = "not-tested" },
            Instrumentation = record.Instrumentation with { AccessibilityTreeCapture = false },
            Attempts = [record.Attempts[0]],
            Events = [record.Events[0], record.Events[1]],
            Artifacts = [record.Artifacts[0]]
        };
        if (scenario == "none") return record with
        {
            Test = record.Test with { KeyboardOutcome = "completed", ObservedOutcome = "Checkout task completed", Failures = 0 },
            Findings = [],
            Attempts = [],
            Events = [record.Events[0] with { Title = "Tested task completed", Result = "Checkout confirmation", Tone = "pass" }]
        };
        return record;
    }

    public static ImmutableArray<EvidenceArtifactProof> IllustrativeProofs(EvidenceScanRecord record) =>
        record.Artifacts.Select(a => new EvidenceArtifactProof(a.Id, "Illustrative")).ToImmutableArray();
}

/// <summary>Optional archive identity within the authenticated route's workspace; never an arbitrary storage address.</summary>
public sealed record EvidenceArchiveReference(Guid ArchiveId, string IndexSha256);
/// <summary>Evaluate supplied observations or a named fictional example. A URL alone never starts a fabricated scan.</summary>
public sealed record EvidenceReadinessRequest(string TargetUrl = "", EvidenceScanRecord? Scan = null, string? Example = null,
    EvidenceArchiveReference? Archive = null, string? ScanJson = null);
/// <summary>Stateless decision and its source record. No URL navigation or evidence collection is implied.</summary>
public sealed record EvidenceReadinessResponse(string Schema, Guid OrganizationId, string Source, string Status,
    string TargetUrl, EvidenceAssessment? Assessment, EvidenceScanRecord? Scan,
    ImmutableArray<EvidenceArtifactProof> ArtifactProofs, string Message)
{
    public const string CurrentSchema = "hsr-evidence-readiness-v1";
}
