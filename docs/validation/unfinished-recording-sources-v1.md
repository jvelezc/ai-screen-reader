# Unfinished recording sources v1

October 31, 2026. [Decision0072](../decisions/0072-unfinished-recording-source-retention.md)
adds private retention of original JPEG subsets from an unfinished admitted
keyboard route. Source, local checks, Linux/native execution, CI, exact private
deployment and original-account consumer acceptance remain separate gates.

Local Windows focused checks discover, execute and pass75/75, including41 new
source-retention cases. The full pinned Release command
`dotnet test HeadlessScreenReader.slnx -c Release --no-build --no-restore` passes
2,754/2,754, failed/skipped0. Required TRX files are parsed and every discovered
case executes and passes. Strict non-incremental Release build has zero warnings/
errors; `dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore`
passes. Normal Windows Playwright assets were restored after native packaging,
before the complete suite. Both CI platforms and exact delivery remain pending.

| Requirement | Evidence |
| --- | --- |
| Separate source identity, bounded bytes, scope and acknowledgements | `MalformedOrUnadmittedMediaCannotBecomeAStorageAcknowledgement`, `MediaAcknowledgementMustBindActualWireIdentityAndDigest`, `BatchByteLimitAllowsItsExactBoundaryAndRejectsTheNextByte` |
| Original byte snapshots, independently checked storage and explicit orphans | `SourceSnapshotCopiesActualBytesBeforeCallerMutation`, `ForeignOrChangedPrivateOriginalCannotBeReadAsTheAdmittedSource`, `MissingManifestDoesNotTurnAnOrphanOriginalIntoRetainedMedia` |
| Producer binding and no overwrite, replay or completion | `RepeatedSourceIdentityCannotReplacePreviouslyRetainedBytes`, `ActualSourceOriginalsRemainPartialAndMatchTheirCompleteBundleWithoutBecomingOne` |
| Fresh authority after actual writes; no acknowledgement on storage loss | `MediaAuthorityMustRemainValidAfterActualRetentionBeforeAnyAcknowledgement`, `LostCheckpointAuthorityAfterWriteCannotAcknowledgeAnInputBaseline`, `MediaStorageFailureCannotAcknowledgeOrAdmitACommandBaseline` |
| Independent delivery/source ordering and packet identity | `IndependentSourceAndDeliverySequencePreserveOutOfOrderCompletions`, `DeclaredWireDigestCannotReplaceTheActualCanonicalSourcePacket`, `RehashedManifestStillCannotInventAnOriginalWireDigest` |
| Exact capacity and cancellation leave no invented retained frame | `JournalCapacityRetainsTheExactAllowedBoundaryAndRejectsAnotherOriginal`, `CancelledActualWriteLeavesOnlyUncommittedDiagnosticsWithoutASavedFrame` |
| Real Chromium transfer, interruption, exclusion drain and negative acknowledgement | `ActualChromiumMediaWireMatchesRetainedOriginalsAndTheFinalBundle`, `InterruptedActualKeyboardAttemptKeepsOriginalSourcesWithoutACompleteBundle`, `ActualCaptureExclusionJoinsPendingOriginalTransferEvenWhenItsCallerIsCancelled`, `WrongSourceAcknowledgementStopsActualChildBeforeAnyCommandCheckpoint` |
| Presentation timing and explicit opt-in cannot be silently replaced | `MatchingImageBytesCannotOverrideDifferentOriginalPresentationTiming`, `SourceAdmissionCannotSilentlyDowngradeToUnretainedMedia` |

The native harness kills the real coordinator after a durable observed input,
independently verifies retained image files, rejects a replacement producer, and
compares the source inventory across actual owned native removal. It must leave
the original attempt blocked without a completed archive or replacement input.
Local Windows-host Podman and Linux CI receipts are reported separately.

The actual local Windows-host Podman harness passes69/69 requirements, failed/
skipped/unconfirmed0. It retains18 committed original JPEGs totaling242,108 bytes
after killing the real coordinator following a durably observed key. Independent
file hashes and byte counts match; the private read rejects a different producer.
Native cleanup preserves every source file and the same attempt, without replay
or a complete archive. Completeness and overall goal stay NotAssessed. The owned
test database and image are removed. This is actual process/native interruption,
not a full-disk, power-loss, hosted-provider or assistive-technology proof.

The currently accepted deployment remains df5dc50. Its maintenance operator
passes171 local Linux operational tests, all nine CI jobs and exact151-file
private deployment, plus actual completed-run maintenance under the worker's
rootless account. [Separate acceptance](publication-maintenance-operator-v1.md).

Partial image metadata is currently private. No browser UI, Edge function,
HTTP/MCP partial-image export, original-account partial-media delivery or actual
NVDA comparison is claimed. Original complete ZIP exports retain their existing
contract. Unfinished sources cannot authorize replay, completion or a website
evidence Ready decision.
