# Finite original acquisition before final transfer

October 31, 2026. Product: Privensic Screen Reader. Scope: the admitted controlled
keyboard fixture's existing recording pipeline. The original proposal under
`outputs/` remains unchanged. [Decision0076](../decisions/0076-finite-recording-finalization.md).

## Accepted starting point and intermittent hosted result

Core `ac228d76d6bb725712e746c3b9659d137c633860` passes all nine jobs in
[CI41233416651](https://github.com/jvelezc/ai-screen-reader/actions/runs/41233416651):
Windows and Ubuntu each discover, execute and pass 3,007, with zero failed or
skipped; native71/database450/operations172 pass separately. Its
[private deployment41234972492](https://github.com/jvelezc/ai-screen-reader/actions/runs/41234972492)
matches the exact inventory and hashes of all151 installed files. Manifest SHA256
is `7e840bb7139d29c442004c794b4a3283484247fc8337fff703e75ecf20bea425`.
Trusted TLS, API1.14/private access guards, actual service health and the dedicated
rootless native inventory are independently checked. Automatic restart is disabled.

The original account admits scan `be2a0507-7e8e-4bb6-9d58-4dbeb9a9cb8d`, job
`f263904b-022f-41d1-b884-c78ea341266d`, generation1. The normal disabled-control
route completes without an observer or deliberate interruption. All five S3
originals and every archive part independently match the private staged bundle;
the baseline also matches. The complete archive is2,767,750 bytes. There are178
source originals and228 decoded video frames. The original-user notebook loads
the verified four-state/three-edge route, selects Activate, and plays the actual
9.12-second original video through its end without a media error.

The route is NotReached with an observed disabled control. Its blocked activation
makes zero browser calls. Those facts do not turn its separate zero-input
baseline into an executed route or establish real-website evidence sufficiency.

A distinct normal repeat, scan `208cb3b3-0d73-4b10-ab83-bcc6e8b1b5b0`, job
`ef24343f-91e7-4ffa-9c7f-c39a557677a6`, generation1, blocks with
`WORKER_PROGRESS_RECORDING_UNCONFIRMED`, one attempt, zero leases and no result.
Its original producer remains `7a7bfcd1-0c26-4be1-9759-fa4d9e0f0baa`.
The original account reads seven checkpoints, three intents/three observed
results and189 listed partial source images. Only the latest Activate result
confirms zero browser calls and Blocked. Overall input and goal verification
stay Unconfirmed, with no complete recording or replay authority.
The passing recording does not establish the cause of this intermittent failure.
Neither failed recording is replayed or promoted into complete evidence.

## New source behavior and local evidence

An actual Chromium route test fails before the repair:17 source files become30
while the final source transfer waits500 ms after the last route observation.
A second actual check finds that original.webm has not yet been finalized when
that transfer begins. Finalization now joins both image acquisition and video
before the private transfer, inside the existing input-denied Draining phase.
Every acquired original remains in the result. Capture exclusion is acknowledged
only after the real transfer settles.

The focused set discovers, executes and passes162 tests, zero failed/skipped,
including all four real Chromium routes, held transfers, cancellation, failure,
the original privacy-drain checks and actual framed-child rejection. The final
pinned Windows Release suite independently discovers, executes and passes3,012,
zero failed/skipped, in7m58s. Strict Release build has zero warnings/errors; full
format verification passes. Source CI, exact deployment and fresh hosted runs
are separate gates for this increment. No local result is a hosted repair.

| Requirement | Evidence |
| --- | --- |
| Final transfer cannot extend source acquisition after the route | `FinalOriginalTransferCannotExtendActualSourceAcquisitionAfterTheRouteEnds` |
| Actual image set and finalized video stay immutable during a held transfer | `ActualFinalSourceDrainDeniesInputAndWaitsForTheOwnedTransfer` |
| Deny input/public copying and replacement sinks while final transfer is pending | `ActualFinalSourceDrainDeniesInputAndWaitsForTheOwnedTransfer` |
| Cancellation or failure cannot acknowledge exclusion or return completed media; native calls settle | `CancelledOrFailedFinalSourceTransferCannotAcknowledgeCaptureExclusion` |
| Pre-cancelled transfer joins acquisition, never invokes its sink and cannot acknowledge exclusion | `PreCancelledFinalTransferJoinsAcquisitionWithoutInvokingTheSink` |
| Existing public-transfer cancellation still waits for actual owned work | `ActualCaptureExclusionJoinsPendingOriginalTransferEvenWhenItsCallerIsCancelled` |
| Safe export cause survives sanitized disposal without a stop-success claim | `ActualRecordingCollectorKeepsSafeExportCauseAfterSanitizedShutdownFailure`, `WrongSourceAcknowledgementStopsActualChildBeforeAnyCommandCheckpoint` |
| Independent outcomes and timing survive on all four actual routes | `FourOriginalRecordings_LinkRealCaptureAndInputIntervalsWithoutChangingRouteClassification` |

## Next acceptance gate

The full pinned Windows Release suite, strict build and format verification pass.
Push main, require all CI platforms, promote the exact source privately, and
verify distinct normal recordings and their originals through the original
account. Keep the intermittent failure open until the new hosted evidence
supports closure. A resource-boundary count alone is not a cause. Broader fault,
authority, target collection, intervention, operations and human/AT gates remain
open in the [ordered completion plan](../completion-plan.md).
