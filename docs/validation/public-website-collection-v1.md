# Public website collection: durable local worker slice

November2,2026. The customer release gate is [sign in → URL → saved results](../product-access.md).
This increment implements canonical URL admission, scoped durable work and actual
isolated collection. The API/MCP, existing Edge, customer form and public UI origin
remain to be connected. The tested basic public-collection migration is now hosted,
and a5817a5 is privately promoted after the VPS readiness repair described below.
Automatic private promotion remains held while the next API increment is developed.

## Retained facts and their limits

`hsr-approved-public-readonly-v1` uses the existing target policy and external
egress isolation. It accepts a canonical credential-free HTTPS DNS source, binds
organization/scan/job/generation and actual producer, and collects two actual
same-document accessibility snapshots. The worker confirms native shutdown and
absence of all three containers and two networks before returning collected data.
Dispatched inputs are0 and goal verification isfalse. Task success, a blocking
barrier, video, comparison/repeat support and evidence sufficiency remain unassessed.
An engine/policy/cleanup failure cannot become a website barrier.

The separate public queue and tables preserve Foundation fixture provenance.
Authentication membership governs admission and reads. Private lease capabilities
are never returned to consumers. A workspace has at most50 queued/leased jobs;
the trusted coordinator permits two leased public jobs globally and one per
workspace. Matching request IDs reconcile the same caller/source at quota.
Expired work blocks with its original attempt; it never automatically replays.
Complete native-inventory recovery precedes the next claim.

The parent renews authority during collection and immediately before publication.
SQL rechecks the exact lease, generation, worker, original creator membership and
fresh expiry after actual row/membership/queue locks. Missing queue records deny
renewal, completion and retirement. Known locked jobs defer without archiving.
Cleanup uncertainty halts polling and retains ownership/capacity.

Original UTF-8 observation JSON, its SHA-256 and producer are atomically retained
in PostgreSQL with job completion and queue-capability retirement. The new
observation profile has a1MiB JSON ceiling; this does not change the private
`hsr-evidence` bucket's25MiB limit or existing media originals. Strict C# decoding
checks exact bytes, required/unknown/duplicate members, tenant/source/producer and
passive outcome before use. A matching hash cannot authorize invented facts.

## Actual local receipts

The packaged private worker image
`sha256:6074972f13418995e8caeb54019f5dcf2b81eaeff33b428b6bd8ad98c1085cb7`
passes `--check-public-runtime` without database configuration. It opens
`https://screenreader.privensic.com/ci/v1/source`, returns two9-node captures,
confirms same document, native shutdown and owned resource absence, and persists
no observation. PowerShell launchers, the seccomp policy, firewall and egress
runtime are included in the tested package.

`Test-PublicWebsiteCollection.ps1`, run81379e72fb004c279a937f8648d7f0a2,
uses that actual image and a disposable migrated PostgreSQL instance. A synthetic
local authenticated membership admits one job. The real Npgsql client, coordinator
and isolated browser retain two9-node captures and22390 original JSON bytes;
independent SQL confirms the digest, producer and exact organization/job. The queue,
private lease and ownership intents end at0. Independent native inspection confirms
all five owned resource names absent. The disposable database is removed. This is
actual local composition; it is not original-account hosted acceptance.

The separate database suite discovers/executes/passes631, failed/unconfirmed0,
run4a0daa43087a4170b60b3f40ffeaba4a. Twenty-four new concurrent cases use actual
worker connections and queue-row lock waits. They prove renewal/completion/block
reject expiry after waiting, retain unfinished metadata, reject missing queue
records and defer known busy jobs without archiving. An earlier test setup kept
writes in an uncommitted coordinator transaction; its wait assertions correctly
failed. Setup now commits on the other connection before the worker starts.

The final native run6d1bd8e1821a4cc2b3779a1f41c16366 repeats the same actual
collection and independently enumerates remaining immutable database-container
IDs after removal; absence is not inferred from a failed inspection command.

Linux operational checks discover/execute/pass180, failed/skipped0, using the
actual Podman Linux VM. Deployment readiness requires both the existing fixture
probe and the new public-source probe when the trusted manifest carries its
launcher. Public-journal recovery is a separate narrow operator mode and preserves
unconfirmed intents. Both operator modes exclude database/storage credentials.
Focused Domain/codec/coordinator checks pass94. The full Windows solution discovers,
executes and passes3613, failed/not-executed0, in8m46s. A final nonincremental
Release build has0 warnings/errors and the required format verification is clean.
All nine [CI41521635045 jobs](https://github.com/jvelezc/ai-screen-reader/actions/runs/41521635045)
now pass for e7f5c74: Windows/Ubuntu3613 each, native72, database631 and
operations180. Actual Docker public run e44358afc41b47299bfd8b947242967f uses
image `sha256:a161a4fc1fcb7359cff4e1ed17a73efd8764994b627057b2225143df1167526d`,
retains the real source captures and confirms native/database absence.

The root-owned operator helper is installed from that tested commit, with its
previous bytes backed up. All nine installed module hashes match. The hosted
basic schema and private worker are promoted; there is still no public customer UI.

### Current correction: one bounded worker pool

Hosted read-only metadata confirms the worker login's connection limit is4.
Independent fixture/public data sources could each reserve a pool of3 connections.
The process now owns one existing-configured pool of3, shared by both adapters,
leaving the remaining login slot for separate maintenance. Public string
constructors retain owned disposal; internal borrowed adapters leave disposal to
the composing process. No grants, credential handling or lease SQL changes.

Focused checks pass160; the corrected full Windows suite passes3613 in8m29s,
failed/skipped0. Actual corrected public run e6d1b1a00c6c4844854a25ae8c93d009
retains22389 original bytes and confirms scoped producer/digest and owned absence.
The Linux CI proof now consumes both queue polls before independently checking
one live worker session as `BothDurableQueuesReuseOneBoundedWorkerDatabasePool`;
the disposable role enforces the same connection limit4. That new native case
passes in [CI41527404570](https://github.com/jvelezc/ai-screen-reader/actions/runs/41527404570),
with all nine jobs successful and73 Linux native requirements satisfied.

### Private deployment repair and runtime diagnostics

The first a5817a5 promotion,41531650218, failed its public-source readiness gate
and rolled back to fdb867c. Both services resumed on the prior release. The VPS
had Podman4.9.3 and netavark1.4.0 but no `aardvark-dns`. Its native warning entered
the launcher's combined output and cleanup JSON parsing failed. The missing
Ubuntu24.04 package `aardvark-dns=1.4.0-5` was installed after a simulated package
transaction confirmed one addition with no upgrades/removals. No container or
service restart was required by that installation.

The exact staged image then passed independent public readiness: two9-node
captures, same document, native shutdown and all five resources absent, with no
database configuration, persisted observations, input or verified goal. Retried
[private deployment41534173325](https://github.com/jvelezc/ai-screen-reader/actions/runs/41534173325)
returns `Ready` for a5817a58f48c9cdc3d9f64cdf323c626920fc054, previous fdb867c,
run2d5f308f47fb4ac5b63f923c97e34ed4. This proves private promotion, not the
original-account website-result journey or new assistive-technology acceptance.

The launcher now captures stdout and stderr separately with a structured argument
list. Both pipes drain concurrently. JSON/IDs use only stdout; diagnostics remain
available separately, and failed commands retain a bounded diagnostic message.
Build/server diagnostic logs explicitly retain both streams. The first new
regression fails against the old helper. All six Windows regression cases pass
after the fix; both operating-system CI jobs now execute and verify those six.
Actual local collection run13282666ec7d417cb8b6b672e9889161 again retains22390
original bytes, checks digest/producer/source and independently confirms native
and disposable-database absence. Hosted project changedfalse; original-account
acceptancefalse. Linux CI for this stream correction remains pending its push.

| Requirement | Evidence |
| --- | --- |
| Successful native diagnostics cannot corrupt JSON | `SuccessfulRuntimeJsonExcludesDiagnosticStream` |
| Allowed failures keep output, diagnostics and native exit separately | `AllowedFailurePreservesSeparateOutputAndExitCode` |
| Failed commands preserve bounded useful diagnostics | `FailedRuntimeRetainsBoundedDiagnostic` |
| Diagnostic JSON cannot substitute for stdout | `DiagnosticOnlyOutputCannotBecomeSuccessfulJson` |
| Explicit UTF-8 output survives capture | `UnicodeOutputIsPreserved` |
| Both pipes can exceed their native buffer without deadlock | `BothRuntimePipesDrainBeyondTheirBuffer` |

| Requirement | Evidence |
| --- | --- |
| Retain actual source, producer and unverified passive facts | `StoredOriginalBytesRetainEscapedNamesProducerSourceAndUnverifiedOutcome`; actual run81379e72fb004c279a937f8648d7f0a2 |
| Hash and byte limits cannot bless changed facts | `MatchingHashCannotAuthorizeChangedScopeOrUnconfirmedPublicFacts`, `DecoderAcceptsExactByteCeilingWithAValidOriginalDocument`, `EncoderRejectsAValidatedShapeThatExceedsTheActualByteBudget` |
| Fresh scoped authority precedes completion | `FreshAuthorityAndExactPassiveFactsPrecedeDatabaseCompletion`, `AuthorityLostAfterCollectionCannotPublishLateSnapshots` |
| Engine/cleanup/publication errors preserve uncertainty | `SourceAndCleanupFailuresRemainDistinctFromWebsiteBarriers`, `FailedOrUnacknowledgedDatabaseCompletionDoesNotBecomeObserved`, `UnacknowledgedFailureReconciliationRemainsUnconfirmed` |
| Actual late SQL operations cannot renew or publish | `PublicRenewActuallyWaitsForQueueRow`, `PublicFinishRechecksExpiryAfterQueueWait`, `PublicBlockRejectsMissingQueueMessage` |
| Known busy work keeps its queue record | `PublicBusyKnownJobDefersWithoutCapability`, `PublicBusyKnownJobIsNeverArchivedAsUnknown` |
| New packages require observed public readiness | `test_public_capability_requires_both_independent_native_readiness_receipts`, `test_public_capability_cannot_promote_unknown_or_unconfirmed_native_facts` |
| Recovery cannot retire unknown native ownership | `test_public_recovery_cannot_acknowledge_remaining_or_invalid_inventory`, `test_public_operator_modes_keep_the_closed_credential_environment` |

## Deployment and next customer gate

First pass strict build/format, full tests and every CI job. Apply the tested
additive migration through Supabase, review advisors, install the tested root-owned
operator modules and verify public readiness/recovery. Only then restore automatic
private promotion and deploy the exact successful CI release. Keep local Windows,
Linux checks, CI, private promotion and original-account/browser/AT acceptance
separate. Public MCP/plugin publication remains deferred.

Next expose scoped admission, status and original result reads through the
independent core API/private MCP. Consume those through the existing Supabase Edge
gateway, then finish the URL form and publish a real HTTPS UI origin. Preserve
unsupported/policy/failed states and missing task/evidence conditions throughout.
