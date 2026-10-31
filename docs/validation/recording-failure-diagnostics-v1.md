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
The real wrong-source-ack case surfaces an unknown wrapped exception and therefore
retains only `Unconfirmed`; the test does not pretend its cause is proven.
Pinned strict Release build has0 warnings/errors; format verification passes.
The full pinned Windows Release suite discovers/executes/passes **2,983**,0
failed/skipped; independent TRX parsing confirms the actual totals. This adds47
cases to the accepted2,936 baseline. CI/deployment remain separate gates.

Linux Supabase PostgreSQL17 in an owned disposable Podman container passes
**450/450**,0 failed/unconfirmed/skipped, including19 new actual role/capability
checks. Each of the thirteen reasons persists through the existing original
attempt capability without a completion reference. User/anonymous access, raw
details, prefix-forged codes, null reasons and a different capability stay denied.
The container is removed and that run changes no hosted project or worker.

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

Full Windows/Ubuntu CI, actual Linux native validation, exact private delivery and
a distinct original-account recording remain pending for this increment. Then
establish the actual cause from retained safe diagnostics, fix it within declared
limits and verify complete originals with the actual goal outcome. A count at a
resource boundary alone is not that diagnosis; never replay the old failed attempt
or promote partial files into successful collection. Wider fault/authority/
website/operations/human/AT gates remain open.
