# Retained original-image reads v1

October 31, 2026. [Decision 0073](../decisions/0073-protected-original-image-reads.md)
adds independently usable API 1.13 and20 private MCP tools. Source/local tests,
Linux CI, exact deployment and original-account Edge/UI delivery remain distinct.
Local Windows validation passes 156 focused cases and all 2,857 discovered tests,
with zero failed/skipped. The strict nonincremental Release build has zero
warnings/errors, and full-solution format verification passes. No CI/private
delivery or application-account partial-image acceptance is claimed yet.
The deployed accepted baseline is `65689fc` / 1.12, with the source-retention and
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
A fresh full Release run and new all-job CI run remain required.

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
