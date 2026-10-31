# Trusted recording failure boundaries

October 31, 2026. Product: Privensic Screen Reader. This increment locates an
uncertain recording failure without exposing raw exceptions or claiming repair.
The original proposal under `outputs/` is unchanged.
[Decision0077](../decisions/0077-trusted-recording-failure-boundaries.md).

## Delivered baseline and separate hosted outcomes

Core 7fbf57a passes all nine [CI41239208999](https://github.com/jvelezc/ai-screen-reader/actions/runs/41239208999)
jobs: Windows/Ubuntu each3,012 discovered/executed/passed,0 failed/skipped;
native71/database450/operations172 pass separately. Its
[private delivery41240613414](https://github.com/jvelezc/ai-screen-reader/actions/runs/41240613414)
matches all151 installed files and manifest
`ff06739b69d2592f1fff77230bfa394c5a9417ee6feba1853387f4e92b471d3f`.
Trusted TLS, API1.14/private guards, both services and actual dedicated rootless
native absence pass independently. Restart remains disabled.

Normal original-account scan `6e39209b-51c9-4bc6-aa2a-55429470cb00`, job
`eb5b2ddd-34ed-494e-bab5-775c92497e08`, generation1, producer
`a06df21b-8822-4807-9e73-9a0acb9739c6` completes in one attempt, with no lease
remaining. The2,667,874-byte bundle, all five S3 originals, every archive part and
the separate baseline independently match. There are171 original source images
and201 decoded video frames. The original account loads the four-state/three-edge
route, selects Activate and plays the8.04-second native original video through
its end with no media error. The disabled-control route is NotReached/Observed;
Activate confirms zero browser calls. Its zero-input baseline stays separate.

A distinct normal repeat, scan `6eecf6f6-f25b-479e-9c87-e1e5d716b396`, job
`7ac8fa08-8a32-43ef-b91f-57a1ece4a8cd`, generation1, blocks with
`WORKER_PROGRESS_RECORDING_UNCONFIRMED`, one attempt, zero leases and no result,
archive or completed-recording digest. Both same healthy service invocations and
actual native absence are checked after it. No observer, deliberate interruption
or replay was introduced. Its cause remains unproven; preserve every old attempt.

## Source checks and database application

The focused Release set discovers, executes and passes242 tests,0 failed/skipped,
including actual Chromium normal, checkpoint/preparation failure, cleanup scope
and framed output-transfer cases. Strict Release build has0 warnings/errors;
full format verification passes. The final pinned Windows Release suite discovers,
executes and passes3,060 tests,0 failed/skipped, in8m16s. CI, exact private delivery
and hosted acceptance remain separate gates for this increment.

The isolated Linux Podman PostgreSQL17 proof discovers, executes and passes461,
0 failed/skipped/unconfirmed, and removes its owned container. The actual final
receipt is `artifacts/database/70474be965184a468a660095b0897386/run.json`.
All24 recording reason values are tested with worker-only execution and existing
capability fences. The runner refuses Ready when its461-case count is incomplete.
An earlier460-case SQL pass failed its old expected-total gate and is superseded.

Supabase migration `20261031213005_recording_failure_boundaries.sql` is applied
through the connected project. The CLI-generated local filename matches the
observed hosted history. The exact new function-body SHA256 is
`46f3e3e820ce9bc4abaa569a0535cb496876ec810e8dab988a062c769b21d9ab`.
Owner postgres, SECURITY DEFINER and empty search_path remain; worker execution
is allowed and authenticated/anonymous execution denied. Security-advisor finding
identities and metadata are unchanged; unrelated baseline settings are preserved.

| Requirement | Evidence |
| --- | --- |
| Preserve scoped five-field bounded uncertainty for every trusted operation | `TrustedRecordingBoundaryRetainsOnlyUncertaintyAndTheOriginalFrameScope` |
| Accept exact known stop codes and reject forged suffixes | `OnlyExactKnownStopCodesRefineTheTrustedBoundary` |
| Exception data cannot supply an unknown or invalid boundary | `ExceptionTextAndDataCannotSupplyAnUnknownOrInvalidRecordingBoundary` |
| Existing collector causes win; a generic code cannot overwrite the operation | `SpecificCollectorFailureWinsOverATrustedBoundaryWithoutClaimingStop`, `GenericRecordingUncertaintyCannotOverrideTheActualTrustedBoundary` |
| Actual returned traversal failure survives successful cleanup | `ActualUnknownCheckpointFailureRetainsTheTrustedTraversalBoundaryAfterCleanup` |
| Preserve normal originals, outcomes and evidence format | `ActualSuccessfulKeyboardRecordingKeepsItsBoundaryTraceOutOfTheEvidence` |
| Successful cleanup preserves a prior failure; foreign data stays untouched | `ActualSuccessfulCleanupCannotReplaceThePriorFailedRecordingBoundary`, `CleanupScopeFailurePreservesForeignFilesAndReportsOnlyCleanupUncertainty`, `SuccessfulOwnedFolderCleanupCannotInventAFailureBoundary` |
| No, partial or complete received bytes cannot acknowledge a failed output transfer | `ActualOutputTransferFailureCannotMasqueradeAsValidationOrAcknowledgeCompletion` |
| Coordinator persists a closed error without a result or completion | `KnownRecordingFailurePersistsSpecificReasonWithoutPromotingACompletedReceipt` |
| Worker-only SQL execution and unchanged lease/capability fences | `tests/database/recording-failure-reasons.sql`;461-case isolated PostgreSQL receipt |

## Next acceptance gate

Require the complete Windows suite and both CI platforms, promote the exact
source privately, then admit a distinct normal original-account recording.
Use its actual closed failure boundary to investigate the intermittent fault;
do not infer a cause from a frame count or retry an uncertain attempt. Complete
originals and actual outcomes must remain independently verifiable. The diagnostic
increment alone cannot establish that recording is reliable. Wider failure,
authority, target, intervention, operations and human/AT gates remain in the
[ordered completion plan](../completion-plan.md).
