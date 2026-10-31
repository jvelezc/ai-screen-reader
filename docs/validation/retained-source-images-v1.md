# Retained original-image reads v1

October 31, 2026. [Decision 0073](../decisions/0073-protected-original-image-reads.md)
adds independently usable API 1.13 and twenty private MCP tools. Source/local tests,
Linux CI, exact deployment and original-account Edge/UI delivery remain distinct.
Local Windows validation passes 156 focused cases and all 2,857 discovered tests,
with zero failed/skipped. The strict nonincremental Release build has zero
warnings/errors, and full-solution format verification passes. The repaired full
Windows run passes 2,858/2,858, with zero failed/skipped. Accepted delivery is
`4381d44` / 1.13. The earlier source-retention and
complete-recording consumer evidence recorded [separately](unfinished-recording-sources-v1.md).

## CI playback issue and repair

The first [CI run](https://github.com/jvelezc/ai-screen-reader/actions/runs/41198779391)
at `134ec0f` passes eight of nine jobs. Ubuntu passes all 2,857 tests; Windows passes
2,856 with one existing viewer-test failure, `NativePlaybackEvents_KeepStatusConsistentThroughPlayPauseEndAndEvidenceSelection`.
All 103 new source-image cases pass on both platforms. The downloaded native
receipt independently passes 71 requirements with database/image removal; the
database receipt passes 361 and confirms container removal; operational tests pass 171.
This failed run does not authorize deployment or completion of the core gate.

The viewer helper gave native media observation and its outer Playwright call
identical 5-second deadlines. The outer timeout masked the actual native state;
the failed receipt cannot establish a specific decoder failure. The repair gives
initial media decoding the existing 15-second document-startup allowance, keeps
play/seek/end observation at 5 seconds, and makes the outer deadline exceed each
inner budget. Error/hidden states fail promptly with ready/network/error/seek/
pause/end/timing and visible status diagnostics. No native events are fabricated
and no playback assertion or production deadline is relaxed.

All eight focused viewer cases pass locally, including
`InvalidNativeMediaFailsReadinessWithDiagnosticState`, which uses an actual
invalid WebM and verifies a native error and failed-readiness diagnostics.
A fresh full Windows run passes 2,858/2,858. The repaired
[CI run 41200510505](https://github.com/jvelezc/ai-screen-reader/actions/runs/41200510505)
passes all nine jobs. Downloaded Windows and Ubuntu TRX files independently confirm
2,858/2,858 each, including all 103 new source-image cases and eight viewer cases,
with zero failed/skipped. Native Linux passes 71, database 361 and operational 171;
the required native/image/database/container cleanup flags are confirmed.

## Exact private delivery and original-account consumer

[Deployment 41201965100](https://github.com/jvelezc/ai-screen-reader/actions/runs/41201965100)
promotes exact `4381d44601d2ce2a953c904366b461d436aa49c2`. Independent host readback
verifies all 151 release files, default TLS, API 1.13.0, source schema references
and anonymous 401 guards on both new routes. Public `/mcp` remains 404.

Consumer `166746ebef274af2ec8a6fec3fa7916a925c32c1` passes
[705 tests per OS](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/41203286902).
Existing-project Edge `hsr-core` v10 is Active; all three deployed files exactly
match that commit, retaining its original-user authentication. The original
application account reads 71 source images/1,016,310 bytes for scan
`ef97721d-0d9e-42e3-b2cb-5495d378a6a7`, job
`fd0b4edb-f264-4f84-86b1-3d4fc786b636`, generation 1, actual producer
`975a64d7-d58a-440a-825a-40137f7e9102`.

Keyboard-selected source 71 downloads as 18,078 bytes. An independent direct
byte comparison matches the private original; SHA-256 is
`a5a9bea80fdc496c23e8cbc3c18f8e29dbaf9b687a2f48ed7646433116688941`.
Its raw presentation timestamp is `1793445377994.28` ms and source prefix is
`3b1e907f3c785f5bd7393167d75f0deccb549788478755b19ca4b2a1577d60fa`.
All 71 originals/timestamps separately match the complete original bundle. The
actual rootless `hsr-worker` account confirms current native absence. Scope stays
RetainedOriginalSourceSubset, completeness/goal NotAssessed, no replay/completion.

Browser keyboard/focus and 320px reflow pass; source region width/scrollWidth are
both 273px, picker 249px. A full reload restores the original account without
credential entry. Five published originals, the three-state/two-edge graph,
complete-copy availability and the original 3.76s paused video at 1.747361s
(readyState 4) also pass fresh reads. Playback, human usability and NVDA are not
claimed. The download-event observer lost its browser attachment after the file
was saved; actual file comparison and a fresh same-profile tab establish the
result, rather than treating that tool timeout as product failure.

This hosted source run completed normally. Actual interrupted-source retention
was separately proven by local/CI native cases at that point. A later
[actual hosted interruption](hosted-interrupted-source-readback-v1.md) now passes:
117 retained originals after owned-child loss, blocked same-producer attempt,
fresh user readback, matching image download and actual reconnect. The second
intent remains Unknown; no complete recording or inferred goal is manufactured.
**Next:** validate/install the [operator directory fix](operator-command-directory-v1.md), then prove first publication through the ordered maintenance wrapper. Remaining failure/provider/website gates stay open.

| Requirement | Evidence |
| --- | --- |
| Missing, empty, uncommitted and committed subsets stay explicit | `MissingEmptyAndCommittedImagesStayDistinctWithoutInventingARecording`, `UnrecordedOrUnstartedDoesNotReadStorageOrInventAProducer` |
| Exact original pages, hashes and EOF reauthorization | `ExactImagePagesReconstructTheOriginalThenRequireAnEmptyAuthorizedEofRead`, `ByteIdentityAndOffsetGuardsCannotReadAnotherOriginal` |
| Actual caller/producer scope after every read and failure | `EveryReadRechecksAuthorityAndAttemptIncludingStorageFailures`, `MembershipLossAfterActualFileReadCannotReleaseImageBytesOrEof`, `CancellationAfterActualFileReadCannotReleaseAnImagePage` |
| Corrupt bytes and substituted provider metadata fail closed | `AProviderCannotSubstitutePrivateHeaderScopeOrInventByteTotals`, `ChangedOriginalBytesNeverReachAnAuthorizedImagePage`, `AReadProviderCannotSubstituteBytesAfterTheOriginalFileWasVerified` |
| Source changes do not overwrite or replay originals | `SourceAppendInvalidatesPriorPrefixWithoutChangingOriginalImagesOrAuthorizingReplay` |
| Platform-independent contracts preserve uncertainty and bounds | `DomainSubsetCannotBecomeAFinalRecordingOrAnUnboundManifest`, `ImageMetadataHasAnExactBoundedOriginalIdentity`, `PublishedSourceImageSchemasPreserveBoundsRequiredIdentityAndSubsetMeaning` |
| Real Chromium images through actual HTTP/private stdio MCP and reconnect | `ActualInterruptedChromiumImagesSurviveProtectedHttpAndPrivateMcpReconnect` |
| Both HTTP routes retain authentication/body boundaries | `SourceHttpAuthenticationAndClosedBodyBoundaryProtectBothRoutes` |
| MCP rejects forged upstream authority, identity, pages and cursors | `ForgedImagePageCannotChangeRequestedScopeBytesCursorOrSubsetAuthority`, `ManifestAvailabilityMustAgreeWithActualAttemptAndSubsetMetadata`, `InvalidSourceArgumentsCannotReachUpstreamOrStartARetry` |
| Maximum payload and exact EOF remain useful without invented completion | `MaximumManifestAndImagePagesStayInsideTheRealToolPayloadBudget`, `ExactEofRemainsAnEmptyFreshlyAuthorizedPageWithTheSameImageIdentity` |

Positive composition uses actual interrupted Chromium originals and real local
files, HTTP and private MCP. Provider authorization and failure cases use scoped
test doubles; signature-only deterministic byte controls are not media-decoding
or actual-provider claims. Current bytes, before/after authority and original
producer checks are separately asserted. Neither saved metadata nor a matching
hash alone establishes a barrier or evidence sufficiency.
