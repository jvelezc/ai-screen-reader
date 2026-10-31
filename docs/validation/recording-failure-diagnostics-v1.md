# Scoped recording-failure diagnostics

October31,2026. Scope: the existing admitted authored-keyboard recording child and
its host/coordinator. This implements safe failure propagation; it does not yet
establish or fix the separate hosted recording's actual finalization cause.
Public MCP/plugins and arbitrary target collection remain deferred/separate.

## Concrete trigger and accepted scope

At accepted core5b75935, original-account scan
`ba75b574-07f6-4f83-863a-21b5aa584779` retains seven command checkpoints and250
source images, has no complete output seal, and reports only
`FIXTURE_OBSERVATION_UNCONFIRMED`. Its catalog intentionally sends no browser keys;
the after-input observer correctly refuses a missing input witness and never
freezes or kills it. Those retained facts cannot establish overall zero input,
complete video or a successful goal. Preserve the existing attempt.

The new scope-bound terminal diagnostic preserves only a closed reason and the
original accepted stream prefix. Unknown errors stay `Unconfirmed`, never raw
stderr or an inferred accessibility barrier. [Decision0075](../decisions/0075-scoped-recording-failure-diagnostics.md).

## Local verification

Two initial regression cases fail before implementation and pass after it: the
host formerly rejects the diagnostic as an unrelated JSON shape, and the
coordinator replaces its reason with a generic block. The final focused set passes
**153/153**,0 failed/skipped, including actual Chromium framed-child execution.
At the accepted1200190 revision, the real wrong-source-ack case surfaces an
unknown wrapped exception and therefore retains only `Unconfirmed`.
Pinned strict Release build has0 warnings/errors; format verification passes.
The full pinned Windows Release suite discovers/executes/passes **2,983**,0
failed/skipped; independent TRX parsing confirms the actual totals. This adds47
cases to the accepted2,936 baseline.

Linux Supabase PostgreSQL17 in an owned disposable Podman container passes
**450/450**,0 failed/unconfirmed/skipped, including19 new actual role/capability
checks. Each of the thirteen reasons persists through the existing original
attempt capability without a completion reference. User/anonymous access, raw
details, prefix-forged codes, null reasons and a different capability stay denied.
The container is removed and that run changes no hosted project or worker.

## Accepted1200190 delivery and fresh original-account result

[CI41228536723](https://github.com/jvelezc/ai-screen-reader/actions/runs/41228536723)
passes all nine jobs. Independently parsed Windows and Ubuntu TRX each report
2,983 discovered/executed/passed,0 failed/skipped. Linux native71, database450
and operations172 also pass. [Private deploy41230145370](https://github.com/jvelezc/ai-screen-reader/actions/runs/41230145370)
matches all151 actual installed files; manifest SHA256 is
`eb789c10933d94336486ebb49651ec87ebb6ef5a51470ee215e505f3b6594502`.
Trusted TLS, HTTP1.14/access guards, healthy services and rootless native absence
pass separately from the CI receipt. Automatic restart remains disabled.

The original account admits a distinct normal disabled-control recording,
scan `37ef357d-af22-4c03-a053-954a17269fdd`, without an observer, freeze or deliberate
interruption. Its original job `d255cfb8-3d11-4cd8-98f6-df1376dbb1e1`, generation1,
has no result reference and remains blocked with
`WORKER_PROGRESS_RECORDING_UNCONFIRMED`. The notebook shows NotCollected, actual
inputs/overall goal unconfirmed, seven checkpoints and184 partial images. All
three saved command results exist; the latest reports0 input calls and Blocked.
Those local command facts do not establish overall collection or a final video.
The same worker PID/invocation remains healthy and dedicated rootless containers
are independently absent. Preserve this attempt; it is not replayed.

## Collector-cause preservation: source implemented, delivery pending

Source review and the actual wrong-ack regression identify two sanitizing layers:
capture stop and browser disposal. Both must keep their existing fail-closed
acknowledgement/privacy contracts. The recording probe now retains only a closed
frame-budget/invalid-frame/write-failure/export-uncertainty code during teardown,
before browser shutdown can mask it. No original exception, stack, Data or path
is retained as that diagnostic. The CLI carries it internally, JSON-ignored, and
the child recognizes only an immediate approved cause under the exact capture-
stop or Chromium collector-stop wrappers. Other shutdown errors, deeper chains,
unknown causes and missing causes remain Unconfirmed. No new public fields,
reason codes, SQL permissions or capture limits are introduced.

The final focused run passes81/81,0 failed/skipped. Four initial mapper cases fail
before repair. Actual Chromium now carries SourceExportUnconfirmed through the
wrong-source-ack child without any observed result/bundle/next checkpoint; it does
not invent an ACK-rejection diagnosis. Direct actual collector shutdown remains
Faulted with settled calls and a payload-free export reason. All four successful
recording routes retain their prior independent outcomes and no failure cause.
The full pinned Windows Release suite independently reports3,007 discovered/
executed/passed,0 failed/skipped. Strict build and full format verification pass.
The first full run exposed an unrelated pixel-denial assertion matching PNG
inside a random trace ID. It now pins the problem JSON media type,401/title/detail
and permitted field set while retaining the actual revocation/drain assertions;
all25 focused pixel-transport tests and the final full suite pass. CI, exact
promotion and a new hosted diagnosis remain pending for this follow-up; the
failed hosted run's underlying cause is still unproven.

| Requirement | Evidence |
| --- | --- |
| Preserve known cause without exception data | `KnownFaultCarriesOnlyBoundExecutionAndClosedReasonWithoutExceptionData` |
| Unknown error cannot disclose details | `UnknownOrRawExceptionsCanReleaseOnlyBoundUnconfirmedStatusWithoutDetails` |
| Exact execution, prefix and closed JSON | `ForgedMalformedOrUnboundFailureCannotReleaseAnApprovedReason`, `UnboundTerminalFailureCannotAcknowledgeOrMutateTheOriginalPrefix` |
| Plan/source/byte boundaries | `FailureCountersRespectActualPlanAndAdmittedSourceBounds`, `DiagnosticSizeIsBoundedBeforeDeserialization`, `UnadmittedSourcesOrUnknownInternalReasonCannotBecomeAPublicDiagnostic` |
| Failure cannot authorize input or completion | `HostKeepsScopedRecordingFailureWithoutAcknowledgingInputOrCompletion`, `TerminalRecordingFailurePreservesPendingInputAndCannotAdmitAFollowingObservedResult` |
| Real Chromium rejection preserves no-result/no-bundle contract | `RealChromiumWrongIntentAcknowledgementCannotProduceAnObservedResultOrBundle`, `WrongSourceAcknowledgementStopsActualChildBeforeAnyCommandCheckpoint` |
| Persist exact safe reason and preserve cancellation priority | `KnownRecordingFailurePersistsSpecificReasonWithoutPromotingACompletedReceipt`, `RecordingDiagnosticCannotOverruleCommittedCallerCancellation` |
| Worker-only durable reason and unchanged capability fence | `RecordingDiagnosticsPreserveWorkerOnlyBlockAuthority`, `ApplicationCallerCannotWriteWorkerRecordingReason`, `KnownRecordingReasonCannotBypassTheOriginalAttemptCapability`, thirteen `BoundRecordingFailurePersistsWithoutCompletion_` cases |
| Capture-stop privacy and denied-input state stay intact for known and unknown errors | `KnownCollectorFailureCannotExposePayloadOrImplySuccessfulCaptureStop`, `UnknownCollectorErrorStillHasNoInnerPayloadAndCannotReopenCapture` |
| Only approved immediate collector causes become a diagnostic | `KnownCollectorStopCauseMapsWithoutLosingUncertaintyOrDisclosingDetails`, `ForeignDeepUnknownOrRawStopCausesRemainUnconfirmed` |
| Internal cause cannot enter a serialized traversal report | `InternalCollectorCauseCannotBeSerializedIntoTraversalReports` |
| Actual collector/export failure survives sanitized disposal without a success claim | `ActualRecordingCollectorKeepsSafeExportCauseAfterSanitizedShutdownFailure`, `WrongSourceAcknowledgementStopsActualChildBeforeAnyCommandCheckpoint` |
| Successful actual recordings keep their independent route classifications | `FourOriginalRecordings_LinkRealCaptureAndInputIntervalsWithoutChangingRouteClassification` |
| Revoked pixel response is an explicit JSON denial, regardless of opaque trace ID | `ActualPixelHttpRevocation_WaitsActualSourceAndDropsItsLateBytes` |

The actual child still exits3 on failure and emits no successful output bundle.
Invalid starts emit neither progress nor failure metadata. A terminal frame cannot
acknowledge another result; an already saved input intent remains unresolved.
The independent native cleanup and caller-cancellation safeguards remain mandatory.

## Hosted schema and remaining gates

Migration `20261031180507_recording_failure_reasons` is applied through Supabase
MCP. Independently read function-body SHA256 matches local source:
`a8431a5701265d95d82e7f3ae9c8cdb0159b4b0f3d6bfe5bda79301c91eac986`.
Empty search path, security definer and worker-only execute remain intact.
This additive function change does not replace original lease fencing or grant
application users worker authority.

Terminal diagnostics at1200190 are delivered and the distinct account recording
is preserved as failed. Collector-cause preservation passes3,007 locally and still
requires CI/native/exact delivery and a new original-account recording. Then establish the
actual cause from retained safe diagnostics, fix it within declared
limits and verify complete originals with the actual goal outcome. A count at a
resource boundary alone is not that diagnosis; never replay the old failed attempt
or promote partial files into successful collection. Wider fault/authority/
website/operations/human/AT gates remain open.
