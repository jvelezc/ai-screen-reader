# Traversal recording evidence v1 validation

October 20, 2026, local Windows: **700 discovered/executed/passed, 0 failed/skipped**. All 665 preceding identities are retained; 35 cases were added. Discovery and TRX identity sets match. Nonincremental Release build: zero warnings/errors. Formatting verification passed. TRX: `artifacts/recording-validation/traversal-recording-windows-clean.trx`. CI is tracked on the corresponding main push. No local Linux, deployment or fresh assistive-technology result is claimed here.

The standalone `--traversal-recording` command passed with four original recordings at `artifacts/traversal-recording/5837199c80064248912f2761ebddcc26/index.md`. All capture, attempt and reader-event intervals were Covered. The routes separately remained Reached, observed disabled barrier with zero keys, wrong-destination Unknown, and unchanged Unknown.

| Requirement | Evidence |
| --- | --- |
| Bracket actual captures on the runner clock | `CaptureWindows_BracketActualAcquisitionOnTheRunnerClock` |
| Independently anchor capture, attempt and reader-event intervals without goal inference | `IndependentAnchor_MapsCaptureAttemptAndReaderOriginIntervalsWithoutChangingGoal` |
| Missing leading source frames use actual overlapping marker/video positions | `MissingLeadingSourceFrames_UseObservedOverlapAndItsVideoPosition` |
| Conflicting later evidence cannot hide behind the first marker | `ConflictingLaterOverlap_CannotHideBehindAnAccurateFirstMarker` |
| Held CFR pixels do not create fictitious source timestamps | `HeldLaterMarker_UsesItsVisibleTransitionRatherThanEncoderPadding` |
| Reappearing markers preserve every possible transition | `ReappearingMarker_RetainsEveryPossibleTransitionInItsUncertainty` |
| Exact and adjacent clip boundaries retain partial/outside/unknown states | `ClipBoundaries_PreservePartialOutsideAndUnknownInsteadOfInventingFrames` |
| Missing acquisition intervals do not invent atomic frames | `MissingCaptureWindow_DoesNotReconstructAnAtomicFrameFromRetentionTime` |
| Invalid event times remain Unknown | `InvalidEventElapsedTime_CannotAcquireAFrameThroughReaderOriginOffset` |
| Missing anchors, foreign document, invalid hash or clock cannot link evidence | `MissingAnchorWrongDocumentHashOrClock_ProducesNoVideoCues` |
| Duplicate matches preserve ambiguity at and above the 250 ms bound | `DuplicateAnchorMatches_RetainAmbiguityAtAndAboveTimingBudget` |
| Invalid frame streams and duplicate evidence IDs fail closed | `ReversedMissingNonfiniteOrOversizeFrames_AndDuplicateEvidenceIdsFailClosed` |
| Four real Chromium routes retain actual classification, keys, decoded originals, hashes and passive links | `FourOriginalRecordings_LinkRealCaptureAndInputIntervalsWithoutChangingRouteClassification` |
| Changed originals are rejected before report publication | `OriginalMediaChangedBeforePublishing_RejectsAttachmentsWithoutPublishingUnrelatedTrace` |
| Cancellation retains failure without invented media/input | `CancellationBeforeLaunch_RetainsFailureAndCreatesNoInputOrRecordingClaim` |
| Recorded fixture has no editable fields or legacy private canaries | `SafeShopFixture_ContainsNoEditableFieldsOrLegacyPrivateCanaries` |
| Foreign run/document/hash binding is rejected | `ForeignRecordingBinding_IsRejectedBeforeAnyReportIsWritten` |
| Missing, empty or escaped artifacts are rejected | `MissingEmptyOrEscapedArtifact_NeverPublishesARecordingLink` |
| Recording declaration requires corresponding finalized media | `RecordingDeclaration_RequiresTheCorrespondingFinalizedMediaManifest` |
| Failed/unknown/partial evidence cannot upgrade the route | `FailedMediaAndUnknownOrPartialTiming_RemainVisibleAndDoNotUpgradeTheRoute` |
| Native dialog close notifications cannot steal a subsequent focus transition | `ClosedDialog_QueuedNotificationCannotStealSubsequentFocus` |

The first CLI run exposed Partial baseline windows on very fast virtual-only routes. Explicit recording padding now surrounds the measured traversal; the uncertainty calculation was not reduced. A report assertion was corrected to expect Markdown-escaped underscores in the visible failure code. The failed artifacts remain separate from accepted runs.

A full run also exposed an existing widget-fixture race: a queued dialog `close` listener redundantly focused its opener after later navigation. A new deterministic Chromium test failed against that listener, then passed when the fixture relied on native synchronous dialog focus restoration. Reader focus checks and keyboard dispatch were unchanged. The red receipt is `artifacts/recording-validation/dialog-focus-race-red.trx`.

A later run overlapped an incorrectly scheduled nonincremental rebuild and encountered Windows binary locks. It is retained as a failed run, not used for acceptance. The clean build, formatting check and accepted full test run were then performed in sequence.

The initial Markdown/SVG media links do not yet constitute an integrated playback viewer. That is the [next increment](../decisions/0022-traversal-recording-evidence.md#next-passive-evidence-viewer).

## October 22: missed first source marker

[CI 39862667359](https://github.com/jvelezc/ai-screen-reader/actions/runs/39862667359)
failed Windows route 3 at source `12cb238`: 1,600 passed, one failed, none skipped;
Ubuntu passed 1,601/1,601. The diagnostic retained first video tick 61 but source
ticks 36-39 and 62 onward, with no tick 61. Strict first-frame-only matching
therefore returned ANCHOR_MISSING even though later independent overlap existed.
The failed run was not deployed. The earlier `58a7c87` run passed all nine jobs;
that did not dismiss this subsequent failure.

The mapper now uses the observed first frame of each marker run and subtracts
its CFR video position from every matching precise source timestamp. Every
possible match contributes to the uncertainty interval. Missing overlap,
conflicting timing and reappearing-marker ambiguity still fail closed; no tick
arithmetic, goal inference, threshold increase or automatic retry was added.
Unchanged video copies do not represent additional acquisitions. The pinned
[Playwright encoder](https://github.com/microsoft/playwright/blob/v1.63.0/packages/playwright-core/src/server/videoRecorder.ts)
uses 25 fps, duplicates frames and extends the last image on shutdown, which is
why counting held copies as new source timestamps incorrectly widened uncertainty.

All six new deterministic cases fail against the preceding implementation. The
repaired focused suite passes 40/40, including the four actual Chromium routes,
original-media hashes, real native cleanup and unchanged route classifications.
The full local Windows suite passed 1,607/1,607 with zero failures/skips, and
format verification passed. Exact-revision replacement Windows/Linux CI and
private deployment remain separate promotion gates.
