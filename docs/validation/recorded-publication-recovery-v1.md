# Original-attempt storage publication recovery

October31,2026. Implements [decision0070](../decisions/0070-recorded-publication-recovery.md).
Source state and acceptance gates remain distinct. HTTP1.12/eighteen private MCP
tools and existing UI f7c1127 / Edge9 are unchanged. Automatic restart is disabled.

| Requirement | Named evidence |
| --- | --- |
| Actual Chromium originals retain the original job, producer, bundle and goal uncertainty | `ActualChromiumOriginalCanPublishThroughStorageOnlyAuthorityWithoutNewInputOrChangedProducer` |
| Busy/rejected/already-completed states do not repeat storage or input | `NonclaimsDoNotWriteStorageOrRenewInput` |
| Missing cleanup or substituted producer cannot authorize publication | `UnconfirmedCleanupCannotAcquirePublicationAuthority`, `ClaimCannotSubstituteTheOriginalProducer` |
| Initial and all storage-boundary authority checks remain required | `DeniedInitialFenceDoesNotCreateLocalOrRemotePublication`, `AuthorityIsRequiredBeforeAndAfterEveryStorageFinalizationBoundary` |
| Only exact existing bytes/indexes may be reused; normal persistence stays immutable | `ExactExistingObservationAndArchiveCanBeReusedButNormalPersistenceStillCannotReplaceThem`, `RecoveryCannotReplaceDifferentExistingObservationData` |
| Source changes and linked paths cannot reach finalization | `SourceChangedAfterClaimCannotReachLocalOrRemoteFinalization`, `RecoveryDirectoryCannotFollowALinkOutsideTheOriginalObservationPath` |
| Storage failures and rejected/lost database completion remain unconfirmed | `StorageFailureRetainsOriginalsAndVisibleFailureWithoutCompletion`, `WrittenOriginalsCannotSubstituteForAnAcceptedDatabaseCompletion` |
| Stop and heartbeat loss join the actual storage callback | `CancellationJoinsActualStorageBeforeRecordingSafeFailure`, `LostHeartbeatDoesNotReleaseOwnershipBeforeActualStorageSettles` |
| Five-second renewal boundary and in-flight renewal join use deterministic fake time | `FiveSecondHeartbeatMustSettleBeforePublicationEvenWhenStorageReturns` |
| Capabilities, private errors and external database TLS remain protected | `ClaimFailureCannotExposePrivateDatabaseDetailsOrWriteStorage`, `PublicationCapabilityAndResultDoNotSerializeASecretOrReplayAuthority`, `PublicationDatabaseRequiresCredentialsAndVerifiedExternalTls` |
| Private source selectors cannot cross attempts | `PrivateCandidateSelectorsCannotSelectAnotherAttempt` |
| Separate database capabilities preserve execution tokens and provenance | `PublicationTokenCannotRenewExecution`, `ExecutionTokenCannotRenewPublication`, `PublicationPreservesOriginalExecutionAttemptExactly` |
| Original membership, route, source, receipt and completed-state fences remain current | `PublicationRenewalRechecksOriginalCreatorMembership`, `RevokedMembershipCannotCompletePublication`, `PublicationCannotChangeTheRecordedRouteAfterClaim`, `CompletedPublicationCannotBeReopenedByAnInconsistentJobState`, `CompletedPublicationCannotOverwriteArchiveIndex`, `CompletedPublicationRejectsANullCapabilityExplicitly` |
| Concurrent claims and actual post-lock clock checks cannot revive authority | `ConcurrentPublicationCannotIssueASecondStorageCapability`, `PublicationRenewalCannotUseAPrelockTimestampToReviveExpiredAuthority` |
| Real archive-before-metadata failure retains complete media | `CompleteOriginalSurvivesActualArchiveBeforeDatabaseCommitFailure` |
| Native recovery needs no browser configuration and preserves original input history | `StorageOnlyRecoveryPublishesTheSameOriginalProducerAndBundleWithoutBrowserConfiguration`, `ActualRecoveryPreservesInputHistoryAndAttemptWithoutNativeReplay` |
| Independently finalized archive and database receipt match; exact retry cannot overwrite | `RecoveredDatabaseReceiptMatchesTheIndependentlyFinalizedOriginalArchive`, `LostPublicationResponseReconcilesWithoutReplacingCompletedReceipt` |

Local focused Windows tests pass122/122, including31 new publication cases;
all named cases appear in the actual TRX. Disposable pinned PostgreSQL passes
361/361, failed/unconfirmed/skipped0:64 new capability/RLS/failure cases and
eight actual concurrent-connection/expiry cases. Its container is removed and
the hosted project is unchanged. The first capability pass was338/338. Added
boundary checks then caught a real NULL-versus-false completed-token result;
the corrected function rejects it explicitly. An accompanying test-query syntax
error was fixed without relaxing the permission check. Final review also binds
the admitted route throughout publication and rejects reopening a completed
publication even if privileged job metadata becomes inconsistent. Its test
initially hit the existing archive/state constraint; the fixture now explicitly
models an inconsistent cleared job receipt and restores its exact prior fields.

Windows-host Podman passes65/65 actual native requirements, failed/unconfirmed0,
including the six new publication checks. Its actual isolated Chromium task
finishes the archive; a trigger scoped to that disposable job makes the database
completion fail. Recovery first rejects the still-active execution lease, then
publishes from the same retained bundle after explicit expiry. Browser image/
runtime configuration is removed for recovery. Input checkpoints, source files,
original execution attempt, baseline and archive hashes remain unchanged;
native containers are independently absent and the database/image are removed.
This is a real local archive-before-database failure. It is not a claimed
Supabase network outage or hosted tenant acceptance. Two Linux-only signal
requirements are outside this Windows-host native path; Linux CI requires67.

The first native pass failed a later legacy fixture check because it hard-coded
the former number of admitted jobs. That assertion now compares the actual
database snapshot across credential-free native cleanup, and the full65-case
run passes. Its earlier failed receipt is retained separately.

The first full Windows run executes2,713:2,711 pass and two CLI subprocess tests
fail after overlapping Linux-native packaging removes the Windows Playwright
executable from shared CLI output. Their failed TRX is retained. A strict normal
Windows build restores the executable; both exact affected tests then pass2/2.
The sequential full Windows rerun passes2,713/2,713, executed2,713, failed/skipped0,
including all31 new publication cases. Strict non-incremental Release build has
zero warnings/errors; format verification exits0. Initializer whitespace was
corrected without changing behavior.

The additive migration is applied to the intended hosted project as
`20261031085938_recorded_publication_fence`. The CLI-created unpublished draft
was renamed to this actual server receipt before its first source commit;
published migration history was not rewritten. Readback confirms both tables
enforce RLS, only the worker role can claim publication, caller roles cannot
invoke it, and neither callers nor workers can read the capability table or
invoke the private helper. The active helper binds the recorded route. There
are zero publication rows and zero queued/leased jobs at this pre-acceptance
snapshot. Advisors report no new warning for these objects; the new private
capability table has the intentional deny-all/no-policy informational finding.
Existing unrelated advisor findings are unchanged. CI, exact deployment and
original-account recovery acceptance remain separately pending.

Do not infer publication from retained progress, a saved bundle, copied cleanup
history or a matching hash alone. This increment requires validated complete
original output, actual admitted producer, storage readback and an accepted
database fence. It does not recover unfinished child media, demonstrate every
full-disk/cancellation/provider fault or establish a website barrier, custody,
standing or legal sufficiency. A Published receipt retains overall NotAssessed.
