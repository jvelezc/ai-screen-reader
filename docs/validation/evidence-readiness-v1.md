# Website evidence readiness validation

2026-10-27. See [policy and wire contract](../decisions/0057-automated-website-evidence-readiness.md).

The new72 .NET cases cover pure evaluation, scoped HTTP, original-file storage
verification and private MCP. The existing real Chromium/stdio test now also
discovers and calls `evidence_assess` against an actual core HTTP server. Its
provider identity is synthetic; it is not original-user/RLS acceptance.

| Requirement | Evidence |
| --- | --- |
| All eight requirements and three honest decisions | `EveryRequiredConditionCanIndependentlyPreventReadiness`; `ExamplesRemainIllustrativeAndNeverAuthorizeARealWebsitePackage` |
| Unknown URL generates no finding | `UrlOnlyReturnsScanRequiredWithoutInventingAnAssessment` |
| Declared hashes do not prove originals | `DeclaredHashesAndBooleanCaptureFlagsCannotVerifyOriginalArtifacts`; `ReadinessRequiresAuthorizedOriginalBytesRatherThanDeclaredHashes` |
| Actual original reconstruction, bounds and cancellation | `WholeOriginalVerificationReadsEachObjectOnceAndRejectsCorruptionAndCancellation` |
| Engine/policy/partial/attempted input is not a documented barrier | `EnginePolicyAndUnclassifiedFailuresNeverBecomeDocumentedBarriers`; `IncompleteCollectionCannotBecomeReadyOrNoBarrier`; `AttemptedOrUnknownInputIsNotACompletedFailureTrace` |
| Distinct same-task repeat and pointer evidence | `RepetitionCountsCannotReplaceDistinctSameTaskAttemptEvents`; `ForeignTaskAndPointerEventsCannotSupportKeyboardBarrierOrComparator` |
| Strict raw JSON and closed source/target selection | `RawImportsRejectAmbiguityOversizeAndSourceConfusion`; `MalformedAndOverBudgetRecordsAreRejected` |
| Original-user/workspace authority before evaluation and after reads | `AuthenticationAndCurrentWorkspacePrecedeEvaluationOrArchiveReads`; `RevocationDuringOriginalReadCannotReleaseAnAssessment` |
| Larger assessment body does not relax other routes | `LargeRecordRouteDoesNotRelaxOtherControlBodyLimits` |
| HTTP examples, published contract and safe caching | `AuthorizedHttpExamplesMatchPublishedContractAndNeverAuthorizeRealEvidence` |
| MCP scope, result validation and credential exclusion | `PrivateMcpAssessmentUsesOriginalCallerAndCoreDecision`; `PrivateMcpRejectsForgedOrForeignAssessmentResponses`; `HostedStdioUsesRealApiChromiumPagesGraphAndConfirmedClose` |
| Reviewed ingress replacement, rollback and concurrent-change denial | `test_replace_checks_previous_hash_and_preserves_unrelated_configuration`; `test_replace_validation_failure_restores_previous_owned_bytes`; `test_replace_does_not_overwrite_concurrent_owned_change_during_rollback` |

Linux native ingress validation passed25/25 on the VPS using temporary test
directories and injected Caddy callbacks. This verifies real Linux file ownership
and compare/replace/rollback behavior; it does not attest a live reload or TLS.
Local Windows discovers and passes **2,271/2,271**, zero failed/skipped, parsed
from the final TRX. The strict nonincremental Release build passes with0
warnings/errors; full `dotnet format --verify-no-changes --no-restore` passes.
Windows/Linux CI, exact private deployment, live Caddy replacement and
original-account Edge/browser acceptance remain separate gates until recorded.

An intermediate full run passed2,270/2,271; its only mismatch was the expected
MCP catalogue lacking the new tool. That assertion now includes and calls the
tool. A later run was invalidated by an overlapping clean rebuild that deleted
Playwright runtime files while tests were active; its64 browser failures are
retained locally and are not product acceptance. Verification is rerun sequentially
after restoring the full build. No test is skipped or weakened.

No live arbitrary-site scanner, queue-produced recording/attempt linkage,
human/NVDA comparison, original media authenticity, custody attestation, judicial
admissibility or plaintiff-specific legal element is established by these cases.
