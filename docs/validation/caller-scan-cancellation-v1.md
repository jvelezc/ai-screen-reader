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

Format verification passes with no changes. Windows/Linux CI, exact private core delivery, existing Edge
deployment and original-account queued/active cancellation remain pending until
their receipts are retained; local passing tests do not establish them.

Original-account acceptance must preserve the exact admitted scan/job/generation/
producer and saved originals after a stop request; verify current native absence
independently, with no replacement attempt, stale publication or inferred goal.
The UI must distinguish a lost response from cancellation, clear data on actual
authority loss, preserve keyboard focus and work at320px. Agent use is separate
from human usability and real assistive-technology acceptance.
