# Reader-owned capture exclusion v1 validation

October 20, 2026, local Windows: **731 discovered/executed/passed, 0 failed/skipped**. All 715 preceding test identities are retained; 16 cases were added. Discovery and TRX identity sets match. Nonincremental Release build: zero warnings/errors. Formatting verification passed. Receipt: artifacts/recording-validation/reader-capture-windows.trx. [CI run 39493315500](https://github.com/jvelezc/ai-screen-reader/actions/runs/39493315500) passed all 731 tests on Windows and Ubuntu, recording demos and packaging. Independent Linux isolation/public-reader acceptance also passed.

| Requirement | Evidence |
| --- | --- |
| Whole and queued commands drain; late results and announcements are not retained | WholeCommandAndQueuedCommand_DrainWithoutLateResultsOrRetainedAnnouncements |
| Caller wait cancellation cannot acknowledge exclusion | CancelledWaiter_DoesNotReleaseWholeOperationOrAcknowledgeExclusion |
| Every command, idle entry and timeline read denies while excluded without consuming the command budget | EveryCommandIdleStreamAndTimeline_DenyDuringExclusionWithoutConsumingBudget |
| In-flight idle payload is neither yielded nor retained | InFlightIdleBatch_IsDrainedAndCannotYieldOrRetainLatePayload |
| Polling waits stop and old iterators cannot cross a resumed epoch | IdlePollWaitAndPausedIterator_CannotContinueAcrossExclusion |
| Stalled reader, collector stop or baseline faults closed, including after late completion | StalledReaderStopOrResume_FaultsClosedAndLateCompletionCannotReopen |
| Foreign, stale and consumed reader authority cannot resume | ForeignStaleAndConsumedLeases_CannotResumeTheSession |
| Invalid adapter stop/baseline acknowledgement fails closed | InvalidAdapterAcknowledgementOrBaseline_FailsClosedWithoutPayload |
| Adapter error payload is sanitized; unsupported capture control leaves normal reading available | AdapterFailure_IsSanitizedAndUnsupportedBrowserLeavesOrdinaryReadingAvailable |
| Lifecycle retention is bounded and payload-free | RepeatedExclusions_BoundPayloadFreeLifecycleAndRotateAuthority |
| Disposal closes timeline access and capture authority | DisposedReader_ClosesTimelineAndCaptureAuthority |
| Real visible/password canaries exist during exclusion, remain absent from serialized reader evidence, and same-context resume resets the baseline | WholeReaderExclusion_DeniesEveryEntryPointAndRetainsNoCanaryAcrossSameContextResume |

The real Chromium positive control uses the trusted diagnostic page in memory to prove the excluded canaries exist. It removes them before requesting resume. Only safe post-resume reader evidence is written and read back. This is separate from earlier decoded original-media canary proofs; no new general recording or sensitive entry is enabled.

An initial test build caught an obsolete synchronous xUnit assertion around a Task API; it was corrected to await ThrowsAsync. No production approval or exclusion policy was weakened.

No hosted persistence privacy, deployment, local Linux, fresh assistive-technology comparison or Claude-client acceptance is claimed. External consumers may still hold previously issued immutable evidence; see the [scope boundary](../decisions/0024-reader-capture-exclusion.md#scope-and-remaining-privacy-gate).
