using System.Collections.Immutable;
using System.Globalization;

namespace HeadlessScreenReader.Domain;

/// <summary>Canonical retained facts from one fixed authored run. No inferred barrier, repeat or pointer success.</summary>
public static class RecordedEvidenceProjection
{
    // Reserved identifier for the bundled fixture, never a claimed visited public URL.
    public const string FixtureTarget = "https://synthetic-shop.invalid/";

    public static EvidenceScanRecord Create(AdmittedKeyboardRecording recording, string observationHash, string traversalHash)
    {
        var receipt = recording.Receipt;
        var events = ImmutableArray.CreateBuilder<EvidenceEvent>();
        var taskAttempt = "run-" + receipt.RunId;
        foreach (var attempt in receipt.Attempts)
        {
            if (attempt.Step is not { } step || attempt.ResultObservationId is null) continue;
            var observation = receipt.Observations.Single(o => o.Id == attempt.ResultObservationId);
            var before = receipt.Observations.Single(o => o.Id == attempt.SourceObservationId).Snapshot.EffectiveFocusedNode;
            var focused = observation.Snapshot.EffectiveFocusedNode;
            foreach (var input in attempt.Events.Where(e => e.Kind == ReaderEventKind.InputCompleted))
                events.Add(new()
                {
                    Id = "event-" + input.Sequence.ToString(CultureInfo.InvariantCulture),
                    Time = input.ElapsedMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms",
                    Title = attempt.Instruction.Command.ToString(),
                    Key = input.Detail,
                    Focus = before is null && focused is null ? "" : $"Before: {Focus(before)}; after: {Focus(focused)}",
                    Ax = step.Announcement,
                    Result = step.Outcome + "/" + step.Verification,
                    Tone = step.Verification == VerificationStatus.TimedOut ? "warn" : "pass",
                    InputStatus = "completed",
                    FailureKind = step.ErrorCode is null ? "none" : "engine",
                    AttemptId = taskAttempt,
                    Note = "Retained reader output from page semantics; not actual NVDA, JAWS or VoiceOver speech."
                });
        }
        var keyboard = receipt.Reachability switch { TraversalReachability.Reached => "completed", TraversalReachability.NotReached => "blocked", _ => "unknown" };
        var expected = receipt.Plan.Destination;
        var environment = recording.Environment;
        return new()
        {
            SubjectKind = "ControlledFixture",
            Target = FixtureTarget,
            Test = new()
            {
                Url = FixtureTarget,
                Goal = receipt.Plan.Name,
                ExpectedOutcome = $"Role: {expected.Role}; name: {expected.Name}; focused: {expected.MustBeFocused}",
                ObservedOutcome = receipt.Reachability.ToString(),
                KeyboardOutcome = keyboard,
                PointerOutcome = "not-tested",
                Repetitions = 1,
                Failures = keyboard == "blocked" ? 1 : 0,
                Mode = "Fixed authored keyboard route",
                Completion = receipt.Execution == TraversalExecution.Completed ? "completed" : "partial"
            },
            Environment = new()
            {
                CapturedAt = receipt.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                Browser = environment?.Browser ?? "",
                Os = environment?.OperatingSystem ?? "",
                Viewport = environment?.Viewport ?? ""
            },
            Instrumentation = new(!receipt.TimelineTruncated, true, true),
            Events = events.ToImmutable(),
            // One journey, not one task attempt per reader command. No pointer comparator is fabricated.
            Attempts = [new(taskAttempt, "keyboard", keyboard, events.LastOrDefault()?.Id ?? "", FixtureTarget, receipt.Plan.Name)],
            Findings = [],
            Artifacts =
            [
                new() { Id = "VIDEO", Name = "Original session video", Type = "Video capture", Path = "original.webm", Sha256 = recording.VideoSha256, Original = true },
                new() { Id = "EVENTS", Name = "Actual keyboard path and reader events", Type = "Machine event log", Path = "traversal.json", Sha256 = traversalHash, Original = true },
                new() { Id = "AX", Name = "Recorded page accessibility states", Type = "AX snapshot bundle", Path = "observation.json", Sha256 = observationHash, Original = true }
            ]
        };
    }

    private static string Focus(AccessibleNode? node) => node is null ? "not exposed" : $"{node.Role}: {node.Name} [{node.Id}]";

    public static EvidenceSourceProof Compare(EvidenceScanRecord supplied, EvidenceScanRecord recorded,
        WorkerEvidenceArchive producer, AdmittedKeyboardRecording recording)
        => Compare(supplied, recorded, new("Verified", recorded.SubjectKind, producer.Job, producer.WorkerId,
            producer.Receipt.Identity, recording.Receipt.RunId, recording.Receipt.Observations[0].Snapshot.DocumentId,
            [], [], [], true, recording.Environment is not null, []));

    public static EvidenceSourceProof Compare(EvidenceScanRecord supplied, EvidenceScanRecord recorded, EvidenceSourceProof source)
    {
        var issues = ImmutableArray.CreateBuilder<string>();
        if (supplied.Target != recorded.Target || supplied.SubjectKind != recorded.SubjectKind)
            issues.Add("The test concerns the bundled synthetic shop, not the supplied website. Use a recording of the same target.");
        var outcome = supplied.Test == recorded.Test;
        if (!outcome) issues.Add("The task or its claimed outcome differs from the recorded run. Unknown and timed-out outcomes cannot be changed to success or an accessibility barrier.");
        var context = source.EnvironmentVerified && supplied.Environment == recorded.Environment;
        if (supplied.Environment != recorded.Environment) issues.Add("The claimed test conditions differ from the retained collector context.");
        var known = recorded.Events.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var verified = supplied.Events.Where(e => known.TryGetValue(e.Id, out var original) && e == original).Select(e => e.Id).ToImmutableArray();
        if (!supplied.Events.SequenceEqual(recorded.Events)) issues.Add("The supplied keys, focus, page accessibility data or results differ from the retained event sequence. Review the original events.");
        var attempts = supplied.Attempts.Where(a => recorded.Attempts.Contains(a)).Select(a => a.Id).ToImmutableArray();
        if (!supplied.Attempts.SequenceEqual(recorded.Attempts)) issues.Add("This is one keyboard journey. Additional repeats or a successful mouse comparison require their own recordings.");
        if (supplied.Instrumentation != recorded.Instrumentation) issues.Add("The declared captures differ from the actual recording.");
        if (!supplied.Artifacts.SequenceEqual(recorded.Artifacts)) issues.Add("The listed evidence files do not match this admitted recording's originals.");
        return source with
        {
            State = issues.Count == 0 ? "Verified" : "Mismatch",
            VerifiedEventIds = verified,
            VerifiedAttemptIds = attempts,
            OutcomeVerified = outcome,
            EnvironmentVerified = context,
            AccessibilityFailureEventIds = [],
            Issues = issues.Take(32).ToImmutableArray()
        };
    }
}
