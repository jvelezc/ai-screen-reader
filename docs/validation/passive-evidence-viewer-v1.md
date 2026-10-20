# Passive evidence viewer v1 validation

October 20, 2026, local Windows: **715 discovered/executed/passed, 0 failed/skipped**. All 700 preceding test identities are retained; 15 cases were added. Discovery and TRX identity sets match. Nonincremental Release build: zero warnings/errors. Formatting verification passed. Receipt: artifacts/recording-validation/passive-viewer-windows-final.trx.

The standalone four-route demo passed at artifacts/traversal-recording/0337842e335446908bb65c5bc74cc69a/index.md. Open a route's HTML link to inspect the original recording. Desktop 1365 px, mobile 360 px and narrow 320 px captures had no document overflow; body type remained 16 px. The independent finish review scored the playback-status correction resolved. Documentation preserves the incumbent report style.

| Requirement | Evidence |
| --- | --- |
| Four real routes support pointer and native Tab/Enter selection, actual intervals and timeline links without live input, external requests or media mutation | FourRoutes_OfflineViewerSelectsRealNodesActionsAndEventsWithoutLiveInputOrMediaMutation |
| Native playback status follows play, pause, end and evidence selection | NativePlaybackEvents_KeepStatusConsistentThroughPlayPauseEndAndEvidenceSelection |
| Unknown/outside/partial timing and missing originals preserve uncertainty and classification | MissingOriginalOrUnknownPartialOutsideTiming_RemainsExplicitAndDoesNotInventSeek |
| Collector failure and untrusted markup remain readable and inert in Chromium | FailedCollectorAndInjectedMarkup_KeepReadableEvidenceWithNoScriptExecution |
| Encoded source strings and exact CSP hashes prevent executable interpolation | UntrustedNamesAndEvents_AreEncodedInMarkupAndInertJsonWithExactCspHashes |
| Failed media does not invent playback or goal success | FailedRecording_RendersFailureStageWithoutInventedMediaOrGoal |
| Interrupted input retains uncertainty without an intended result node | InterruptedInput_RetainsUncertaintyAndNeverCreatesTheIntendedResultNode |
| Empty failed evidence displays execution, policy, error and truncation separately | EmptyFailedReceipt_ShowsExecutionPolicyAndMissingEvidenceWithoutInventedStates |
| Oversized display falls back without losing full receipt links | OversizedViewer_FallsBackToBoundedReadableEvidenceWithoutLosingTheReceipt |
| Prior HTML or pending HTML prevents overwrite of a bundle | ExistingViewerOrPendingViewer_PreventsPublishingAnUnrelatedBundle |
| Invalid recording declaration or route is rejected | InvalidRouteOrRecordingDeclaration_IsRejectedBeforeRendering |

The native-media regression initially used two Playwright expression waits that required eval under this strict CSP. Function predicates corrected the test harness; CSP was not weakened. Failed receipts are retained separately.

Remote Windows/Linux CI is tracked on this increment's main push. No local Linux, deployment, fresh NVDA or Claude-client acceptance is claimed. These checks cover this bounded passive viewer, not general accessibility conformance or recording privacy on arbitrary content.
