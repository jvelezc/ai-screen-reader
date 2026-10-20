# Local traversal v1 acceptance

October 20, 2026. Reader profile remains `hsr-desktop-reader-v13`; traversal schema is independently versioned `hsr-traversal-v1`.

The later [directed trace increment](traversal-trace-v2.md) adds SVG state/action diagrams and graph projection `execution-trace-v2`. Counts below record the first traversal increment.

## Behavior and evidence

| Requirement | Evidence |
| --- | --- |
| A verified keyboard route retains immutable source/result and distinct input/goal events | `TraversalRunnerTests.VerifiedRoute_RetainsImmutableSourceResultAndOrderedInputEvidence`; `ChromiumTraversalTests.SuccessfulCommerceRoute_VerifiesActualKeyboardDestinationWithSourceEvidence` |
| Explicit precondition failure sends no input | `MissingPrecondition_PreservesBaselineAndDoesNotRunRoute` |
| Wrong destinations create actual observed edges and stay Unknown | `WrongDestination_ProjectsObservedDialogAndNeverCreatesIntendedGoalNode`; `ValidationDialogRoute_PreservesWrongDestinationRatherThanCheckoutSuccess` |
| An observed disabled route is distinct from policy denial, engine failure and unchanged feedback | `ObservedDisabledControl_IsRouteBarrierWithoutInput`; `InputPolicyDenial_IsSeparateFromWebsiteBarrier`; `BrowserFailure_PreservesInputUncertaintyWithoutReplayOrResultEdge`; `UnchangedCommerceRoute_RecordsCompletedInputWithoutGoalOrBarrierClaim` |
| Command and no-progress bounds stop further input at their boundary | `CommandBudget_ExecutesLastAllowedInstructionAndStopsBeforeNext`; `GoalOnExactCommandBudget_IsVerified`; `NoProgressBudget_StopsAtBoundaryWithoutCallingUnchangedStateBarrier`; `StateChange_ResetsConsecutiveNoProgressCount` |
| Time bounds preserve before/at/after behavior and uncertain input | `DurationJustBeforeDeadline_AllowsObservedGoal`; `DurationBudget_StopsAtExactDeadlineBeforeDispatch`; `DurationDuringInput_PreservesUncertaintyAndStopsBeforeNextCommand` |
| Supported upper bounds and invalid bounds/routes are enforced before work | `MaximumRouteAndBounds_AcceptFiveHundredInstructions`; `InvalidBounds_AreRejectedBeforeBrowserCapture`; `InvalidRouteLength_IsRejectedBeforeCapture`; `NullInstruction_IsRejectedBeforeCapture` |
| Cancellation, unsupported focus and document changes never repair/replay input | `CancellationBeforeOrDuringInput_RetainsReceiptAndNeverRunsNextInstruction`; `FocusRequired_StopsWithoutRepairOrContinuation`; `ChangedDocument_IsObservedButStaleInputIsDenied`; `AdapterInputDenial_StopsAndKeepsPolicySeparateFromReachability` |
| Baseline engine errors have no invented observation | `BaselineBrowserFailure_ReturnsReceiptWithNoInventedObservation` |
| Same-URL states stay distinct and snapshot URL payloads are omitted | `SnapshotUrlIsOmitted_AndSameUrlNeverMergesDistinctObservedStates` |
| Projection rejects inconsistent references; serialized evidence remains usable | `Projection_RejectsMissingAndDuplicateEvidenceReferences`; `Receipt_RoundTripsStructuredEvidenceAndProjectsSameObservedGraph` |
| Passive report links the correct evidence and escapes page text | `Report_LinksEveryObservedEdgeAndEventWithoutEnablingReplay`; `Report_EscapesUntrustedNamesAndNeverCreatesExecutablePageMarkup` |
| File publication preserves originals, cancellation receipts and visible partial writes | `Writer_RetainsJsonGraphAndReadableReportWithoutOverwritingOriginals`; `Writer_PreservesCancelledInputReceiptAfterBrowserCancellation`; `ExistingPendingFile_IsNotOverwrittenOrPublishedAsComplete` |
| Real disabled route requires zero keyboard input | `ChromiumTraversalTests.DisabledCommerceRoute_IsObservedBarrierWithoutAnyBrowserInput` |

## Reproduce

```powershell
dotnet test HeadlessScreenReader.slnx -c Release
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --traversal
```

The CLI must report all four summary flags true and print a unique `artifacts/traversal/<batch>/index.md` path. Each route includes original local JSON and a passive Markdown report. The final JSON snapshot contains observed dialog state for the validation route and has no checkout destination heading; the unchanged route retains distinct observation IDs with equal fingerprints. Synthetic account/password canaries are absent from serialized success evidence.

42 new tests include four real Chromium cases; all 568 prior identities are retained. Local Windows: 610 discovered/executed/passed, zero failed/skipped, including 94 Chromium cases. The full warnings-as-errors build, locked restore, formatting verification and four-route CLI also passed. CI results are reported separately in GitHub Actions; local Linux execution was not repeated for this increment.

The original full milestone is not accepted by these tests: recording and its 20-marker timing measurement, diagnostic hypotheses, replay, capture privacy, human control and durable hosted storage remain open. No fresh NVDA comparison, hosted deployment or customer acceptance is claimed.
