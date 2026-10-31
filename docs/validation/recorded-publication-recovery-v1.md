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
original-account recovery acceptance were completed as follows.

## CI, deployment and original-account acceptance

Core `d14499332e648176245f34bd295d2c957a2fa660` passes all nine jobs in
[CI41186968109](https://github.com/jvelezc/ai-screen-reader/actions/runs/41186968109).
Downloaded TRX reports independently confirm Windows and Ubuntu each discover,
execute and pass2,713/2,713, failed/skipped0. The actual native Linux receipt
passes67/67 and PostgreSQL361/361, failed/unconfirmed/skipped0; native database/
image and database-proof container are removed. Exact
[private deployment41188274025](https://github.com/jvelezc/ai-screen-reader/actions/runs/41188274025)
is Ready. Independent checks match151 release files, default trusted TLS,
HTTP1.12 schema references and anonymous protected-read denials.

The existing signed-in account admitted one new Checkout preview recording
through UI f7c1127 → existing Edge9 → core. No replacement account, privileged
admission or fresh sign-in was used. Scan `b2b291c2-52bd-48c8-a3db-cd30edf5f5c9`,
job `2421ec9a-198e-42f1-8826-70e81ce40426`, generation1, actual producer
`f27d4e39-764e-4f0e-8ffa-f4ec75e95acf` remained bound throughout.

A scoped empty directory at only that new job's baseline destination caused an
actual local persistence failure after recording and native cleanup. The account
observed Blocked/NotCollected with `FIXTURE_OBSERVATION_UNCONFIRMED`; there was
one execution attempt and no archive/database completion. The complete retained
bundle was955,559 bytes, SHA-256
`239d960c03f199044fac2d0e2383210f0d7159fcd1fc2c5e361d9aa3f47856fc`.
The matching durable native-cleanup acknowledgement is retained. An early
independent check used root's Podman namespace, which cannot establish absence
in the worker's rootless namespace; that receipt is insufficient for this claim.
A subsequent fresh check runs Podman as the actual hsr-worker account(uid1001),
using its runtime environment, and confirms the original owned container is absent.
It does not retroactively establish fresh rootless absence at the earlier instant.

After removing only the empty test directory, the explicit command ran under
the existing worker identity/private environment and returned Published. An
exact retry returned AlreadyCompleted. The execution-attempt fingerprint,
generation, producer, complete stage inventory and every progress-file hash
were unchanged; no replacement input attempt was created. The publication audit
contains exactly Claim/Completed. Independent signed private S3 reads verified
all five original files and each stored part, the index, producer manifest and
the separate baseline. The archive totals956,155 bytes. These checks used the
existing private S3 pair without exposing credentials.

The original account then refreshed Observed/Available, verified the same five
originals, and selected the recovered graph's Enter edge. The actual retained
journey has three states/two edges and two attempted/completed calls. Its2.68s
original video loads with readyState4 and seeks to the selected interval; this
does not claim actual playback or NVDA speech. Baseline inputs remain separately
zero, baseline objective false, and recovery overall goal NotAssessed. Both
services are restored and protected HTTP/TLS/release checks pass again.

The test also exposed an operator ordering gap: stopping observation stops its
dependent control service; starting both together can launch control before the
worker reader host is ready. ActiveState alone did not establish readiness. The
API was restored by starting control after actual worker readiness. Deployment's
existing `DeploymentRuntime.start_ready` already enforces the ordered checks;
the next operator increment must reuse it for maintenance recovery. An initial
transient command also omitted the non-secret `HSR_WORKER_EVIDENCE` setting;
it failed closed before creating a publication claim, then succeeded with the
complete existing worker environment. The failed setup/read receipts are retained.

This hosted proof is recovery after a scoped local storage failure with real
S3 publication. It is not a Supabase network outage, full-disk test, unfinished
child-media recovery or provider-authority-loss acceptance. The original files
were preserved. Automatic worker restart and public MCP/plugin exposure remain
disabled/deferred.

Do not infer publication from retained progress, a saved bundle, copied cleanup
history or a matching hash alone. This increment requires validated complete
original output, actual admitted producer, storage readback and an accepted
database fence. It does not recover unfinished child media, demonstrate every
full-disk/cancellation/provider fault or establish a website barrier, custody,
standing or legal sufficiency. A Published receipt retains overall NotAssessed.
