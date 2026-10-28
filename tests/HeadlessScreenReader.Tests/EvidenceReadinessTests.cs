using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class EvidenceReadinessTests
{
    internal static EvidenceScanRecord Complete() => EvidenceReadinessExamples.Create("complete") with { Demo = false };
    internal static ImmutableArray<EvidenceArtifactProof> Proofs(EvidenceScanRecord record) =>
        record.Artifacts.Select(a => new EvidenceArtifactProof(a.Id, "HashVerified", a.Sha256)).ToImmutableArray();

    // Isolated policy tests supply a trusted-source seam. HTTP tests separately prove how the host obtains it.
    internal static EvidenceSourceProof Trusted(EvidenceScanRecord record) => new("Verified", record.SubjectKind,
        null, null, null, null, null, record.Events.Select(e => e.Id).ToImmutableArray(),
        record.Attempts.Select(a => a.Id).ToImmutableArray(),
        record.Events.Where(e => e.FailureKind == "accessibility").Select(e => e.Id).ToImmutableArray(), true, true, []);
    private static EvidenceAssessment AssessTrusted(EvidenceScanRecord record, ImmutableArray<EvidenceArtifactProof> proofs = default,
        bool illustrative = false) => EvidenceReadiness.Assess(record, proofs, illustrative, Trusted(record));

    [Fact]
    public void VerifiedFileHashesCannotVerifyUploadedActionsBarrierOrSuccessfulOutcome()
    {
        var blocked = Complete(); var result = EvidenceReadiness.Assess(blocked, Proofs(blocked));
        Assert.Equal("InsufficientEvidence", result.Decision); Assert.False(result.WebsitePackageReady);
        Assert.Equal("Suspected", Assert.Single(result.Findings).Classification);
        Assert.True(result.Conditions[7].Met); Assert.False(result.Conditions[1].Met);
        Assert.False(result.Conditions[3].Met); Assert.False(result.Conditions[6].Met);
        var reached = EvidenceReadinessExamples.Create("none") with { Demo = false };
        Assert.Equal("InsufficientEvidence", EvidenceReadiness.Assess(reached, Proofs(reached)).Decision);
    }

    [Fact]
    public void MismatchedOutcomeScopeAndControlledFixtureCannotAuthorizeWebsiteReadiness()
    {
        var record = Complete(); var proof = Trusted(record);
        Assert.False(EvidenceReadiness.Assess(record, Proofs(record), source: proof with { OutcomeVerified = false }).WebsitePackageReady);
        Assert.False(EvidenceReadiness.Assess(record, Proofs(record), source: proof with { State = "Mismatch" }).WebsitePackageReady);
        Assert.False(EvidenceReadiness.Assess(record, Proofs(record), source: proof with { SubjectKind = "ControlledFixture" }).WebsitePackageReady);
        var fixture = record with { SubjectKind = "ControlledFixture" };
        var result = EvidenceReadiness.Assess(fixture, Proofs(fixture), source: Trusted(fixture));
        Assert.Equal(8, result.ConditionsMet); Assert.Equal("InsufficientEvidence", result.Decision); Assert.False(result.WebsitePackageReady);
    }

    [Theory]
    [InlineData("complete", "Ready", 8)]
    [InlineData("gaps", "InsufficientEvidence", 4)]
    [InlineData("none", "NoBlockingBarrierDocumented", 3)]
    public void ExamplesRemainIllustrativeAndNeverAuthorizeARealWebsitePackage(string scenario, string decision, int met)
    {
        var record = EvidenceReadinessExamples.Create(scenario);
        var result = AssessTrusted(record, EvidenceReadinessExamples.IllustrativeProofs(record), illustrative: true);
        Assert.Equal(decision, result.Decision); Assert.Equal(met, result.ConditionsMet);
        Assert.True(result.Illustrative); Assert.False(result.WebsitePackageReady);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => "G" + i), result.Conditions.Select(c => c.Id));
        Assert.Equal(EvidenceReadiness.OutsideScope, result.OutsideScanScope);
    }

    [Fact]
    public void ReadyRequiresAllEightConditionsAndTrustedOriginalHashProofs()
    {
        var record = Complete(); var result = AssessTrusted(record, Proofs(record));
        Assert.Equal("Ready", result.Decision); Assert.True(result.WebsitePackageReady);
        Assert.Equal(8, result.ConditionsMet); Assert.All(result.Conditions, c => { Assert.True(c.Met); Assert.Empty(c.Missing); });
        var finding = Assert.Single(result.Findings); Assert.Equal("Documented", finding.Classification); Assert.Equal(new[] { "EV-02" }, finding.EventIds.ToArray());
        Assert.Equal(new[] { "EV-02", "EV-03" }, result.Conditions[4].EventIds.ToArray());
        Assert.Equal(new[] { "EV-04" }, result.Conditions[5].EventIds.ToArray());
        Assert.Equal(new[] { "EX-01", "EX-02", "EX-03" }, result.Conditions[7].ArtifactIds.ToArray());
    }

    [Fact]
    public void DeclaredHashesAndBooleanCaptureFlagsCannotVerifyOriginalArtifacts()
    {
        var result = AssessTrusted(Complete());
        Assert.Equal("InsufficientEvidence", result.Decision); Assert.False(result.WebsitePackageReady);
        Assert.False(result.Conditions[7].Met); Assert.Equal(7, result.ConditionsMet);
        Assert.Equal(3, result.Conditions[7].Missing.Length);
        Assert.All(result.Conditions[7].Missing, item => Assert.Contains("Unverified", item));
    }

    [Theory]
    [InlineData("G1")]
    [InlineData("G2")]
    [InlineData("G3")]
    [InlineData("G4")]
    [InlineData("G5")]
    [InlineData("G6")]
    [InlineData("G7")]
    [InlineData("G8")]
    public void EveryRequiredConditionCanIndependentlyPreventReadiness(string condition)
    {
        var record = Complete(); var proofs = Proofs(record);
        record = condition switch
        {
            "G1" => record with { Test = record.Test with { ExpectedOutcome = "" } },
            "G2" => record with { Findings = [record.Findings[0] with { Material = false }] },
            "G3" => record with { Findings = [record.Findings[0] with { EventRef = "missing-event" }] },
            "G4" => record with { Instrumentation = record.Instrumentation with { FocusCapture = false } },
            "G5" => record with { Attempts = [record.Attempts[0], record.Attempts[2]] },
            "G6" => record with { Test = record.Test with { PointerOutcome = "not-tested" } },
            "G7" => record with { Environment = record.Environment with { CapturedAt = "yesterday" } },
            _ => record
        };
        if (condition == "G8") proofs = [];
        var result = AssessTrusted(record, proofs);
        Assert.Equal("InsufficientEvidence", result.Decision); Assert.False(result.WebsitePackageReady);
        Assert.False(result.Conditions.Single(c => c.Id == condition).Met);
        Assert.NotEmpty(result.Conditions.Single(c => c.Id == condition).Missing);
    }

    [Theory]
    [InlineData("engine")]
    [InlineData("policy")]
    [InlineData("unknown")]
    public void EnginePolicyAndUnclassifiedFailuresNeverBecomeDocumentedBarriers(string kind)
    {
        var record = Complete(); record = record with { Events = record.Events.Select(e => e.Tone == "fail" ? e with { FailureKind = kind } : e).ToImmutableArray() };
        var result = AssessTrusted(record, Proofs(record));
        Assert.Equal("InsufficientEvidence", result.Decision); Assert.Equal("Suspected", Assert.Single(result.Findings).Classification);
        Assert.False(result.Conditions[1].Met); Assert.False(result.Conditions[2].Met); Assert.False(result.Conditions[4].Met);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("cancelled")]
    [InlineData("engine-error")]
    [InlineData("policy-denied")]
    [InlineData("unknown")]
    public void IncompleteCollectionCannotBecomeReadyOrNoBarrier(string completion)
    {
        var record = Complete(); record = record with { Test = record.Test with { Completion = completion } };
        Assert.Equal("InsufficientEvidence", AssessTrusted(record, Proofs(record)).Decision);
        var empty = record with { Findings = [], Test = record.Test with { KeyboardOutcome = "completed" } };
        Assert.Equal("InsufficientEvidence", AssessTrusted(empty).Decision);
    }

    [Theory]
    [InlineData("attempted")]
    [InlineData("unknown")]
    public void AttemptedOrUnknownInputIsNotACompletedFailureTrace(string input)
    {
        var record = Complete(); record = record with { Events = record.Events.Select(e => e.Tone == "fail" ? e with { InputStatus = input } : e).ToImmutableArray() };
        var result = AssessTrusted(record, Proofs(record));
        Assert.False(result.Conditions[1].Met); Assert.False(result.Conditions[2].Met);
        Assert.Equal("Suspected", Assert.Single(result.Findings).Classification);
    }

    [Fact]
    public void RepetitionCountsCannotReplaceDistinctSameTaskAttemptEvents()
    {
        var record = Complete(); var countOnly = record with { Attempts = [] };
        Assert.False(AssessTrusted(countOnly).Conditions[4].Met);
        var duplicated = record with { Attempts = [record.Attempts[0], record.Attempts[1] with { EventRef = "EV-02" }, record.Attempts[2]] };
        Assert.False(AssessTrusted(duplicated).Conditions[4].Met);
        var foreign = record with { Attempts = [record.Attempts[0], record.Attempts[1] with { Goal = "A different task" }, record.Attempts[2]] };
        Assert.False(AssessTrusted(foreign).Conditions[4].Met);
    }

    [Fact]
    public void ForeignTaskAndPointerEventsCannotSupportKeyboardBarrierOrComparator()
    {
        var record = Complete(); var foreign = record with { Test = record.Test with { Url = "https://different.example/checkout" } };
        Assert.Equal("Suspected", Assert.Single(AssessTrusted(foreign).Findings).Classification);
        var keyboard = record with { Events = record.Events.Select(e => e.Tone == "fail" ? e with { Key = "Pointer click" } : e).ToImmutableArray() };
        Assert.False(AssessTrusted(keyboard).Conditions[1].Met);
        var comparator = record with { Attempts = record.Attempts.Select(a => a.Mode == "pointer" ? a with { TaskUrl = "https://shop.northstar.example/other" } : a).ToImmutableArray() };
        Assert.False(AssessTrusted(comparator).Conditions[5].Met);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Invalid")]
    [InlineData("Unavailable")]
    [InlineData("Unverified")]
    [InlineData("Illustrative")]
    public void UnverifiedArtifactStatesCannotSatisfyRealPackageIntegrity(string state)
    {
        var record = Complete(); var proofs = Proofs(record).SetItem(0, new(record.Artifacts[0].Id, state, record.Artifacts[0].Sha256));
        Assert.False(AssessTrusted(record, proofs).Conditions[7].Met);
    }

    [Fact]
    public void WrongHashDuplicatePathAndIllustrativeImportsRemainInsufficient()
    {
        var record = Complete(); var wrong = Proofs(record).SetItem(0, new(record.Artifacts[0].Id, "HashVerified", new('d', 64)));
        Assert.False(AssessTrusted(record, wrong).WebsitePackageReady);
        var same = record with { Artifacts = record.Artifacts.Select(a => a with { Path = "one-file.json" }).ToImmutableArray() };
        Assert.False(AssessTrusted(same, Proofs(same)).Conditions[7].Met);
        Assert.False(AssessTrusted(record with { Demo = true }, Proofs(record)).WebsitePackageReady);
    }

    [Fact]
    public void NoBarrierIsLimitedToACompletedRecordedJourneyAndNeverAnEmptyUnknownScan()
    {
        var completed = EvidenceReadinessExamples.Create("none") with { Demo = false };
        var result = AssessTrusted(completed);
        Assert.Equal("NoBlockingBarrierDocumented", result.Decision); Assert.False(result.WebsitePackageReady);
        Assert.Empty(result.Findings); Assert.False(result.Conditions[1].Met);
        var empty = new EvidenceScanRecord { Target = completed.Target };
        Assert.Equal("InsufficientEvidence", AssessTrusted(empty).Decision);
        Assert.Equal(0, AssessTrusted(empty).ConditionsMet);
    }

    [Theory]
    [InlineData("duplicate-event")]
    [InlineData("duplicate-finding")]
    [InlineData("duplicate-attempt")]
    [InlineData("counter")]
    [InlineData("oversize-events")]
    [InlineData("oversize-findings")]
    [InlineData("oversize-text")]
    [InlineData("unsafe-id")]
    [InlineData("unknown-tone")]
    public void MalformedAndOverBudgetRecordsAreRejected(string scenario)
    {
        var record = Complete(); record = scenario switch
        {
            "duplicate-event" => record with { Events = [record.Events[0], record.Events[0]] },
            "duplicate-finding" => record with { Findings = [record.Findings[0], record.Findings[0]] },
            "duplicate-attempt" => record with { Attempts = [record.Attempts[0], record.Attempts[0]] },
            "counter" => record with { Test = record.Test with { Failures = 3 } },
            "oversize-events" => record with { Events = Enumerable.Range(0, 129).Select(i => record.Events[0] with { Id = "E-" + i }).ToImmutableArray() },
            "oversize-findings" => record with { Findings = Enumerable.Range(0, 33).Select(i => record.Findings[0] with { Id = "F-" + i }).ToImmutableArray() },
            "oversize-text" => record with { Test = record.Test with { Goal = new('x', 2049) } },
            "unsafe-id" => record with { Findings = [record.Findings[0] with { Id = "unsafe/id" }] },
            _ => record with { Events = [record.Events[0] with { Tone = "anything" }] }
        };
        Assert.Equal("EVIDENCE_RECORD_INVALID", Assert.Throws<ArgumentException>(() => AssessTrusted(record)).Message);
    }

    [Fact]
    public void ExactCollectionAndTextBoundsRemainAccepted()
    {
        var record = Complete(); record = record with
        {
            Test = record.Test with { ObservedOutcome = new('x', 2048) },
            Findings = Enumerable.Range(0, 32).Select(i => record.Findings[0] with { Id = "F-" + i }).ToImmutableArray(),
            Events = record.Events.AddRange(Enumerable.Range(0, 124).Select(i => new EvidenceEvent { Id = "More-" + i }))
        };
        EvidenceReadiness.Validate(record);
        Assert.Equal(32, AssessTrusted(record).Findings.Length);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@shop.example/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://localhost/")]
    [InlineData("https://shop.example/#token")]
    public void WebsiteIdentityRejectsUnsupportedOrCredentialBearingAddresses(string input)
    { Assert.Throws<ArgumentException>(() => EvidenceReadiness.Website(input)); }
}
