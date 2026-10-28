using System.Collections.Immutable;
using System.Globalization;

namespace HeadlessScreenReader.Domain;

/// <summary>Imported scanner observations, never an authorization or artifact-integrity attestation.</summary>
public sealed record EvidenceScanRecord
{
    public bool Demo { get; init; }
    public string SubjectKind { get; init; } = "Website";
    public string Target { get; init; } = "";
    public EvidenceScanTest Test { get; init; } = new();
    public EvidenceEnvironment Environment { get; init; } = new();
    public EvidenceInstrumentation Instrumentation { get; init; } = new();
    public ImmutableArray<EvidenceFinding> Findings { get; init; } = [];
    public ImmutableArray<EvidenceEvent> Events { get; init; } = [];
    public ImmutableArray<EvidenceArtifact> Artifacts { get; init; } = [];
    public ImmutableArray<EvidenceAttempt> Attempts { get; init; } = [];
}

/// <summary>Task and declared aggregate outcomes. Distinct attempts require their own retained event references.</summary>
public sealed record EvidenceScanTest
{
    public string Url { get; init; } = "";
    public string Goal { get; init; } = "";
    public string ExpectedOutcome { get; init; } = "";
    public string ObservedOutcome { get; init; } = "";
    public string KeyboardOutcome { get; init; } = "unknown";
    public string PointerOutcome { get; init; } = "not-tested";
    public int Repetitions { get; init; }
    public int Failures { get; init; }
    public string Mode { get; init; } = "";
    public string Completion { get; init; } = "unknown";
}

/// <summary>Recorded test context; presence is not proof of freshness or a particular person's encounter.</summary>
public sealed record EvidenceEnvironment
{
    public string CapturedAt { get; init; } = "";
    public string Browser { get; init; } = "";
    public string Os { get; init; } = "";
    public string Locale { get; init; } = "";
    public string Viewport { get; init; } = "";
}

public sealed record EvidenceInstrumentation(bool KeyboardCapture = false, bool FocusCapture = false, bool AccessibilityTreeCapture = false);

/// <summary>A reported finding becomes documented only through a matching retained failure event.</summary>
public sealed record EvidenceFinding
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Location { get; init; } = "";
    public bool Material { get; init; }
    public bool TestedTaskBlocked { get; init; }
    public string Wcag { get; init; } = "";
    public string Impact { get; init; } = "";
    public string RootCause { get; init; } = "";
    public string Expected { get; init; } = "";
    public string Observed { get; init; } = "";
    public string EventRef { get; init; } = "";
}

/// <summary>Recorded input, focus and AX text. AX/model output is not genuine assistive-technology speech.</summary>
public sealed record EvidenceEvent
{
    public string Id { get; init; } = "";
    public string Time { get; init; } = "";
    public string Title { get; init; } = "";
    public string Key { get; init; } = "";
    public string Focus { get; init; } = "";
    public string Ax { get; init; } = "";
    public string Result { get; init; } = "";
    public string Tone { get; init; } = "warn";
    public string Note { get; init; } = "";
    public string InputStatus { get; init; } = "unknown";
    public string FailureKind { get; init; } = "unknown";
    public string AttemptId { get; init; } = "";
}

public sealed record EvidenceArtifact
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Path { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public bool Original { get; init; }
    public string Purpose { get; init; } = "";
}

/// <summary>One separately identified attempt of the same task. Summary counts cannot replace this record.</summary>
public sealed record EvidenceAttempt(string Id, string Mode, string Outcome, string EventRef, string TaskUrl, string Goal);
/// <summary>Supplied by the trusted artifact reader, separate from imported scanner JSON.</summary>
public sealed record EvidenceArtifactProof(string ArtifactId, string State, string? VerifiedSha256 = null);
public sealed record EvidenceCondition(string Id, string Title, bool Met, string Reason,
    ImmutableArray<string> Missing, ImmutableArray<string> EventIds, ImmutableArray<string> ArtifactIds);
public sealed record EvidenceFindingAssessment(string FindingId, string Classification, string Reason, ImmutableArray<string> EventIds);
/// <summary>Operational website-evidence readiness only; neither legal eligibility nor a site-wide accessibility verdict.</summary>
public sealed record EvidenceAssessment(string PolicyVersion, string Decision, string Title, int ConditionsMet,
    bool WebsitePackageReady, bool Illustrative, ImmutableArray<EvidenceCondition> Conditions,
    ImmutableArray<EvidenceFindingAssessment> Findings, ImmutableArray<string> OutsideScanScope);

/// <summary>Pure eight-condition evaluator. No browser, storage, human-review queue or legal determination.</summary>
public static class EvidenceReadiness
{
    public const string PolicyVersion = "hsr-website-evidence-v2";
    public const int MaximumInputBytes = 65_536;
    public static readonly ImmutableArray<string> OutsideScope =
        ["A particular plaintiff's disability", "A particular plaintiff's personal injury or actual encounter",
        "Standing to sue", "Legal coverage, jurisdiction, liability and remedies", "Judicial admissibility or chain-of-custody attestation"];
    public static readonly ImmutableArray<string> RequiredArtifactTypes = ["Video capture", "Machine event log", "AX snapshot bundle"];

    public static string Website(string value)
    {
        if (value is null || value.Length is < 1 or > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.') || uri.Host.EndsWith('.')
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("EVIDENCE_RECORD_INVALID");
        return uri.AbsoluteUri;
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value) && value != "—";
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static void Fields(params string?[] values)
    { if (values.Any(v => v is null || v.Length > 2048 || v.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))) throw new ArgumentException("EVIDENCE_RECORD_INVALID"); }
    private static bool Identifier(string? value) => value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static void Ids(IEnumerable<string> values)
    {
        var items = values.ToArray();
        if (items.Any(id => !Identifier(id)) || items.Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("EVIDENCE_RECORD_INVALID");
    }

    public static void Validate(EvidenceScanRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _ = Website(record.Target);
        if (record.SubjectKind is not ("Website" or "ControlledFixture")) throw new ArgumentException("EVIDENCE_RECORD_INVALID");
        if (record.Test is null || record.Environment is null || record.Instrumentation is null
            || record.Findings.IsDefault || record.Events.IsDefault || record.Artifacts.IsDefault || record.Attempts.IsDefault
            || record.Findings.Length > 32 || record.Events.Length > 128 || record.Artifacts.Length > 32 || record.Attempts.Length > 16
            || record.Findings.Any(f => f is null) || record.Events.Any(e => e is null)
            || record.Artifacts.Any(a => a is null) || record.Attempts.Any(a => a is null)) throw new ArgumentException("EVIDENCE_RECORD_INVALID");
        var test = record.Test;
        Fields(test.Url, test.Goal, test.ExpectedOutcome, test.ObservedOutcome, test.Mode, test.Completion);
        Fields(record.Environment.CapturedAt, record.Environment.Browser, record.Environment.Os, record.Environment.Locale, record.Environment.Viewport);
        if (test.KeyboardOutcome is not ("blocked" or "completed" or "unknown" or "not-tested")
            || test.PointerOutcome is not ("blocked" or "completed" or "unknown" or "not-tested")
            || test.Completion is not ("completed" or "partial" or "cancelled" or "engine-error" or "policy-denied" or "unknown")
            || test.Repetitions is < 0 or > 16 || test.Failures < 0 || test.Failures > test.Repetitions) throw new ArgumentException("EVIDENCE_RECORD_INVALID");
        Ids(record.Findings.Select(f => f.Id)); Ids(record.Events.Select(e => e.Id));
        Ids(record.Artifacts.Select(a => a.Id)); Ids(record.Attempts.Select(a => a.Id));
        foreach (var f in record.Findings) Fields(f.Title, f.Location, f.Wcag, f.Impact, f.RootCause, f.Expected, f.Observed, f.EventRef);
        foreach (var e in record.Events)
        {
            Fields(e.Time, e.Title, e.Key, e.Focus, e.Ax, e.Result, e.Note, e.AttemptId);
            if (e.Tone is not ("pass" or "warn" or "fail") || e.InputStatus is not ("completed" or "attempted" or "unknown")
                || e.FailureKind is not ("accessibility" or "engine" or "policy" or "unknown" or "none")) throw new ArgumentException("EVIDENCE_RECORD_INVALID");
        }
        foreach (var a in record.Artifacts) Fields(a.Name, a.Type, a.Path, a.Sha256, a.Purpose);
        foreach (var a in record.Attempts)
        {
            Fields(a.Mode, a.Outcome, a.EventRef, a.TaskUrl, a.Goal);
            if (a.Mode is not ("keyboard" or "pointer") || a.Outcome is not ("blocked" or "completed" or "unknown")) throw new ArgumentException("EVIDENCE_RECORD_INVALID");
        }
    }

    public static EvidenceAssessment Assess(EvidenceScanRecord record, ImmutableArray<EvidenceArtifactProof> proofs = default,
        bool illustrative = false, EvidenceSourceProof? source = null)
    {
        Validate(record);
        var test = record.Test; var environment = record.Environment;
        source ??= EvidenceSourceProof.Missing;
        var sourceVerified = source.State == "Verified" && source.SubjectKind == record.SubjectKind && source.OutcomeVerified;
        bool RecordedEvent(EvidenceEvent e) => illustrative || sourceVerified && !source.VerifiedEventIds.IsDefault && source.VerifiedEventIds.Contains(e.Id);
        bool RecordedAttempt(EvidenceAttempt a) => illustrative || sourceVerified && !source.VerifiedAttemptIds.IsDefault && source.VerifiedAttemptIds.Contains(a.Id);
        var events = record.Events.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var sameOrigin = Uri.TryCreate(test.Url, UriKind.Absolute, out var testUri)
            && testUri.GetLeftPart(UriPartial.Authority) == new Uri(Website(record.Target)).GetLeftPart(UriPartial.Authority)
            && testUri.UserInfo.Length == 0 && testUri.Fragment.Length == 0;
        bool Failure(EvidenceEvent e) => RecordedEvent(e)
            && (illustrative || !source.AccessibilityFailureEventIds.IsDefault && source.AccessibilityFailureEventIds.Contains(e.Id))
            && e.Tone == "fail" && e.FailureKind == "accessibility" && e.InputStatus == "completed"
            && Present(e.Key) && !e.Key.Contains("pointer", StringComparison.OrdinalIgnoreCase) && !e.Key.Contains("click", StringComparison.OrdinalIgnoreCase)
            && Present(e.Focus) && Present(e.Result) && Present(e.Ax);
        bool SameTask(string url, string goal) => Present(url) && Present(goal)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.AbsoluteUri == testUri?.AbsoluteUri && goal == test.Goal;
        bool Blocked(EvidenceFinding f) => sameOrigin && Present(test.Goal) && test.KeyboardOutcome == "blocked" && test.Completion == "completed"
            && f.Material && f.TestedTaskBlocked && Present(f.Expected) && Present(f.Observed) && f.Expected != f.Observed
            && events.TryGetValue(f.EventRef, out var e) && Failure(e);
        var documented = record.Findings.Where(Blocked).ToArray();
        var candidate = record.Findings.Any(f => f.Material || f.TestedTaskBlocked) || test.KeyboardOutcome == "blocked";
        var linkedFailures = record.Findings.Where(f => events.TryGetValue(f.EventRef, out var e) && Failure(e)).Select(f => f.EventRef).Distinct().ToImmutableArray();
        var failures = record.Attempts.Where(a => RecordedAttempt(a) && a.Mode == "keyboard" && a.Outcome == "blocked" && SameTask(a.TaskUrl, a.Goal)
            && events.TryGetValue(a.EventRef, out var e) && e.AttemptId == a.Id && Failure(e)).ToArray();
        var comparator = record.Attempts.Where(a => RecordedAttempt(a) && a.Mode == "pointer" && a.Outcome == "completed" && SameTask(a.TaskUrl, a.Goal)
            && events.TryGetValue(a.EventRef, out var e) && RecordedEvent(e) && e.AttemptId == a.Id && e.Tone == "pass"
            && e.InputStatus == "completed" && e.FailureKind == "none" && e.Key is "Pointer click" or "Click" && Present(e.Focus) && Present(e.Result)).ToArray();
        var conditions = ImmutableArray.CreateBuilder<EvidenceCondition>();
        void Add(string id, string title, bool met, string reason, string missing,
            IEnumerable<string>? eventIds = null, IEnumerable<string>? artifactIds = null) =>
            conditions.Add(new(id, title, met, met ? reason : missing, met ? [] : [missing],
                (eventIds ?? []).Distinct().ToImmutableArray(), (artifactIds ?? []).Distinct().ToImmutableArray()));
        Add("G1", "Meaningful service journey identified", sameOrigin && Present(test.Goal) && Present(test.ExpectedOutcome),
            "A named task, same-site URL and expected outcome identify the tested journey.", "Record a task, same-site test URL and expected outcome.");
        Add("G2", "Material blocking barrier documented", documented.Length > 0,
            "A material finding links to completed input and an observed accessibility failure in this record.",
            "No material finding is supported by completed input, an accessibility failure and distinct expected/observed outcomes.", documented.Select(f => f.EventRef));
        Add("G3", "Action and failure are linked", linkedFailures.Length > 0,
            "Finding references resolve to actual recorded input, focus, AX and failed-result fields.",
            "Retain a finding-linked failure event with completed input, focused target, AX and observed result; engine/policy errors do not qualify.", linkedFailures);
        var diagnosticEvents = record.Events.Where(e => RecordedEvent(e) && Present(e.Ax) && Present(e.Focus) && e.InputStatus == "completed").Select(e => e.Id).ToArray();
        Add("G4", "Keyboard, focus and AX diagnostics captured", record.Instrumentation.KeyboardCapture && record.Instrumentation.FocusCapture
            && record.Instrumentation.AccessibilityTreeCapture && diagnosticEvents.Length > 0,
            "The record declares all three captures and supplies linked diagnostic events.",
            "Provide keyboard, focus and accessibility-tree captures plus a completed-input diagnostic event.", diagnosticEvents);
        Add("G5", "Barrier reproduced across distinct attempts", failures.Length >= 2 && failures.Select(a => a.EventRef).Distinct().Count() >= 2
            && test.Failures >= failures.Length && test.Repetitions >= failures.Length,
            "At least two separately identified same-task keyboard attempts retain their own failure events.",
            "Provide at least two distinct same-task keyboard attempts and their individual failure events; aggregate counts alone are insufficient.", failures.Select(a => a.EventRef));
        Add("G6", "Alternative interaction compared", documented.Length > 0 && test.PointerOutcome == "completed" && comparator.Length > 0,
            "A separately recorded pointer attempt completes the same task while the documented keyboard route fails.",
            "Retain a completed-input pointer attempt and observed success for the exact same task and URL.", comparator.Select(a => a.EventRef));
        var timestamp = DateTimeOffset.TryParse(environment.CapturedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && environment.CapturedAt.Contains('T') && (environment.CapturedAt.EndsWith('Z') || environment.CapturedAt.Length >= 6 && environment.CapturedAt[^6] is '+' or '-');
        Add("G7", "Environment and capture context recorded", (illustrative || sourceVerified && source.EnvironmentVerified)
            && timestamp && sameOrigin && Present(environment.Browser) && Present(environment.Os) && Present(test.Mode),
            "A timestamp with offset, URL, browser, operating system and test mode contextualize the record.",
            "Record an ISO timestamp with offset, same-site URL, browser, operating system and test mode.");
        var verified = proofs.IsDefault ? [] : proofs;
        bool Original(EvidenceArtifact a) => a.Original && Present(a.Path) && Hash(a.Sha256)
            && verified.Any(p => p.ArtifactId == a.Id && (illustrative ? p.State == "Illustrative" : p.State == "HashVerified"
                && p.VerifiedSha256 == a.Sha256.ToLowerInvariant()));
        var originals = RequiredArtifactTypes.Select(type => record.Artifacts.FirstOrDefault(a => a.Type == type && Original(a))).ToArray();
        var sourceReady = originals.All(a => a is not null) && originals.Select(a => a!.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3
            && (illustrative || !record.Demo);
        Add("G8", "Original artifacts and integrity verified", sourceReady,
            illustrative ? "Illustrative artifact references only; this example supplies no authentic evidence."
            : "The server reconstructed distinct video, event-log and AX originals against their complete retained hashes.",
            "Provide distinct original video, machine event log and AX snapshot files in an authorized archive; metadata or hash declarations alone are not verified files.",
            artifactIds: record.Artifacts.Where(a => RequiredArtifactTypes.Contains(a.Type)).Select(a => a.Id));
        if (!sourceReady)
        {
            var missing = RequiredArtifactTypes.Where((_, index) => originals[index] is null).Select(type =>
            {
                var artifact = record.Artifacts.FirstOrDefault(a => a.Type == type);
                var state = artifact is null ? "missing reference" : verified.FirstOrDefault(p => p.ArtifactId == artifact.Id)?.State ?? "Unverified";
                return $"{type}: {state}; provide an original file with a matching server-verified SHA-256.";
            }).ToImmutableArray();
            if (originals.All(a => a is not null)) missing = [record.Demo ? "Imported example records cannot authorize a real evidence package." : "The three required originals must have distinct paths."];
            conditions[^1] = conditions[^1] with { Missing = missing };
        }
        var count = conditions.Count(c => c.Met);
        var noBarrier = (illustrative || sourceVerified && source.OutcomeVerified) && !candidate && test.Completion == "completed" && test.KeyboardOutcome == "completed"
            && conditions[0].Met && conditions[6].Met && record.Events.Any(e => e.Tone == "pass" && Present(e.Result));
        var decision = count == 8 && (illustrative || sourceVerified && record.SubjectKind == "Website")
            ? "Ready" : noBarrier ? "NoBlockingBarrierDocumented" : "InsufficientEvidence";
        return new(PolicyVersion, decision, decision == "Ready" ? "Ready to proceed with the website evidence package"
            : decision == "NoBlockingBarrierDocumented" ? "No blocking barrier documented" : "Insufficient supporting evidence",
            count, decision == "Ready" && !illustrative && !record.Demo && sourceVerified && source.SubjectKind == "Website", illustrative, conditions.ToImmutable(),
            record.Findings.Select(f => new EvidenceFindingAssessment(f.Id, Blocked(f) ? "Documented" : "Suspected",
                Blocked(f) ? "Supported by the linked completed-input accessibility failure in the supplied record."
                    : "This is a reported issue, not a verified blocking barrier. Retain matching recorded actions and independently supported accessibility-failure evidence; engine, policy and timeout states do not establish it.",
                events.ContainsKey(f.EventRef) ? [f.EventRef] : [])).ToImmutableArray(), OutsideScope);
    }
}
