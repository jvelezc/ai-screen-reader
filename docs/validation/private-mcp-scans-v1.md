# Private MCP durable scan workflow validation

[Decision 0052](../decisions/0052-private-mcp-durable-scans.md) consumes the
existing core API; it adds no storage, queue or identity-provider implementation.

| Requirement | Evidence |
| --- | --- |
| Only fixed scope and original caller; caller request UUID retained and no invented execution receipt | `InvalidRequestAndScanIdentitiesNeverReachHttp`, `ExplicitAdmissionRetryUsesSameRequestAndReturnsOnlyAdmissionFacts`, `InvalidAdmissionReceiptsRetainCallerRequestIdentity` |
| Unknown admission never auto-retries; explicit retry uses exactly the same UUID/body | `LostAdmissionNeverRetriesOrGeneratesReplacementRequest`, `ExplicitAdmissionRetryUsesSameRequestAndReturnsOnlyAdmissionFacts` |
| Dispatched cancellation joins actual work; closing the client sends no job cancellation or deletion | `CancellationJoinsAdmissionAndDisposalDoesNotCancelDurableJob` |
| Recent history is bounded to 50 and omitted history is explicit; scope/profile/identity contradictions fail closed | `RecentListRetainsBoundAndExplicitOmittedHistory`, `InvalidRecentListsCannotExposeUnconfirmedRows` |
| Pending, blocked, missing and invalid evidence do not become a goal/barrier claim | `ExecutionAndEvidenceStatesNeverBecomeInventedGoalOrBarrier`, `ContradictoryReportsAndMalformedEvidenceCannotBecomeAvailable` |
| Available evidence preserves storage/digest/unknown semantic facts and actual input/goal contradictions | `AvailableReportPreservesCoreEvidenceDigestStorageAndExposedFacts`, `ReportedInputAndGoalViolationsRemainVisibleWithoutSourceText` |
| Fresh authority on every page; changed reports cannot return old nodes | `ChangedReportAndRevokedAccessNeverReturnPreviousPages` |
| Immutable byte-bounded paging survives reconnect without skipping; invalid and exact end bounds stay explicit | `ReportBytePagingSurvivesReconnectAndNeverSkipsNodes`, `InvalidReportPagesAreRejectedBeforeHttp`, `ReportEndAndBeyondEndKeepExactCursorBounds`, `OversizedReportNodeFailsWithoutSkippingOrInventingCursor` |
| Ten hosted tools and a request-only admission schema; default local tools remain unchanged | `HostedStdioUsesRealApiChromiumPagesGraphAndConfirmedClose`, `StdioDiscovery_AdvertisesBoundedReaderToolsAndUsableSchemas` |
| Actual separate stdio processes consume Kestrel, reconnect to real saved Chromium evidence and reject corruption/revocation | `StdioScanWorkflowReadsActualChromiumEvidenceAcrossReconnectAndRejectsCorruption` |
| Scan authority failure and client disposal cannot quarantine a real live Chromium reader | `ScanAuthorityFailureAndClientDisposalDoNotQuarantineLiveReader` |

Focused execution passed 61/61 after correcting a missing test namespace import;
there were no failed behavioral assertions or weakened native deadlines. The
separate stdio test uses synthetic original-caller authentication and actual
file-backed, hash/identity-validated Chromium observations. It is not a remote
provider/JWT, S3, database-transaction or human/AT acceptance claim. SQL request-ID
idempotency remains covered by the existing real database gate. Native worker
and private S3 execution are covered separately by their existing harnesses.

Full local Windows, Windows/Ubuntu CI and private backend delivery are separate
gates, recorded on the architecture Page after completion. The MCP consumer
runs in the client's process; backend promotion does not publish an MCP service.
The real signed-in reader, authenticated Edge-to-core and human/AT gates remain
open. Public MCP, client onboarding and official plugins remain deferred.


Final local Windows: **2,004/2,004 discovered/executed/passed**, zero failures/skips, including60 new scan cases. Strict nonincremental Release build and architecture dependency checks pass with zero warnings/errors; full solution formatting passes. All 20 exact requirement method names are present and passed in the final full TRX. No production deadline or assertion was relaxed. Remote CI, private delivery and real provider acceptance remain separate receipts.
