# Retained original-image reads v1

October 31, 2026. [Decision 0073](../decisions/0073-protected-original-image-reads.md)
adds independently usable API 1.13 and20 private MCP tools. Source/local tests,
Linux CI, exact deployment and original-account Edge/UI delivery remain distinct.
Local Windows validation passes 156 focused cases and all 2,857 discovered tests,
with zero failed/skipped. The strict nonincremental Release build has zero
warnings/errors, and full-solution format verification passes. No CI/private
delivery or application-account partial-image acceptance is claimed yet.
The deployed accepted baseline is65689fc / 1.12, with the source-retention and
complete-recording consumer evidence recorded [separately](unfinished-recording-sources-v1.md).

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
