# Private HTTP-backed MCP validation

This validates [decision 0051](../decisions/0051-private-http-backed-mcp.md).
Synthetic protocol tests are separate from provider identity and hosted acceptance.

| Requirement | Evidence |
| --- | --- |
| Private fixed origin, original caller and scope | `ConfigurationRejectsNonPrivateOrNonRootOrigins`, `ConfigurationRejectsInvalidBearers`, `ConfigurationRedactsCredentialAndRejectsEmptyScopeAndOversizedBearer`, `FixedScopeOriginalCallerAndConfirmedReplacementArePreserved` |
| No redirect credential forwarding; actual bounded body/deadline | `RealHttpRedirectNeverForwardsOriginalBearerToDestination`, `InvalidAdmissionNeverClaimsCleanOrRetries`, `BodyReadHasOwnDeadlineAfterHeadersComplete` |
| No duplicate admission or false cleanup; cancellation joins actual work | `LostAdmissionBlocksAutomaticReadmissionAndRetainsUnconfirmedCleanup`, `UnconfirmedCloseCannotReplaceButExplicitCloseCanRecover`, `CallerCancellationJoinsDispatchedAdmissionAndKeepsOwnership`, `KnownAdmissionRejectionCanRetryWithoutInventingOwnership` |
| Immutable pages recheck authority, fence documents and never skip oversized content | `CachedPagesRecheckAuthorityAndCommandsInvalidateObservation`, `RevokedAuthorityNeverServesCachedObservation`, `SnapshotBytePagingAndOversizedFirstNodeNeverSkipContent` |
| Input/goal denial and actual uncertainty preservation, including contradictory API totals | `InputCommandsAndKeysAreDeniedBeforeHttp`, `ReportedInputViolationPreservesFactsAndRequiresExplicitRecovery`, `ContradictoryApiInputAndGoalTotalsRemainVisible`, `OversizedHostedCommandPreservesActualInputUncertainty`, `HostedStdioUsesRealApiChromiumPagesGraphAndConfirmedClose` |
| Atomic trace/event byte paging and explicit truncation/provenance | `TraceShrinksByEncodedBytesWithoutSkippingAtomicEntries`, `OversizedTraceEntryFailsWithoutInventedContinuation`, `TraceRejectsContradictoryOrMissingProvenance`, `ExplicitTraceTruncationSurvivesMcpPaging`, `TimelineBytePagingRetainsEventsAndTruncationWithoutCapture` |
| Separate stdio process consumes Kestrel/real Chromium, discovers seven tools and verifies upstream absence after close | `HostedStdioUsesRealApiChromiumPagesGraphAndConfirmedClose` |

The API/OpenAPI/local MCP contract suite passed 90/90 after the shared policy
extraction, before hosted behavior was introduced. The first focused pass found
two incorrect test assumptions: normalized loopback spelling and equivalent
escaped JSON. Expectations were corrected to compare the address and semantic
JSON; no production fence or deadline was relaxed.

Final Windows, Linux CI and private delivery results are recorded separately on
the architecture Page when complete. Real Chromium uses a synthetic API
authenticator; application-user JWT/RLS, hosted tenant reader acceptance,
authenticated Edge-to-core and human/AT remain separate open gates. Public MCP,
official plugin and user onboarding remain deferred.


Final local Windows validation: **1,944/1,944 discovered/executed/passed**, zero failures/skips, including58 new hosted MCP cases. Strict nonincremental Release build and architecture dependency checks pass with zero warnings/errors; complete solution format verification passes. Every named requirement test above is present and passed in the final full TRX. One earlier clean rebuild overlapped an active test process, causing file locks and a browser cleanup failure; after joining it, the rebuild and complete suite were rerun sequentially. No assertion or native deadline was relaxed. Remote CI and exact private backend delivery are separate receipts on the architecture Page.
