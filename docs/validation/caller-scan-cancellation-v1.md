# Caller-owned scan cancellation validation

October 31, 2026. Scope: an explicit cancellation request for an already admitted
run. No public MCP/plugin, arbitrary-site admission, evidence deletion or replay.

## Accepted locally

Pinned .NET10.0.402, Windows Release: **2,924 discovered/executed/passed,0 failed,
0 skipped**. The actual Chromium framed-worker case below passed separately and
in that full suite. Linux Supabase PostgreSQL17 in an owned disposable Podman
container: **417 discovered/executed/passed,0 failed/unconfirmed/skipped**, including
41 cancellation and15 actual concurrent-session cases. The container was removed;
that test did not change the hosted project or execute a worker.

| Requirement | Evidence |
| --- | --- |
| Original caller, tenant and fixed scope only | `CreatorCancellationRpcIsAvailableToAuthenticatedCallers`, `WorkerCannotInvokeOriginalCallerCancellation`, `CancellationRpcUsesOriginalBearerAndOnlyTheAdmittedScopeWithoutClientActorOrProducer` |
| Preserve actual attempt; no invented zero input | `StopRequestPreservesActualAttemptWithoutClaimingCleanupOrZeroLeasedInput`, `AClaimBetweenInitialReadAndCancellationIsReportedAsUnassessedInput` |
| Repeated request preserves identity and original time | `RepeatedCancellationPreservesTheOriginalRequest` |
| Revoke execution and independent publication authority | `WaitingExecutionCompletionCannotOverrideCancellation`, `WaitingPublicationCapabilityCannotCompleteACancelledRun` |
| Completion wins honestly when it commits first | `CompletedRaceReturnsNoopWithoutInventingAStopOrZeroInput`, `CompletionRacePreservesTheExactOriginalDigestAndState` |
| Current membership and authority after mutation | `CancellationHoldsOriginalMembershipUntilItsTransactionCommits`, `FreshCancellationRejectsTheCommittedMembershipRevocation`, `CurrentCallerLossAfterTheMutationDoesNotReleaseItsScopeMetadata` |
| Validate actual producer and closed provider response | `DifferentActualProducerCannotAuthorizeAnOtherwiseValidCancellationReply`, `UntrustedRpcShapeOrClaimsCannotBecomeAConfirmedCancellation` |
| Bind HTTP status and MCP meaning; no automatic retries | `HttpStatusMustAgreeWithCancellationMeaning`, `LostCancellationResponseNeverAutomaticallyRetriesOrAdmitsReplacement` |
| Real browser denial after actual input | `RealChromiumLeaseRevocationAfterFirstInputRetainsUnresolvedIntentWithoutDispatchingItsNextCommand` |
| Published independent contract | `PublishedCancellationContractDistinguishesAcceptedRequestAndCompletedNoop`, actual stdio tool discovery in `HostedStdioUsesRealApiChromiumPagesGraphAndConfirmedClose` |

The real Chromium case completes one key action, retains the next intent, denies
its acknowledgement after lease-authority loss, joins the failed child, retains
four checkpoints/one result and emits no complete output bundle. It preserves
unknown intent and does not establish provider cancellation or independent
hosted native absence. The SQL races use actual role-restricted separate sessions
and witnessed job/membership lock waits, not simulated concurrent responses.

The 127 existing MCP/transport tests pass before and after extracting the shared
HTTP response path. A new regression rejected Web JSON's quoted numeric attempt;
cancellation now requires a real JSON integer. One legacy concurrent lease test
now checks its own job's completion events so another committed test fixture
cannot invalidate that assertion.

## Delivery gates

Strict Release build passes with0 warnings/errors. Hosted migration
`20261031154146_scan_cancellation` is applied through Supabase MCP, matching the
local forward-only migration identity. Independent function-body SHA256 readback
matches all three definitions; cancellation is executable only by authenticated
users, storage publication only by the worker, and the active-publication helper
by neither caller role. All use the empty search path. Preflight found0 unfinished
jobs/leases and no previous cancellation columns. Existing rows remain unmarked.

At core `f3d675de1cf6699d4b44181cfa1c8d085d986bdd`,
[CI41216392696](https://github.com/jvelezc/ai-screen-reader/actions/runs/41216392696)
passes all nine jobs: Windows/Ubuntu **2,924 each**, native Linux **71**,
database **417**, operational **172**. Downloaded result artifacts independently
confirm these counts. [Private delivery41217941175](https://github.com/jvelezc/ai-screen-reader/actions/runs/41217941175)
passes; all151 deployed files match manifest SHA256
`1f898a4e8ebd5380d31983934c0f6b3390ce628fb5109eaf2595c5c1dc19fb10`.
Trusted TLS, API1.14, cancellation schema13 fields and anonymous/private-route
denials pass independently. UI `c614141b2764ef3fd071822c846145c59c55bfad`
[CI41217235305](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/41217235305)
passes **739 each OS**. Existing Edge `hsr-core` v11 has exact three-file readback;
the existing application account reaches the workspace without another sign-in.

## Original-account cases and an open worker-health regression

Queued scan `965f30ec-bfee-4ae5-96d5-215b30a38850` stops under its original
account with generation0, no attempts/leases or queued message, and an immutable
caller marker. The focused notice distinguishes a request from cleanup and
correctly reports that no execution attempt started.

Checkout scan `50ec9c17-a7c6-46e1-9287-cbcb388cb66a`, job
`7dcfbd6d-f238-4f49-9590-52c9a72b3895`, records actual input before its caller
stops it. The same generation1/producer and single attempt remain. All167 earlier
images survive within **205 validated originals /2,760,703 bytes**. Independently
owned native removal occurs after the marker; current rootless native absence
passes. No seal, archive, completed receipt, replacement or replay is created.
However, the coordinator classifies the revoked lease as unknown and exits3:
**active cancellation acceptance is incomplete because worker availability fails**.
Ordered idle service restoration succeeds with automatic restart still disabled.

An earlier feedback case `182b19ed-51ca-413f-80f6-2ecb30f05eb4` had native
removal before the stop marker. It is an already-ended after-input case and
cannot substitute for active-stop acceptance. Its observer error ran its bounded
thaw cleanup; the original attempt was not replayed.

## Worker continuity repair in progress

Twelve coordinator cases cover capability-bound cancellation, joined execution
and heartbeat cleanup, unknown confirmation/outage, simultaneous heartbeat and
native-cleanup uncertainty, and rejected/lost completion without exposing a local
file as a published receipt. The first eight cases establish red evidence:
six fail before the fix and two existing native-uncertainty guards pass.
The new worker-only read-only RPC has **14 actual PostgreSQL role/capability
checks**, bringing the disposable database run to **431/431**,0 failed/skipped.
Hosted migration `20261031164005_confirm_caller_cancellation` has matching body
SHA256 `1123f68bd318ab988c818453044a5216046450baa9e6ba51513524f1a575c97d`,
empty search path, worker execute and denied authenticated/anonymous execute.
It cannot grant renewal or publication, mutate the job/history, or certify cleanup.

| Requirement | Evidence |
| --- | --- |
| Confirmed stop before allocation keeps polling healthy | `CallerCancelledBeforeAllocationDoesNotHaltThePollingWorker` |
| Actual executor/heartbeat work joins before reconciliation | `CallerCancelledExecutionJoinsCleanupBeforeReconcilingRejectedOrLostBlock`, `CallerCancelledHeartbeatJoinsActualCleanupBeforeHealthyCancellationResult` |
| Cancellation cannot hide unknown native cleanup | `CallerCancellationCannotOverruleUnconfirmedNativeOwnershipOrCleanup`, `CallerCancellationCannotHideCleanupUncertaintyAfterHeartbeatAuthorityLoss` |
| Unknown lease loss or database outage still halts | `UnknownLeaseLossOrCancellationConfirmationOutageStillHaltsSafely` |
| No published receipt from orphaned retained bytes | `CallerCancelledCompletionCannotExposeARetainedFileAsAPublishedReceipt` |
| Original worker/token/generation and tenant only | `OnlyTheOriginalAttemptCapabilityConfirmsDurableCallerCancellation`, `DifferentAttemptTokenCannotConfirmCancellation`, `ForeignOrganizationCannotConfirmCancellation` |
| Read-only confirmation does not restore authority | `ConfirmationCannotMutateOriginalJobAttemptOrEventHistory`, `ConfirmationCannotRenewExecutionOrPublishCompletion` |

The repair's pinned strict Release build has0 warnings/errors. Its full Windows
run discovers/executes/passes **2,936**,0 failed/skipped; independent TRX parsing
also confirms all12 new coordinator cases pass. Format verification follows a
whitespace-only correction. CI, exact private delivery and fresh active-stop then
subsequent no-input admission remain pending. Existing cancelled attempts must
not be replayed to create that proof.

The original account also reads the stopped checkout's two observation nodes,
one actual Tab edge, three checkpoints and205-image subset. Source205 downloads
as13,513 bytes with SHA256
`61e9858a63f4287834df3e164430bf9af68e2dd0a90f5c019f9b01b7b2b09f1c`,
matching its independently checked private original and presentation timestamp
1793463915807.724. Those consumer reads do not close the worker-health gate.

Original-account acceptance must preserve the exact admitted scan/job/generation/
producer and saved originals after a stop request; verify current native absence
independently, with no replacement attempt, stale publication or inferred goal.
The UI must distinguish a lost response from cancellation, clear data on actual
authority loss, preserve keyboard focus and work at320px. Agent use is separate
from human usability and real assistive-technology acceptance.
