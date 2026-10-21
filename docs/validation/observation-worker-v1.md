# Observation worker validation

Run the actual PostgreSQL/pgmq and isolated Chromium acceptance from the repository root:

```powershell
pwsh -NoProfile -File scripts/Test-ObservationWorker.ps1 -Runtime podman
```

Use `-Runtime docker` on Docker hosts. No hosted credentials are needed. Synthetic users are created only in its disposable database. That database publishes only an ephemeral127.0.0.1 port; the separate browser container has networknone. Do not publish while native tests run. After Linux publishing on Windows, restore Windows Playwright outputs before native tests.

| Requirement | Evidence |
| --- | --- |
| Nonsecret bound descriptors and canonical paths | DescriptorRejectsMissingIdentityAndUnsupportedAttempts; DescriptorBuildsExactTenantScanJobGenerationReference; WorkerRejectsInvalidOrExtendedDescriptorBeforeBrowserLaunch; WorkerRejectsOversizedDescriptorWithoutOutput |
| Real initial observation, privacy and native shutdown | RealChromiumWorkerObservesBoundedFixtureWithoutInputThenConfirmsOwnedExit; ActualChromiumObservationDoesNotInventInputOrGoalVerification; ActualBrowserExitAndNativeDrainMeetOriginalDeadlines; StoredFixtureObservationExcludesEditableCanariesAndCapabilities |
| Exact receipt binding/profile and inclusive size/node limits | ReceiptCannotChangeBindingIdentitySemanticsOrObservationClaim; ObservationNodeLimitIsInclusiveAndRejectsOverflow; ReceiptByteBoundsRejectEmptyAndOversizedPayload; ReceiptByteLimitAcceptsExactMaximumAndRejectsUnknownFields |
| Explicit shutdown facts and original deadlines | ReceiptRejectsEveryUnconfirmedShutdownBoundary; ReceiptAcceptsTimelyActualExitAndSettledNativeBoundary; ReceiptRejectsNegativeAndLateExitEvidence; ReceiptRequiresExplicitShutdownFactsEvenWhenDefaultWouldLookSuccessful; ReceiptCannotInventOmittedRequiredObservations; DuplicateJsonClaimsAreRejectedRatherThanSelectingOneValue |
| Original private bytes, immutable publication and readback hash | PersistedBytesAreExactHashedImmutableAndHaveNoPendingFiles; CompletionRequiresPreviouslyPersistedExactBytesAndRetainsRejectedReceipt; PersistedObservationBindsActualTenantScanJobAndAttempt; DatabaseCompletionAcknowledgesExactPersistedBytes |
| Secret exclusion, known runtime/image and remote TLS | RuntimeChildEnvironmentDoesNotCarryDatabaseOrSupabaseSecrets; RuntimeRequiresKnownEngineAndImmutableLocalImage; LeaseSerializationAndDiagnosticTextDoNotExposeCapability; DatabaseConfigurationRequiresExplicitCredentialAndVerifiedTlsAwayFromLoopback |
| Authority before resource allocation | LostInitialAuthorityNeverAllocatesBrowserResourcesOrReleasesJob; ActualNativeAdapterRenewsCurrentServerCapabilityBeforeExecution; NonclaimsDoNotLaunchBrowserOrInventCompletion |
| Lease loss/stop join actual cleanup and renewal | LostHeartbeatCancelsExecutionAndJoinsActualCleanupWithoutCompletionOrRelease; ActualRenewalMustSettleBeforeCompletionEvenAfterBrowserReturns; RequestedStopWaitsForActualCleanupBeforeRecordingSafeBlock |
| Explicit safe engine/database failures | InvalidObservationBlocksSafelyButUnconfirmedCleanupRetainsOwnership; DatabaseFailuresRemainUnconfirmedAndDoNotLeakPrivateErrors; MissingImmutableImageProducesSafeBlockedEngineOutcome; ActualNativeBlockArchivesMessageWithoutInventingObservation |
| Actual queue and immutable attempt state after cleanup | RealWorkerCompletesOnlyTheAdmittedJob; ActualQueueAckRemovesCapabilityAndRetainsOneImmutableAttempt; OwnedObservationContainerIsRemovedBeforeCompletion |

The first focused52-case run found two assertion errors: record equality compared immutable-array identity after JSON deserialization. Corrected assertions check job/shutdown and actual node contents. Focused63-case and77-case runs then passed. Final authority/stop/database-failure cases extend these. The first two real Linux/Podman runs passed8/8. The expanded runner passed11/11, failed/skipped/unconfirmed0, owned database/image removed: `artifacts/worker-linux/aa933a774e0e49cda97bb5ea9af8e9ce/run.json`. All four native PostgreSQL adapter operations reached the actual narrow login. Full native regression and nine-job CI acceptance will be recorded after completion.

The preceding lease increment passed all eight jobs in [CI39673534348](https://github.com/jvelezc/ai-screen-reader/actions/runs/39673534348): Windows/Ubuntu1,247/1,247 each and database161/161. Only then was its unchanged SQL installed through the supported migration API. Supabase recorded20261021210027; the repository filename now matches. Source SHA256: ED2E3EB0A464188C4DB234506EE85BBF693A0C1E2A7AC75BDDC4F635753793D0. Readback confirmed worker loginfalse, worker claimtrue, client claimfalse, worker token-table readfalse, all application tablesRLStrue and zero hosted attempts/scans.

These proofs do not claim hosted native execution, JWT onboarding, crash recovery, S3 durability, public MCP/plugin publication, human viewer acceptance or fresh assistive-technology validation. Source/assertion review is distinct from empirical mutation testing; no mutation-engine result is claimed.

Final local Windows regression: discovered/executed/passed1,329, failed/skipped0; all1,247 prior test identities retained and82 added. Receipt: artifacts/worker-validation/worker-regression.trx. Strict nonincremental Release build and full formatting verification passed. Linux CI additionally exercises ActualLinuxWorkerHonorsSigtermWithoutAutomaticRestart as its12th composition check. Native worker CI/deployment remain pending at this commit.
