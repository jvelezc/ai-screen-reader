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
Windows/Linux CI and exact private deployment are pending for this source state
and must be recorded separately.
Original-account Edge/notebook delivery is a subsequent consumer gate. Existing
UI5972663 / Edge8 acceptance covers retained paths and checkpoint exports, not
this new recording-output API.

This slice does not reconcile archive/database publication, recover unsealed
partial video, repair a restart-time cleanup acknowledgement or prove actual
VPS disk-full, storage outage, lease-expiry or cancellation recovery. It cannot
establish custody, a verified overall goal, an accessibility barrier or legal
sufficiency. Automatic worker restart remains disabled.
