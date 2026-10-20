# Exclusive local controller v1 validation

October 20, 2026, local Windows: **873 discovered/executed/passed, 0 failed/skipped**. All 839 prior identities remain; 34 cases were added (31 deterministic, three real Chromium/file cases). Discovery and TRX identity sets match. Final nonincremental Release build had zero warnings/errors; formatting verification passed. Receipt: artifacts/recording-validation/controller-windows-final.trx. The standalone demo passed at artifacts/controller/712c7ea738264e04bb0ed8c00970a5a6/index.md: Closed, generation 2, same-context epoch 2, no active commands/writes or fault. This gate covers a bounded local AI controller. Main commit 4838aad passed [CI39507955364](https://github.com/jvelezc/ai-screen-reader/actions/runs/39507955364): Windows and Ubuntu each passed 873 tests with zero failures/skips, all demos/packaging and independent Linux isolation/public-reader acceptance passed. Both downloaded controller receipts report Closed at generation 2 and epoch 2, with zero active commands/writes and no fault.

| Requirement | Evidence |
| --- | --- |
| One lease rejects contenders; generation is persisted without token | ExclusiveLease_RejectsContendersAndPersistsGenerationWithoutBearerToken |
| Foreign token, scan and generation cannot capture, publish intent or input | ForeignAuthority_IsRejectedBeforeIntentCaptureOrInput |
| Invalid lifetime does not consume a generation; exact boundaries expire at deadline | InvalidLeaseLifetime_DoesNotConsumeAGeneration; ExactLifetimeBoundaries_ExpireAtDeadlineAndRejectForgedFutureExpiry |
| Cancelled command/release waits cannot release actual writes or ownership early | ReleaseWaitCancellation_DoesNotGrantReplacementBeforeActualWriteAndCommandDrain |
| A command wait timeout cannot release its actual write or ownership | CommandWaitTimeout_DoesNotReleaseActualWriteOrControllerDrain |
| Expiry drains late capture and rejects queued keys | Expiry_DrainsLateCaptureAndRejectsQueuedInputBeforeNewGeneration |
| An actual native call remains owned until adapter stop acknowledges | ExpiryDuringNativeCall_RetainsActualWorkUntilAdapterStopAcknowledgement |
| Stalled drain faults closed even after late completion | UnconfirmedDrain_FaultsClosedAndLateWriteCannotGrantReplacement |
| Persisted fresh baseline/permit precede replacement authority | ReplacementLease_WaitsForFreshPersistedBaselineBeforeAdmission; FailedReplacementBaseline_NeverIssuesAuthorityAndFaultsClosed |
| Duplicate wrappers and grant limits cannot compete | DuplicateOwnerAndGrantLimit_CannotCreateCompetingNativeAuthority; MaximumGrantBoundary_RemainsBoundedAndNeverReusesGeneration |
| Delayed expiry timer cannot bypass final native monotonic check | DelayedExpiryTimer_CannotBypassMonotonicCheckAtFinalNativeDispatch |
| Wall-clock jumps do not change monotonic expiry | WallClockChanges_DoNotExtendOrPrematurelyExpireMonotonicAuthority |
| Store failure visibly faults the controller | StoreFailure_SurfacesControllerFaultAndKeepsAuthorityClosed |
| Close drains work, is idempotent and cannot grant a pending replacement | Close_DrainsActualCommandRejectsAllAuthorityAndIsIdempotent; CloseDuringReplacementBaseline_CannotReturnALeaseOrDispatchInput |
| Unconfirmed close never claims Closed after late write completion | UnconfirmedClose_StaysFaultedAfterLateWriteInsteadOfClaimingClosed |
| Invalid construction cannot poison ownership | InvalidGrantBounds_AreRejectedBeforeOwningAnAdapter; FailedConstructor_DoesNotPoisonOwnershipRegistry |
| Pre-admission cancellation does no work | CancelledBeforeAdmission_PublishesNoIntentAndSendsNoInput |
| Native failure retains uncertainty; policy denial remains distinct | NativeFailure_PreservesInputUncertaintyAndNeverInventsDestinationSuccess; DeniedReaderKey_StaysPolicyDenialWithoutNativeInputOrControllerFailure |
| Real controllers retain native focus, same document, fresh epoch and immutable old files | SequentialControllers_UseNativeFocusAndFreshEpochWithoutMutatingHistoricalFiles |
| Real listbox prefix input is brokered; stale authority cannot type | ListboxCharacters_AreBrokeredAndStaleLeaseCannotSendAnotherPrefix |
| A real pending intent write prevents transfer after caller cancellation | RealIntentDelay_CancelledWaitCannotReleaseAuthorityOrSendOldInput |

The deterministic final-dispatch test deliberately suppresses timer callbacks, lets a pending intent settle after monotonic expiry and proves one input request reached the fence but zero native calls occurred. The native-call race uses a controlled adapter whose stop waits for the actual call; real Chromium acceptance is separately covered by the three browser tests.

Initial browser assertions incorrectly expected a short button label and no focused AX node. The fixture exposes Open checkout preview and its initially focused RootWebArea. Those expectations were corrected; production semantics and focus policy were unchanged. Review also made owned store faults visible as controller faults.

No local Linux, headed Xvfb, private viewer, human takeover, deployment, fresh AT or Claude-client acceptance is claimed here. These are independent gates.
