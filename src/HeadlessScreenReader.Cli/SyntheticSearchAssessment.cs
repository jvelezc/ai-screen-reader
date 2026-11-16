using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

internal static class SyntheticSearchAssessment
{
    internal const string Incomplete = "IncompleteOrMixedResults";

    internal static bool Closed(CaptureStatus? capture, BrowserShutdownStatus? shutdown) =>
        capture is { Phase: CapturePhase.Closed, EventsTruncated: false }
        && shutdown is
        {
            Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0, ErrorCode: null,
            ProcessExitConfirmed: true, CollectorStopAcknowledged: true
        };

    private static bool OriginalVideo(RecordingProbeMedia? media) => media is { Source.IsDefaultOrEmpty: false, Video.IsDefaultOrEmpty: false, Artifacts.IsDefaultOrEmpty: false }
        && media.Artifacts.Count(a => a.File == "original.webm" && a.Bytes > 0 && a.DecodedFrames == media.Video.Length) == 1;

    internal static bool CompleteKeyboard(RecordedTraversalResult result, SyntheticSearchSite source, string key)
    {
        var receipt = result.Receipt;
        var plan = SyntheticSearchPolicy.KeyboardPlan(key);
        if (result.Environment?.Content != BundledFixtureCatalog.Describe(source.Fixture)
            || result.Recording is not { Stage: "Complete", ErrorCode: null, Media.Source.IsDefaultOrEmpty: false, Media.Video.IsDefaultOrEmpty: false }
            || !Closed(result.Recording.CaptureLifecycle, result.Recording.Shutdown)
            || !OriginalVideo(result.Recording.Media)
            || receipt is not
            {
                Execution: TraversalExecution.Completed, Policy: TraversalPolicy.Allowed,
                HasRecording: true, TimelineTruncated: false, UnexecutedInstructions: 0, ErrorCode: null, AttemptedKeystrokes: 16, CompletedKeystrokes: 16
            }
            || receipt.Attempts.Length != plan.Instructions.Length || receipt.Observations.IsDefaultOrEmpty
            || receipt.Attempts.Any(a => a.Step is null || a.Step.ErrorCode is not null)
            || receipt.Plan.Name != plan.Name || receipt.Plan.Precondition != plan.Precondition || receipt.Plan.Destination != plan.Destination
            || !receipt.Plan.Instructions.SequenceEqual(plan.Instructions)
            || !receipt.Attempts.Select(a => a.Instruction).SequenceEqual(plan.Instructions)
            || !plan.Precondition.IsSatisfiedBy(receipt.Observations[0].Snapshot)
            || plan.Destination.IsSatisfiedBy(receipt.Observations[0].Snapshot)
            || receipt.Observations.Any(o => o.Snapshot.DocumentId != receipt.Observations[0].Snapshot.DocumentId
                || o.Snapshot.CaptureEpoch != receipt.Observations[0].Snapshot.CaptureEpoch)) return false;
        var expected = new[] { "Tab" }.Concat(SyntheticSearchPolicy.Query.Select(c => c.ToString())).Concat(new[] { "Tab", key }).ToArray();
        var graph = TraversalProjection.Build(receipt);
        var entry = receipt.Attempts.Single(a => a.Instruction.Command == ReaderCommand.EnterSearchText).Step;
        return entry is
        {
            ErrorCode: null, InputMayHaveOccurred: false, SearchInput.ValueVerified: true,
            SearchInput.CompletedCharacters: 13, SearchInput.Profile: SyntheticSearchPolicy.Profile, SearchInput.Text: SyntheticSearchPolicy.Query
        }
            && graph.UnresolvedAttempts.IsEmpty && graph.Edges.Length == plan.Instructions.Length
            && graph.Edges.All(e => e.InputEvidenceComplete && e.KeyboardInputs.All(k => k.AttemptEventSequence is not null && k.CompletionEventSequence is not null))
            && receipt.Timeline.Where(e => e.Kind == ReaderEventKind.InputAttempted).Select(e => e.Detail).SequenceEqual(expected)
            && receipt.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail).SequenceEqual(expected);
    }

    internal static string Describe(IReadOnlyList<RecordedTraversalResult> results, SearchPointerRecording? pointer, SyntheticSearchSite source)
    {
        if (results.Count != 3 || results.Where((r, i) => !CompleteKeyboard(r, source, i == 2 ? "Space" : "Enter")).Any()
            || pointer is not
            {
                Stage: "Complete", ErrorCode: null, Media.Source.IsDefaultOrEmpty: false, Media.Video.IsDefaultOrEmpty: false,
                Preparation.ErrorCode: null, Preparation.Focus: { InputAttempted: true, InputCompleted: true, FocusObserved: true, ErrorCode: null, After: not null },
                Preparation.AfterEntry: not null, Preparation.Entry.SearchInput:
                {
                    ValueVerified: true, CompletedCharacters: 13,
                    Profile: SyntheticSearchPolicy.Profile, Text: SyntheticSearchPolicy.Query
                },
                Activation: { Attempted: true, Completed: true, GoalObserved: true, After: not null, ErrorCode: null }
            }
            || pointer.Environment?.Content != BundledFixtureCatalog.Describe(source.Fixture)
            || !Closed(pointer.Capture, pointer.Shutdown) || !OriginalVideo(pointer.Media)) return Incomplete;
        var graph = SyntheticSearchPointerProjection.Build(pointer.Preparation, pointer.Activation);
        if (graph.Edges.Length != 3 || graph.Edges.Any(e => !e.InputEvidenceComplete || e.InputUnconfirmed)
            || !graph.Edges.Select(e => e.CompletedNativeCalls).SequenceEqual(new[] { 1, 13, 1 })
            || !graph.Edges[1].KeyboardInputs.Select(k => k.Key).SequenceEqual(SyntheticSearchPolicy.Query.Select(c => c.ToString()))) return Incomplete;
        var goal = SyntheticSearchPolicy.KeyboardPlan("Enter").Destination;
        var document = pointer.Preparation.Focus.Before.DocumentId;
        if (goal.IsSatisfiedBy(pointer.Preparation.Focus.Before) || goal.IsSatisfiedBy(pointer.Preparation.AfterEntry)
            || goal.IsSatisfiedBy(pointer.Activation.Before) || !goal.IsSatisfiedBy(pointer.Activation.After)
            || graph.Nodes.Any(n => n.Snapshot.DocumentId != document || n.Snapshot.CaptureEpoch != pointer.Preparation.Focus.Before.CaptureEpoch)
            || results.Select(r => r.Receipt!.RunId).Append(pointer.Activation.RunId).Distinct(StringComparer.Ordinal).Count() != 4
            || results.Select(r => r.Receipt!.Observations[0].Snapshot.DocumentId).Append(document).Distinct(StringComparer.Ordinal).Count() != 4)
            return Incomplete;
        if (results.All(r => r.Receipt!.Reachability == TraversalReachability.Reached && goal.IsSatisfiedBy(r.Receipt.Observations[^1].Snapshot)))
            return "AllTestedKeyboardGoalsObserved";
        if (results.All(r => r.Receipt!.Reachability == TraversalReachability.Unknown
            && !r.Receipt.Observations.Any(o => goal.IsSatisfiedBy(o.Snapshot))
            && r.Receipt.Attempts[^1].Step?.Verification == VerificationStatus.TimedOut))
            return "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved";
        return Incomplete;
    }
}
