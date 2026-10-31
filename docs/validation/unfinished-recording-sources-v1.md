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
before the complete suite.

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

Core65689fcec5385c5afc6d53ef71cef9883be44c72 passes all nine jobs in
[CI41194631535](https://github.com/jvelezc/ai-screen-reader/actions/runs/41194631535).
Downloaded Windows and Ubuntu TRX each independently confirm2,754/2,754 executed/
passed, failed/skipped0, including41 new cases per OS. Native Linux passes71/71,
failed/skipped/unconfirmed0 and actual Linux signal checks; it retains21 originals /
282,687 bytes across its actual coordinator crash. The native test database and
image are removed. Database admission passes361/361 with its container removed;
the operational receipt passes171/171, failed/skipped0.

[Private deployment41195885690](https://github.com/jvelezc/ai-screen-reader/actions/runs/41195885690)
is Ready. Independent verification matches151 release files, trusted default TLS,
HTTP1.12 schemas/references and protected-read denials. The release verifier's
installed module hash also matches the repository. Automatic restart stays off.

The existing original account admits a fresh Checkout preview through UI f7c1127
→ existing Edge9 → deployed core65689fc, without another sign-in. Scan
`ef97721d-0d9e-42e3-b2cb-5495d378a6a7`, job
`fd0b4edb-f264-4f84-86b1-3d4fc786b636`, generation1 and actual producer
`975a64d7-d58a-440a-825a-40137f7e9102` remain bound. The job completes with one
attempt and zero remaining leases. The original bundle is1,378,331 bytes, SHA-256
`2d5bbe2376a2184592e19b256f825d9047329a259d1e1a5501d5f7cd1b2f82fa`.
The separately published archive totals1,378,927 bytes. Its index SHA-256 is
`740954641a548ad5b7a74b55bf09685d9449c0dbbafd59b5f0174307f5db941d`.

Independent private inspection runs as hsr-worker and verifies71 retained JPEGs /
1,016,310 bytes against the complete bundle's exact original files and raw
presentation timestamps. Producer/native identity matches the sealed output;
current container absence is confirmed in the actual rootless worker namespace.
That source-image snapshot remains RetainedOriginalSourceSubset, completeness/
overall goal NotAssessed, IsCompletedRecording=false and CanReplay=false.
This hosted successful-run source match is separate from native interrupted-run
acceptance; it does not claim a hosted crash was injected.

The original account separately reads Observed/Available, verifies all five
authorized originals and loads three states/two action edges with two attempted/
completed key calls. Selecting Enter loads/seeks the3.76s original video to
1.747361s (readyState4, paused). This proves loading/seeking, not actual playback
or NVDA speech. The baseline remains separately zero inputs/objective false;
the actual authored route reaches its own destination. No partial-image UI is
claimed by this complete-recording consumer check.

The separately accepted df5dc50 maintenance operator
passes171 local Linux operational tests, all nine CI jobs and exact151-file
private deployment, plus actual completed-run maintenance under the worker's
rootless account. [Separate acceptance](publication-maintenance-operator-v1.md).

Partial image metadata is currently private. No partial-image browser UI, Edge
operation, HTTP/MCP partial-image export, original-account partial-media delivery
or actual NVDA comparison is claimed. Original complete ZIP exports retain their existing
contract. Unfinished sources cannot authorize replay, completion or a website
evidence Ready decision.
