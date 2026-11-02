# Evidence publication failure diagnostics v1

November 1, 2026. Product: Privensic Screen Reader. **In progress; not delivered.**
The current private core remains734c761/HTTP1.15. Actual
[upload-before-metadata](hosted-upload-before-metadata-v1.md) and
[worker-to-storage denial](hosted-storage-outage-v1.md) recovery are accepted in
their named scopes. Their originals and generic historical failure codes remain
unchanged.

## Purpose

After capture has returned and actual evidence persistence begins, retain a known
closed storage/publication failure in the original durable job. The caller should
see why publication is unavailable or invalid and the safe next step. A code
cannot establish retained originals, recoverability, a completed input/goal or an
accessibility barrier. Do not repeat target input to repair evidence publication.

## Implementation and verification

Shared Domain codes classify unavailable storage, unconfirmed write/readback,
invalid content and missing original. Worker mapping applies only during actual
persistence and only to defined typed archive failures. Native cleanup uncertainty,
caller stop and lost heartbeat retain their priorities; database acknowledgement
failure stays separate. Unknown codes/enum values and collection errors cannot
become a storage diagnosis.

HTTP1.16 reports a bound blocked publication as Unavailable or Invalid, without
an observation, archive or worker-permission receipt. Private MCP gives a same-scan
originals inspection/storage-only recovery action with `CanReplay:false`; it
does not promise a retained bundle or authorize overwriting conflicting bytes.
Older generic blocked jobs remain NotCollected. SQL must retain the exact existing
lease/tenant/attempt/cancellation capability fence and worker-only ACL.

The first regression run has3 expected failures against the old coordinator.
After repair,251 focused cases and the full Windows suite pass:3,270 discovered,
executed and passed, failed/skipped0, including58 new cases and3 updated original
regressions. Fresh TRX counters, unique execution IDs and all58 new identities
are parsed. The full run takes8m53s. A strict no-incremental build has0 warnings/
errors, and format verification passes. Directory.Build.targets checks dependencies.

The pinned Linux PostgreSQL harness is Ready501/501,18 new, failed/skipped/
unconfirmed0; its owned container is removed. The in-process ad-hoc PowerShell
wrapper captures125 from the expected final missing-container inspection; the
script returns normally and independent run.json/TAP prove all501 plus cleanup.
CI invokes the script as its own PowerShell process. This discrepancy is retained.

The separate UI is committed ate1d1571: local frozen Bun773/typecheck/lint/build
pass, with the same six existing lint warnings. Both Windows/Linux jobs in
CI41397126683 pass773 each. Existing Edge11 remains unchanged. Core CI/deployment
and fresh original-account diagnostics remain separate gates.

The tested migration is installed through Supabase MCP as observed version
`20261102061200`; the CLI-created source filename is aligned to that actual
version, with identical SQL bytes. The preceding permission migration filename
is also aligned to its observed`20261102032301` history record without SQL replay.
All14 local names/versions now match hosted history; no history row is rewritten.
Independent catalog checks confirm four closed codes, the existing active-job
capability fence, empty search path, worker execute and authenticated/anonymous
denial. Installed function definition SHA-256:
`e7bb7f0e99a3aae816117c94c4af41d5e43cebd73b7914ac376b8ac22729b308`.
The security advisor still reports the existing four private RLS-without-policy
tables, scoped authenticated definer functions, public rls_auto_enable warning
and disabled leaked-password protection. No application/anonymous execute grant
is added by this migration; these unrelated findings are not claimed resolved.

| Requirement | Evidence |
| --- | --- |
| Closed cause, no inferred originals | `ClosedPublicationCodePreservesItsMeaningWithoutClaimingOriginals`, unknown-code and undefined-kind cases |
| Exact durable cause and retained bytes | `RemoteFailureRetainsLocalBytesAndBlocksWithoutInventingCompletion`, `ProducerPublicationFailureKeepsSpecificCauseAndOriginalWithoutCompletion` |
| Correct phase and priority | `ArchiveLikeCollectionErrorCannotClaimThatPublicationStarted`, `CallerStopOverridesTypedPublicationFailureAfterActualCallbackSettlement`, `LostHeartbeatOutranksTypedPublicationCauseAfterStorageSettles` |
| Scoped report and schema | `BlockedPublicationPreservesKnownCauseWithoutClaimingRetainedOriginals`, `ActualHttpPublicationFailureMatchesItsSchemaWithoutObservationOrReceipt`, `PublishedSchemaRejectsContradictoryPublicationFacts` |
| Conservative private MCP | `KnownPublicationFailureGivesSameOriginalRecoveryAdviceWithoutReplay`, `ContradictoryPublicationCauseCannotAdvertiseSafeRecovery`, `HistoricalGenericFailureIsNotReclassifiedAsStorageFailure` |
| Original SQL fences |18 pgTAP cases, including `PublicationCauseCannotBypassCapabilityFence`, `PublicationCauseCannotResurrectExpiredAuthority`, `CommittedCallerCancellationOutranksPublicationCause` |

The first expanded schema check catches invalid-queued after an unsupported
anyOf clause is introduced into the bounded test evaluator's emitted subset.
Equivalent enum/if/allOf clauses retain rejection; actual HTTP success shapes and
six new contradictions pass. No assertion or evaluator is weakened.

## Done when

Meaningful deterministic and actual Chromium checks, pinned database fencing,
strict build/format and Windows/Linux CI pass. Verify the exact private release,
then a fresh bounded original-account storage failure shows the specific cause
and safe action. Recovery must preserve the same producer/generation/bundle with
zero input; normal graph/video capacity still works. Preserve the earlier raw
observer classification issue and failed trials. Provider authority loss,
external collection, intervention, operations and human/AT remain separate.
