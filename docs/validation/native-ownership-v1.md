# Native ownership validation

| Requirement | Evidence |
| --- | --- |
| Durable nonsecret ownership before native allocation | DurableIntentBindsExactOwnerJobRuntimeAndImmutableImageWithoutCapabilities |
| Exclusive coordinator until actual owner disposal | ExclusiveWorkerLockRemainsHeldUntilActualOwnerDisposes; ConcurrentNativeCoordinatorCannotAcquireRetainedOwnership |
| Actual absence acknowledged before retiring ownership | ConfirmedRemovalIsDurableBeforeIntentRetirementAndExactRetryPreservesOriginalFact; RecoveryPersistsActualAbsenceBeforeRetiringIntent |
| Invalid records cannot authorize resource operations | InvalidJournalIdentityCannotAuthorizeAnyRecovery; InvalidJournalSizeFailsBeforeAnyRuntimeOperation; IncompleteOrAmbiguousIntentRemainsUnconfirmed; FileNonceMustMatchRetainedOwnerRatherThanAuthorizingAChangedContainerName; MaximumIntentSizeStillPreservesActualBinding |
| Runtime/acknowledgement changes fail closed | ChangedRuntimeFailsClosedBeforeAnyResourceCanBeRetired; FailedRemovalAcknowledgementRetainsIntentRatherThanAllowingSilentRetry |
| Journal failures cannot invent safely stopped work | NativeOwnershipWriteFailureCannotReleaseAJobAsSafelyStopped |
| Real resource recovery preserves unrelated resources and database facts | RetainedNativeIntentRecoveryRemovesOnlyItsBoundContainer; NativeRecoveryNeedsNoDatabaseCredentialAndCannotInventObservation; MismatchedNativeOwnerFailsClosedAndRetainsIntent |
| Original actual browser/queue completion remains | RealWorkerCompletesOnlyTheAdmittedJob; ActualChromiumObservationDoesNotInventInputOrGoalVerification; ActualQueueAckRemovesCapabilityAndRetainsOneImmutableAttempt; ActualBrowserExitAndNativeDrainMeetOriginalDeadlines |

The first focused journal run passed15/15 before two additional size/runtime cases and a coordinator failure case. Local Windows-launched Linux/Podman acceptance passed15/15, failed/skipped/unconfirmed0, with owned database/image removed: artifacts/worker-linux/ec70b9e55a4b46b5b047d429e26f29c4/run.json. This includes all prior11 worker checks and four new retained-native-intent checks. The Linux CI path additionally tests actual SIGTERM and competing coordinator processes, for17 composition checks. The retained-intent sleeper scenario is controlled recovery evidence, not a claimed Chromium-process crash. Real Chromium behavior remains a separate required path.

The preceding worker increment passed all nine jobs in [CI39678180008](https://github.com/jvelezc/ai-screen-reader/actions/runs/39678180008): Windows/Ubuntu1,329/1,329 each, database161/161 and isolated worker12/12. Its database job actually used the identical pinned Docker Hub mirror after the ECR download limit; no image/assertion was weakened. Only after that gate did retained native recovery work begin. Full recovery regression and replacement CI remain required before advancing.

Journal tests use real private temporary files. They pin independent identity/error values, malformed/duplicate/missing JSON, inclusive/exclusive size bounds, immutable acknowledgement bytes, actual lock lifetime and unchanged retained intent after failure. Source/assertion review is distinct from empirical mutation testing. Hosted worker deployment, JWT, S3 durability and human/AT acceptance remain unclaimed.

Final local Windows regression discovered/executed/passed1,351, failed/skipped0: all1,329 prior identities retained,22 added. Receipt: artifacts/worker-validation/recovery-regression.trx. The earlier1,347 run also passed; four focused source-review gaps then added removal schema/false-absence, unexpected file type and null inventory cases. Strict nonincremental Release build and format verification passed. Replacement nine-job CI remains required before private deployment work advances.
