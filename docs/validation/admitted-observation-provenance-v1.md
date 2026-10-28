# Admitted observation provenance validation

October 27, 2026. See [contract 0058](../decisions/0058-admitted-observation-provenance.md).

| Requirement | Evidence |
| --- | --- |
| Exact originals and actual attempt survive reconnect | `ActualChromiumProducerArchiveRetainsExactObservationAndAttemptAcrossReconnect` launches real Chromium, finalizes actual source bytes to an owned file archive, reopens it independently and compares the complete original, manifest scope and hashes. |
| Completion waits for actual archive readback | `ProducerIndexReadbackPrecedesCompletionAndRejectedFenceLeavesOnlyOrphanBytes` holds real finalization at publication and asserts no early completion, then tests both accepted and rejected fences. |
| Cancellation cannot abandon a storage callback | `CancelledProducerReadbackJoinsActualStorageBeforeBlockingWithoutCompletion` holds actual readback across cancellation and checks no completion or early block. |
| Invalid source or readback cannot publish an index | `UnboundProducerInputsCannotPublishAnyArchive`, `MissingOrCorruptOriginalReadbackCannotPublishProducerIndex`. |
| Receipts cannot alter producer scope or collection claims | `ProducerReceiptCannotChangeAttemptOrUpgradeIncompleteReadback`, `ProviderProjectionRequiresAllProducerFieldsAndCompletedAttempt`, `McpRejectsUnconfirmedProducerProvenanceWithoutReturningObservation`, `ProducerReceiptSchemaPreservesReadbackAndObservationOnlyBounds`. |
| Known identity is separate from present availability | `KnownProducerReceiptDoesNotClaimCurrentOriginalAvailability` retains an Unavailable result with the historical receipt. |
| Actual stdio/API/Chromium integration | `StdioScanWorkflowReadsActualChromiumEvidenceAcrossReconnectAndRejectsCorruption` now returns the actual finalized producer receipt through HTTP and reconnected private stdio MCP, while retaining revocation/corruption behavior. Provider identity is synthetic in this test. |
| Durable completion and tenant isolation | `tests/database/worker-evidence.sql`: 27 actual PostgreSQL/pgmq cases, including `ProducerWriteFailureRollsBackActualCompletion`, `FailedProducerWritePreservesQueueLeaseAndNoCompletionEvent`, `ExpiredProducerAttemptCannotPublish`, `ExactProducerRetryIsIdempotent`, `CompletedProducerIndexCannotBeReplaced`, `PeerCannotDiscoverProducerReceipt`, `RevokedMemberCannotRediscoverProducerReceipt`, `LegacyCompletionCannotAcquireRetrospectiveProducerReceipt`. |
| Real admitted container producer | `Test-ObservationWorker.ps1` adds `FencedCompletionPublishesArchiveForActualWorkerAttempt`, `ProducerArchiveIndexBindsJobAndExactOriginalBytes`, `ProducerManifestPreservesNoRecordingNoInputAndActualAttemptScope`. These inspect the actual Linux worker, database attempt and complete original file bytes. |
| Existing consumer compatibility | UI `producer-contract.test.ts`: the retained-Unavailable case and ten rejection cases. Earlier missing/null receipts remain compatible. |

The final Windows run passes **2,311/2,311** discovered/executed tests, with zero
failures or skips, including 40 new producer/schema cases. The first full pass
was 2,305/2,305; the subsequent schema/cancellation focused pass was 68/68.
Counts were independently parsed from the actual TRX counters. The pinned
nonincremental Release build has zero warnings/errors; full formatting is verified.
Remote CI, exact deployment and real-account producer acceptance are recorded
separately in the architecture Page when completed.

On the actual Linux VPS, a separate owned temporary database ran all **223/223**
discovered/executed pgTAP cases with zero failures/skips, including the 27 new
producer cases. Its container was removed. This test changed no hosted project
data and did not exercise Chromium; native worker CI is a separate gate.

The additive migration was applied via Supabase MCP after this database proof.
Readback confirms four new columns, job RLS enabled, worker execute allowed,
anonymous/authenticated execute denied and zero retrospective receipts. The
security advisor has no new producer-function finding. Pre-existing notices
remain for deliberately policy-free private tables, subject-checking member
functions, Supabase's existing `rls_auto_enable` and disabled leaked-password
protection; no unrelated Auth/provider settings were changed. See
[Supabase database linter](https://supabase.com/docs/guides/database/database-linter)
and [password protection](https://supabase.com/docs/guides/auth/password-security#password-strength-and-leaked-password-protection).

The UI passes **380/380** tests, typecheck and production build locally on
Windows. Lint has zero errors and six existing component Fast Refresh warnings.
UI source/CI, core source/CI, exact private deployment, real provider account
acceptance and human/AT validation are distinct gates. This increment does not
claim hosted recordings, arbitrary-site scanning, legal eligibility or custody.
