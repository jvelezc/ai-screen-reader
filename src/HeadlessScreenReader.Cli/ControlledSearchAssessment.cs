using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

internal static class ControlledSearchAssessment
{
    internal static bool Complete(RecordedTraversalResult result, SyntheticSearchSite source, int trial)
    {
        if (trial is 3 or 4) return SyntheticSearchAssessment.CompleteKeyboard(result, source, trial == 3 ? "Enter" : "Space");
        if (trial is not (1 or 2)) return false;
        var plan = SyntheticSearchTaskControl.KeyboardPlan(trial);
        var receipt = result.Receipt;
        if (result.Environment?.Content != BundledFixtureCatalog.Describe(source.Fixture)
            || result.Recording is not { Stage: "Complete", ErrorCode: null, Media: not null }
            || !MediaComplete(result.Recording.Media) || !SyntheticSearchAssessment.Closed(result.Recording.CaptureLifecycle, result.Recording.Shutdown)
            || receipt is not
            {
                Execution: TraversalExecution.Completed, Policy: TraversalPolicy.Allowed, HasRecording: true,
                TimelineTruncated: false, UnexecutedInstructions: 0, ErrorCode: null, AttemptedKeystrokes: 15, CompletedKeystrokes: 15
            }
            || receipt.Attempts.Length != 3 || receipt.Observations.IsDefaultOrEmpty
            || receipt.Plan.Name != plan.Name || receipt.Plan.Precondition != plan.Precondition || receipt.Plan.Destination != plan.Destination
            || !receipt.Plan.Instructions.SequenceEqual(plan.Instructions) || !receipt.Attempts.Select(attempt => attempt.Instruction).SequenceEqual(plan.Instructions)
            || receipt.Attempts.Any(attempt => attempt.Step is null || attempt.Step.ErrorCode is not null)
            || !plan.Precondition.IsSatisfiedBy(receipt.Observations[0].Snapshot) || plan.Destination.IsSatisfiedBy(receipt.Observations[0].Snapshot)
            || receipt.Observations.Any(observation => observation.Snapshot.DocumentId != receipt.Observations[0].Snapshot.DocumentId
                || observation.Snapshot.CaptureEpoch != 1)) return false;
        var expected = new[] { "Tab" }.Concat("sample course".Select(character => character.ToString())).Append("Enter");
        var entry = receipt.Attempts[1].Step;
        var graph = TraversalProjection.Build(receipt);
        return entry is
        {
            InputMayHaveOccurred: false, SearchInput:
            {
                ValueVerified: true, CompletedCharacters: 13,
                Profile: SyntheticSearchPolicy.Profile, Text: SyntheticSearchPolicy.Query
            }
        }
            && graph.UnresolvedAttempts.IsEmpty && graph.Edges.Length == 3 && graph.Edges.All(edge => edge.InputEvidenceComplete)
            && receipt.Timeline.Where(item => item.Kind == ReaderEventKind.InputAttempted).Select(item => item.Detail).SequenceEqual(expected)
            && receipt.Timeline.Where(item => item.Kind == ReaderEventKind.InputCompleted).Select(item => item.Detail).SequenceEqual(expected);
    }

    internal static bool PointerComplete(SearchPointerRecording pointer, SyntheticSearchSite source)
    {
        if (pointer is not
            {
                Stage: "Complete", ErrorCode: null, Media: not null,
                Preparation:
                {
                    ErrorCode: null, TimelineTruncated: false, Focus: { InputAttempted: true, InputCompleted: true, FocusObserved: true, ErrorCode: null, After: not null },
                    AfterEntry: not null, Entry:
                    {
                        ErrorCode: null, InputMayHaveOccurred: false, BrowserKeystrokes: 13, SearchInput:
                        {
                            ValueVerified: true, CompletedCharacters: 13,
                            Profile: SyntheticSearchPolicy.Profile, Text: SyntheticSearchPolicy.Query
                        }
                    }
                },
                Activation: { Attempted: true, Completed: true, GoalObserved: true, After: not null, ErrorCode: null }
            }
            || pointer.Environment?.Content != BundledFixtureCatalog.Describe(source.Fixture)
            || !MediaComplete(pointer.Media) || !SyntheticSearchAssessment.Closed(pointer.Capture, pointer.Shutdown)) return false;
        var graph = SyntheticSearchPointerProjection.Build(pointer.Preparation, pointer.Activation);
        var goal = SyntheticSearchTaskControl.KeyboardPlan(1).Destination;
        var document = pointer.Preparation.Focus.Before.DocumentId;
        return graph.Edges.Length == 3 && graph.Edges.All(edge => edge.InputEvidenceComplete && !edge.InputUnconfirmed)
            && graph.Edges.Select(edge => edge.CompletedNativeCalls).SequenceEqual(new[] { 1, 13, 1 })
            && graph.Edges[1].KeyboardInputs.Select(input => input.Key).SequenceEqual("sample course".Select(character => character.ToString()))
            && graph.Nodes.All(node => node.Snapshot.DocumentId == document && node.Snapshot.CaptureEpoch == 1)
            && !goal.IsSatisfiedBy(pointer.Preparation.Focus.Before) && !goal.IsSatisfiedBy(pointer.Preparation.AfterEntry)
            && !goal.IsSatisfiedBy(pointer.Activation.Before) && goal.IsSatisfiedBy(pointer.Activation.After);
    }

    private static bool MediaComplete(RecordingProbeMedia media) => !media.Source.IsDefaultOrEmpty && !media.Video.IsDefaultOrEmpty
        && !media.PreciseFrames.IsDefaultOrEmpty && !media.Artifacts.IsDefaultOrEmpty
        && media.Artifacts.Count(artifact => artifact.File == "original.webm" && artifact.Bytes > 0 && artifact.DecodedFrames == media.Video.Length) == 1;
}
