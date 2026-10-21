# Tenant scan admission validation

Run `pwsh -NoProfile -File scripts/Test-DurableDatabase.ps1 -Runtime podman` locally, or select `docker`. The independent Linux CI job runs the same harness. The receipt separates discovered, executed, passed, failed and unconfirmed cases; partial or missing TAP plans fail acceptance.

| Requirement | Evidence |
| --- | --- |
| Tenant metadata and job isolation | MemberSeesOnlyOwnOrganization; SecondTenantCannotReadFirstScan; SecondTenantCannotReadFirstJob |
| Derived actor, anonymous/missing/nonmember denial | CreatorDerivedFromAuthenticatedSubject; AnonymousCannotAdmitScan; MissingAuthenticatedSubjectCannotAdmitScan; NonmemberCannotAdmitAnotherTenantScan |
| No direct membership, scan, job or queue authority | ClientCannotGrantMembership; ClientCannotBypassAtomicAdmission; ClientCannotRewriteScan; ClientCannotForgeJob; ClientCannotDeleteJob; ClientCannotReadQueue; ClientCannotReadQueueArchive; ClientCannotEnqueueRawMessage |
| Fixed fixture/profile and required request identity | NullIdempotencyKeyRejected; NullFixtureRejected; EmptyFixtureRejected; ArbitraryTargetRejected; NullProfileRejected; UnsupportedProfileRejected |
| Idempotent receipt, creator and tenant binding | DuplicateRequestReturnsExactPriorReceipt; AnotherMemberCannotReuseCreatorRequest; SameRequestKeyInAnotherTenantCreatesIndependentScan |
| Exact message and same-tenant relationship | QueuePayloadExactlyBindsTenantScanJobAndStage; CompositeForeignKeyRejectsCrossTenantJob |
| Actual scan/job/pgmq rollback and successful retry | FailedJobWriteAbortsAdmission; FailedAdmissionLeavesNoScan; FailedAdmissionLeavesNoJob; FailedAdmissionRollsBackActualPgmqSend; FailedAdmissionRequestCanBeRetriedAfterRollback; SuccessfulRetryAddsExactlyOneMessage |
| Committed admission survives actual database restart | CommittedScanSurvivesDatabaseRestart; RestartedAdmissionReturnsOriginalReceipt; ExactlyOneBoundQueueMessageSurvivesDatabaseRestart |
| Overlapping duplicate admissions serialize and retain one message | ConcurrentDuplicateWaitsForUncommittedAdmission; ConcurrentDuplicateReturnsSameCommittedReceipt; ConcurrentDuplicateCommitsOnlyOneQueueMessage |
| Overlapping membership revocation waits, then denies prior requests | MembershipRevocationWaitsForAdmittedTransaction; WaitingRevocationRemovesExactMembership; CommittedRevocationRejectsPriorRequest |
| Revocation hides stored scans and denies idempotent admission | RevocationImmediatelyHidesRetainedScans; RevocationRejectsEvenIdempotentAdmission |

Local Windows/Podman launch passed50/50 actual SQL cases, zero failed/skipped/unconfirmed, in `artifacts/database/fa349d8426874e91aeca1c611e523cb0/run.json`. Its TAP plans are41 authorization/atomicity,3 restart and6 concurrency cases. Owned container cleanup was confirmed, with no hosted project change or worker execution. Earlier47-case runs passed before adding the three membership-race cases. The initial concurrency setup failed because Supabase's `postgres` role is not a superuser and cannot use password-free dblink; moving only the test coordinator to the disposable image's admin role resolved this while admission continued as `authenticated`.

The full local Windows .NET Release regression discovered/executed/passed1,247, failed/skipped0, preserving all1,247 prior test identities. Receipt: `artifacts/recording-validation/database-admission-regression.trx`. Formatting verification passed. The preceding documentation commit895f4ad passed all seven jobs in [CI39662547091](https://github.com/jvelezc/ai-screen-reader/actions/runs/39662547091). The database increment adds an eighth independent Linux job; its remote acceptance is pending. This evidence does not prove a hosted JWT exchange, live RLS/Data API behavior, worker restart recovery, arbitrary site execution, private S3 durability, deployment or human/AT acceptance.
