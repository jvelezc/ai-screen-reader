# Owned evidence exclusion v1 validation

October 20, 2026, local Windows: **839 discovered/executed/passed, 0 failed/skipped**. All 807 preceding identities remain; 32 cases were added, including four real Chromium cases. Discovery and TRX identity sets match. Nonincremental Release build had zero warnings/errors; formatting verification passed. Receipt: artifacts/recording-validation/owned-persistence-windows.trx. The standalone demo passed at artifacts/owned-scan/78513398459e43989568bcd1648b796d/index.md with epoch 2, Closed, zero active operations/writes and no fault. The corresponding main CI run is tracked separately. The scope is one owned reader/browser/store pipeline with real files; it does not certify external consumers or hosted persistence.

| Requirement | Evidence |
| --- | --- |
| Intent acknowledgement precedes dispatch; input facts remain separate | IntentIsAcknowledgedBeforeDispatch_AndCurrentEvidenceHasSeparateInputFacts |
| Cancelled waits cannot release actual intent/result/completion writes or acknowledge exclusion | CancelledWaiter_DoesNotReleasePendingOwnedWriteOrAcknowledgeGap |
| Late capture and queued commands cannot persist excluded content or send queued input | LateCaptureAndQueuedCommand_AreDrainedWithoutPersistingPayloadOrSendingQueuedInput |
| Stalled writes fault closed and late acknowledgement cannot reopen | StalledWrite_FaultsExclusionAndLateAcknowledgementCannotReopen |
| Baseline and permit acknowledgement precede resumed admission | Resume_KeepsAdmissionClosedUntilBaselineAndPermitWritesAreAcknowledged |
| Store errors are sanitized and cannot impersonate reader cancellation/exclusion | WriteFailures_AreSanitizedAndKeepTheOwnerClosed; StoreExceptions_CannotMasqueradeAsReaderExclusionOrCallerCancellation |
| Settled post-input cancellation retains uncertainty | PostInputCancellation_PreservesUncertaintyWhenActualOperationAlreadySettled |
| Foreign, stale and consumed leases cannot resume | ForeignStaleAndConsumedLeases_RejectBeforeAdapterResume |
| Duplicate ownership, stale epochs and exhausted command budgets cannot dispatch extra work | DuplicateOwnerStaleEpochAndBudget_CannotDispatchOrPublishExtraIntents |
| Excluded metadata acknowledgement is required for authority | ExcludedJournalAcknowledgement_IsRequiredBeforeLeaseAndWaitCancellationDoesNotImplyIt |
| Failed baseline/permit writes cannot grant new-epoch input | FailedResumeWrites_KeepTheOwnerClosedAndDoNotGrantNewEpochInput |
| Shutdown waits for a timed-out resume's actual writer | TimedOutResumeWriter_RemainsTrackedThroughCloseUntilActualAcknowledgement |
| Actual capture windows use the owner clock; caller arguments stay out of metadata | ActualCaptureWindow_UsesOwnerClockAndCallerArgumentsNeverEnterEvidence |
| Epoch limits and wrong adapter epochs fail closed | EpochLimit_BoundsTransitionsWithoutFabricatingAnExclusionLease; AdapterReturningAStaleEpoch_CannotPersistRawEvidenceOrSendInput |
| Foreign/stale and oversized payloads fail before publication | ForeignOrStaleEvidence_IsRejectedBeforeCreatingPayloadFiles; OversizedOwnedResult_IsRejectedBeforePendingPublication |
| Real visible/password canaries stay out of owned files across same-context resume | AcknowledgedGap_ExcludesVisibleAndPasswordCanariesFromRealOwnedFilesAndResumesSameContext |
| A delayed real-store write must settle before acknowledgement despite waiter cancellation | DelayedRealFileWrite_CannotCrossAcknowledgedGapOrBeReleasedByCancelledWaiter |
| Existing real pending files remain unchanged after failure | ExistingPendingResult_IsPreservedAndFaultsTheRealOwnerWithoutGapAuthority |
| Closing during a real excluded canary writes only metadata and closes Chromium | ClosingDuringExcludedCanary_AddsOnlyMetadataAndClosesTheRealContext |

The delayed-file test defers the actual file-store call and acknowledgement; it does not claim to simulate a kernel-level filesystem stall. Positive controls confirm the synthetic canaries really exist during the excluded interval. File inspection includes pending files, hashes and bytes. Sensitive content is cleared before resume; the close case deliberately closes while its excluded canary remains present.

During development, review found that shutdown needed to track baseline/control writes after a timed-out resume, and that store-thrown cancellation/browser exceptions needed a distinct write-failure wrapper. Focused regression cases now cover both. A cancellation assertion was corrected to allow an already-settled structured result to win over caller cancellation while retaining input uncertainty. No capture policy was weakened.

No local Linux, deployment, fresh NVDA, Claude-client acceptance, human-controller channel, general recording or power-loss durability result is claimed.
