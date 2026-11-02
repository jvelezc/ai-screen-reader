# Evidence publication failure diagnostics v1

November 1, 2026. Product: Privensic Screen Reader. **Accepted in the named scope.**
The current private core is38ba353/HTTP1.16, with21 private MCP tools. Actual
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
and fresh original-account diagnostics are independently verified below.

Core`38ba353` is committed to main. CI41399140599 attempt1 passes all eight
other jobs, including Ubuntu3,270/3,270, native71, database501 and operations172.
Windows executes3,270:3,269 pass and1 fails. Its existing
`NarrowViewportPreservesProductNameWhenTextIsEnlarged` fails at local-page
navigation with`net::ERR_NO_BUFFER_SPACE`, before any layout assertion. The full
raw log and both original TRX files are retained; each has3,270 unique execution
IDs. A focused unchanged local Chromium reproduction passes1/1. One unchanged
failed Windows job rerun on a fresh runner passes all3,270, failed/skipped0,
with3,270 unique execution IDs and the same layout test present. All nine CI jobs
pass at attempt2. The underlying initial buffer allocation cause remains unproven.
No assertion or source change is made for the rerun. Private deployment41403509099
is Ready, receipt`f43502e341a241df969dcf2f9849f783`. Independent readback verifies
all151 installed hashes, the exact rootless image, default trusted TLS, HTTP1.16
and its four publication schemas. Root/MCP/config remain404 and anonymous
session/report/cancellation remain401. Restart stays disabled.

The downloaded CI release has151 individually verified files, exact commit
`38ba35368f6b82996f27baa81b56660783c207e2`, manifest SHA-256
`842e2e8143dde100d07fe1fd64234f7cf06a1e208fe6a4afc5f7ee13886fdebd`,
image`sha256:b271780ddfbf4235ff518f9944daeb265deb1373dc9d6e51da6547b1319e4514`
and1,621,848,751-byte archive SHA-256
`d0e6561f7c661f38fe8a8380a27a10a9c56520ba23e7155a8f9763208e22d67c`.
Package verification does not establish installation or customer acceptance.

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

## Actual original-account failure and recovery

The existing signed-in application account makes fresh requests through UIe1d1571
and existing Edge11 to core38ba353. No replacement identity or sign-in prompt is
used. One route1 admission produces scan`3aaf24db-3dee-409e-aaad-823913bc3ef8`,
job`23725c64-f66e-461b-a36d-07f39b2da747`, original producer
`938ed115-3590-46ae-88fd-5ac58705e9d6`, generation1 and one attempt.

The bounded observer waits for sealed complete output, actual owned native
removal and5 saved records/2 intents/2 results before denying only this worker
service's TCP443 traffic to the resolved storage addresses. Parent freeze lasts
0.939946s. The requested25s fault lasts25.145804s including cleanup;2 packets/
120 bytes match. Independent removal/thaw timers bound the intervention.
The worker stays active under the same invocation. The observer reports
`ScopedStorageNetworkFaultReleased`, `WorkerActiveState:active` and
`WorkerStopped:false`; the earlier observer's raw classification remains unchanged.

Before recovery, the job is blocked with
`WORKER_PUBLICATION_STORAGE_UNAVAILABLE`, no lease, result or archive receipt.
Independent signed storage reads find0 job objects and a404 baseline. The
original-account report shows Unavailable, actual inputs/objective Unconfirmed,
the specific storage cause and same-original/no-repeat advice. It explicitly says
that a failure code cannot prove retained or recoverable files. A fresh diagnostic
read shows5 checkpoints/2 intents/2 results, last command Satisfied, overall goal
NotAssessed and no replay permission.

Independent host inspection verifies109 committed images/1,551,471 bytes, with
no uncommitted files. All115 retained files/1,573,908 bytes match before and after:
inventory SHA-256`232a8c4f7453e0f83913ec1b9eb1bc31a30b9666269dffcc4d5a01db1bf89da2`.
The1,876,194-byte bundle remains
`64c861b1a08b8e5efc8da75496e8e7ee258ea8d09e0e95844f6589f31d25bd0a`.
Independent denial absence and all126 exact owned guard checks pass.

The installed maintenance wrapper returns Published for that same producer/job/
generation/bundle,0 dispatched inputs, `CanReplay:false`, completion receipt true,
overall goal NotAssessed and services Ready. The database then completes that
one attempt. All5 storage originals/1,876,790 bytes read back and match every part,
baseline and ZIP source. Index:
`546b1105f236af5202c454cf84b238f0f022be6db3ae49d76467587830766338`;
manifest:`89c33c0bd9361b02df5d9db84991f862372a53f4901ee91a47d68e22533c6466`;
baseline:`d78f6ef464ec53ec8ca6688156daff8aebeb58b9a79dbf9b80f905de607c0721`.
Actual inspection decodes135/135 video frames and retains3 nodes/2 edges/2 keys,
Reached/NoneObserved. The original account verifies all5 originals, selects a2
and plays the actual video to Ended5.40s/errornull. Selection sends no live input.

## Separate normal capacity

After maintenance, a distinct original-account route1 scan
`cc6c0e31-c8fd-42d5-9c97-bd22ee724dbc`, job
`a38a3093-c818-4252-afc8-4e64de57a074`, producer
`bdaf10de-a7db-40d4-9c49-ad458487bfbb` completes generation1/one attempt/no lease.
All5 originals/1,878,701 bytes match storage readback and ZIP contents. Index:
`2a6f2f5d887f593f4daf53dfa2e317d0f01d43a9bd02597da76e24e44012261f`;
manifest:`05b0fec3c5a71c4efe406890be86b0c879a741d4f642900251b4504d959d11ee`;
baseline:`540c70b636aafaf2ae10fea8d514e601d94ec12083230b3f4d964ee817fc2176`.
It retains110 source images,136/136 decoded video frames,3 nodes/2 edges/2 keys,
Reached/NoneObserved and no unresolved attempt. The original notebook verifies
all5 originals, selects a2 and plays to Ended5.44s/errornull. Native0 and healthy,
unfrozen services are independently verified under the same post-maintenance
invocations; automatic restart remains disabled.

Actual hosted acceptance covers StorageUnavailable. The other three closed
causes have deterministic API/MCP/schema/consumer/database checks; no new actual
hosted corruption or missing-original scenario is claimed. This is controlled
fixture acceptance, not external website collection, human/AT usability, custody
or legal acceptance. Real provider expiry/revocation is the next separate gate.

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

## Acceptance criteria

Meaningful deterministic and actual Chromium checks, pinned database fencing,
strict build/format and Windows/Linux CI pass. Verify the exact private release,
then a fresh bounded original-account storage failure shows the specific cause
and safe action. Recovery must preserve the same producer/generation/bundle with
zero input; normal graph/video capacity still works. Preserve the earlier raw
observer classification issue and failed trials. Provider authority loss,
external collection, intervention, operations and human/AT remain separate.
