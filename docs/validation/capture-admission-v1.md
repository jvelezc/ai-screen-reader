# Capture admission v1 validation

Local Windows, October 20, 2026: **641 discovered, executed and passed; 0 failed, 0 skipped**. All 625 previous test identities are retained; 16 cases were added. Release nonincremental build: zero warnings/errors. Formatting verification passed. The retained TRX is `artifacts/capture-validation/capture-windows-final-2.trx` (local generated artifact).

| Requirement | Evidence |
| --- | --- |
| Cancellation does not release underlying Chromium work; late payloads are rejected | `Exclusion_DrainsActualOperationAfterCallerCancellationAndRejectsLatePayload` |
| Stop acknowledgement precedes the exclusion token | `StopAcknowledgement_IsRequiredBeforeExclusionLease` |
| Stop failures and stalled transitions remain closed | `FailedStop_FailsClosedWithoutPayloadInErrorOrLifecycle`; `StalledDrainStopOrBaseline_FailsClosedAndLateCompletionCannotReopen` (drain, stop and baseline) |
| Fresh baseline precedes admission and rotates the epoch | `Resume_KeepsAdmissionClosedUntilFreshBaselineAndRotatesEpoch` |
| Foreign, stale and consumed tokens cannot reopen capture | `WrongForeignAndConsumedLeases_CannotReopenAdmission` |
| Failed resumption remains terminal | `FailedBaseline_RemainsFaultedAndCannotReuseLease` |
| Failed operations release drains; close aborts and fences work | `FailedOperation_ReleasesDrainWithoutPretendingCollectorStopped`; `Close_AbortsUnderlyingWorkBeforeWaitingAndNeverReopens` |
| Lifecycle evidence is monotonic, bounded and payload free | `Lifecycle_IsPayloadFreeMonotonicAndExplicitlyBounded` |
| Real Chromium excludes a visible canary and input, resumes the same document/context, and retains no canary in structured artifacts | `Exclusion_BlocksPayloadAndKeyboardDuringVisibleCanaryThenResumesSameContext` |
| Real scoped character input is denied during exclusion | `Exclusion_DeniesScopedTypeaheadBeforeAuthorizationCaptureOrDispatch` |
| Reader mode/cursor and text/live history reset across gaps without fabricated transitions | `Resume_ResetsReaderCursorAndModeEvenWhenNoCommandRanDuringGap`; `EpochChange_ReanchorsTextAndSeedsLiveBaselineWithoutInventingChangesThroughGap` |

The live canary diagnostic observer is confined to the trusted test assembly. Its content is deliberately not a reader observation or a persisted payload. These tests do not establish video privacy: no video/screenshot/trace/HAR collector is enabled in this increment. Recording-collector canaries are the next gate. No real credentials or human input authority are exposed.

Remote Windows/Linux CI is run on the corresponding main push; inspect its receipt separately. Local Linux, deployment and fresh assistive-technology validation were not performed for this increment.
