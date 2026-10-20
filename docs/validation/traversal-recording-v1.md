# Traversal recording evidence v1 validation

October 20, 2026, local Windows: **700 discovered/executed/passed, 0 failed/skipped**. All 665 preceding identities are retained; 35 cases were added. Discovery and TRX identity sets match. Nonincremental Release build: zero warnings/errors. Formatting verification passed. TRX: `artifacts/recording-validation/traversal-recording-windows-clean.trx`. CI is tracked on the corresponding main push. No local Linux, deployment or fresh assistive-technology result is claimed here.

The standalone `--traversal-recording` command passed with four original recordings at `artifacts/traversal-recording/5837199c80064248912f2761ebddcc26/index.md`. All capture, attempt and reader-event intervals were Covered. The routes separately remained Reached, observed disabled barrier with zero keys, wrong-destination Unknown, and unchanged Unknown.

| Requirement | Evidence |
| --- | --- |
| Bracket actual captures on the runner clock | `CaptureWindows_BracketActualAcquisitionOnTheRunnerClock` |
| Independently anchor capture, attempt and reader-event intervals without goal inference | `IndependentAnchor_MapsCaptureAttemptAndReaderOriginIntervalsWithoutChangingGoal` |
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
