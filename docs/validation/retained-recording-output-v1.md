# Protected retained-recording output reads

October 30, 2026. Implements
[decision0068](../decisions/0068-retained-recording-output-reads.md), HTTP1.12 and
two additional private stdio MCP tools. No stopped task is replayed. These reads
do not publish retained output or complete a blocked queue job.

| Requirement | Named evidence |
| --- | --- |
| Missing, header-only, sealed/cleanup-unconfirmed and available remain distinct | `StatesCannotPromotePartialOrUnconfirmedOutputToCompletedExecution` |
| Unrecorded/unstarted reads do not invent a producer or access media | `UnrecordedOrUnstartedDoesNotReadStorageOrInventAProducer` |
| Original caller and actual producer rechecked after reads and storage failures | `AuthorityAndActualProducerAreRecheckedEvenAfterStorageFailure` |
| Reject substituted source identity, seals, bytes and removal | `ProducerSealAndNativeRemovalCannotBeSubstitutedByAReadProvider` |
| Actual passive and keyboard Chromium ZIPs survive HTTP and two private MCP connections | `ActualChromiumZipSurvivesProtectedHttpAndPrivateMcpReconnectWithoutPublication` |
| Unresolved actual second-input intent does not become a complete recording | `ActualInterruptedChromiumIntentCannotBecomeAnExportThroughHttpOrPrivateMcp` |
| Reject caller authority selectors, duplicate fields and invalid offsets before storage | `HttpRejectsAuthoritySelectorsDuplicateMembersAndInvalidOffsetsBeforeStorage` |
| Exact whole-bundle identity and bounded offsets | `BytesAreBoundToSelectedWholeBundleAndExactBoundedOffsets` |
| Private MCP rejects altered scope, flags, page bytes/digests, pagination and native wrappers without retry | `McpRejectsForgedPageAuthorityFlagsBytesAndNativeWrappersWithoutRetry`, `McpManifestMustAgreeWithItsOriginalScanAndProducer` |
| Executable OpenAPI describes actual bodies, fixed flags and protected operations | `OpenApiDescribesTheActualProtectedRecordingReadContract`, `PublishedContractIncludesEveryConsumerResponseAndAuthenticationBoundary` |

The focused contract/browser run discovers and passes104/104, zero failed or
skipped. It includes51 new retained-output cases and existing API/private MCP
contracts. Actual Chromium produces passive and keyboard output; reconstructed
bytes match those originals exactly after reconnect. The interrupted case
retains an unknown second-input edge and no fabricated output. Provider identity
and native-removal records in this local HTTP/MCP test are synthetic fixtures,
not real Supabase revocation or Linux container-cleanup acceptance. Real native
staging is independently covered by the earlier native57-case receipt.

The first29-case run exposed two insufficient revocation fixtures: the generic
project stub did not simulate provider denial on a subsequent workspace read.
The scoped fixture now explicitly denies revoked access at that boundary, and
all104 focused checks pass. No production access check was relaxed. Required
diagnostic flags are also constructor-required rather than silently defaulted.

The full Windows run discovers, executes and passes **2,657/2,657**, zero failed
or skipped, in7m24s. Its actual TRX counters, rather than aggregated filtered
runs, determine the count. The strict nonincremental SDK10.0.402 Release build
has zero warnings/errors; whole-solution format verification passes.
Delivery is accepted at core25fdf076d9bb3f075cea3f3a8fed700fb947c8a0.
[CI41176596644](https://github.com/jvelezc/ai-screen-reader/actions/runs/41176596644)
passes all nine jobs, including2,657 tests on each of Windows and Ubuntu,
57 actual native requirements and289 database requirements. Failed/skipped
unit tests and failed/unconfirmed native/database requirements are zero.
[Private deployment41177920915](https://github.com/jvelezc/ai-screen-reader/actions/runs/41177920915)
is Ready. Independent SSH verification matches all151 release files, default TLS
and API1.12; protected anonymous reads return401, private root/MCP/config
paths return404, both services remain active with automatic restart disabled.

The separate consumer is accepted at UI f7c1127e21f18f196be35c94d5e2e316ed5376bf
through existing Edge v9. All three deployed Edge files match source;
[UI CI41179332066](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/41179332066)
passes621 tests per OS. The original application account saves an actual
765,454-byte controlled-fixture ZIP. Independent filesystem and private VPS
stage checks agree on its whole-file SHA256, original job, generation and worker.
The account and exact descriptor restore after real reload, without another
sign-in. This receipt verifies the matching historical removal record; it does
not add a fresh independent native-container absence observation. The separate
[consumer receipt](https://github.com/jvelezc/ai-screen-reader-ui/blob/f7c1127e21f18f196be35c94d5e2e316ed5376bf/docs/validation/retained-recording-v1.md)
details that scope. A synthetic layout capture is not an original-account proof.

This slice does not reconcile archive/database publication, recover unsealed
partial video, repair a restart-time cleanup acknowledgement or prove actual
VPS disk-full, storage outage, lease-expiry or cancellation recovery. It cannot
establish custody, a verified overall goal, an accessibility barrier or legal
sufficiency. Automatic worker restart remains disabled.
