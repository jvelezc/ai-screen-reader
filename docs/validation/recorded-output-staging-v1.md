# Complete recorded-output staging

October 30, 2026. This worker-local increment implements decision
[0067](../decisions/0067-recorded-output-staging.md). Public API and private MCP
contracts remain unchanged. No customer website or original stopped task is
replayed for validation.

| Requirement | Named evidence |
| --- | --- |
| Reopen actual Chromium output with its original identity and no promoted authority | `ActualChromiumOriginalOutputSurvivesReopenBeforePublicationWithoutReplayOrGoalAuthority` |
| Native acknowledgement is distinct from output sealing | `RemovalAcknowledgementComesFromMatchingNativeJournalAndDoesNotPublishAnObservation` |
| Reject foreign storage scope and same-prefix producer changes | `DifferentStorageScopeCannotSelectAnotherOriginal`, `HeaderScopeCannotBeChangedEvenWithTheCorrectStoragePrefix`, `InvalidProducerIdentityIsRejectedBeforeCreatingAStage` |
| Partial bytes are not a complete original | `AHeaderOrUncommittedBundleCannotBecomeASealedRecording`, `AFailedSealWriteRetainsOnlyUnpublishedBytesAndDoesNotOverwriteTheFailedWrite` |
| Reject changed seal, media bytes, inventories and sizes | `SealedMetadataCannotBlessDifferentOrUnboundedBytes`, `ActualOutputCorruptionFailsBeforeRecovery`, `UnknownInventoryAndOversizedPendingWritesAreNotSilentlyIgnored`, `InvalidPayloadSizeIsRejectedBeforeAuthorizationOrWriting` |
| Reject linked attempt directories without reading outside data | `ALinkedAttemptDirectoryCannotExposeBytesFromAnotherLocation` |
| No overwrite or replacement producer | `DuplicateBeginOrSealCannotReplaceTheOriginalAttemptOrBytes` |
| Authority checked at all three sealing boundaries | `AuthorityLossAtEverySealBoundaryPreventsSuccessAndNeverAuthorizesPublication` |
| Cancellation cannot seal partial bytes | `CancellationCannotSealPartiallyStagedBytes`, `CancelledBeginDoesNotAllocateARecordingStage` |
| A foreign or uncertain cleanup claim is insufficient | `AForeignOrUnconfirmedNativeRemovalCannotBlessStagedOutput` |
| Failure remains visible and cannot complete/replay | `CoordinatorKeepsOutputFailureVisibleWithoutCompletionOrAutomaticReplay` |

The byte and identity cases share one actual Chromium authored-route recording;
they do not use invented media as a substitute for browser acceptance. Filesystem
corruption, cancellation and lease callbacks are controlled test inputs, not
actual VPS disk-full, provider revocation or storage-outage acceptance.

Local Windows validation discovers and passes **2,606/2,606**, zero failed or
skipped, including **38 new cases**. The final focused run passes **38/38**.
The strict nonincremental pinned SDK10.0.402 Release build has zero warnings or
errors; whole-solution format verification and PowerShell syntax parsing pass.
The initial focused run caught a missing-directory exception expectation; the
corrected assertion passes without changing storage behavior. Before the final
linked-directory case, the full production state also passed2,605/2,605. Final
counts come from the actual TRX discovery/execution counters, not aggregation of
separate filtered runs. CI/native execution and deployment receipts follow
separately; they are not inferred from these local checks.

The actual isolated-worker acceptance script adds four requirements:
`SealedRecordedOutputBindsActualProducerAndOriginalBytes`,
`StagedRecordingAllFourFilesMatchPublishedOriginals`,
`RecordedOutputRemovalAcknowledgementMatchesIndependentOwnedAbsence`, and
`InterruptedChildCannotInventASealedCompleteRecordingOrNativeRemoval`.
It compares every staged bundle member with the independently reconstructed
published original, verifies actual producer/header/bundle seals and inspects
the exact owned container's absence. The existing real mid-input process kill
must leave no fabricated complete output seal. Native Linux expects 57 cases
(55 on the non-Linux script path); exact run receipts are separate from source.

Local focused tests, full discovery/build/format, Windows/Linux CI, actual native
Linux execution, exact deployment and original-account consumer acceptance are
separate gates. The independently shipped full-path consumer remains
[UI5972663 / Edge v8](https://github.com/jvelezc/ai-screen-reader-ui/blob/5972663c3757412ec98b659e9b7102352778d04e/docs/validation/retained-path-v1.md),
with 570 tests per OS and an actual complete checkpoint export; that earlier
receipt does not validate this staging change.

No protected staged-media read, archive-publication recovery, partial live-video
salvage or automatic retention deletion is supplied by this slice. Native cleanup
must still be verified; a sealed private bundle is never a completed queue job,
an overall verified goal or a sufficient website-evidence package. Actual S3
outage, upload-before-commit, cancellation and full-disk recovery remain open.

## Delivered staging receipt

[CI41173536711](https://github.com/jvelezc/ai-screen-reader/actions/runs/41173536711)
passes all nine jobs for commit`e8a015cd5717c308b49c8e1cc664b3f0e4da7d98`.
Windows and Ubuntu each discover, execute and pass2,606/2,606, zero failed or
skipped. The actual Linux worker proof executes57/57, zero failed or unconfirmed,
including all four new staging requirements; database and image cleanup are
confirmed. This does not establish actual full-disk or upload-outage recovery.

[Private deployment41174654423](https://github.com/jvelezc/ai-screen-reader/actions/runs/41174654423)
is Ready for that exact commit. Independent VPS verification matches all151
release files, default TLS and API1.11 schemas; anonymous session/progress/graph/
checkpoint reads are denied and root/MCP/host metadata are not exposed. Both
services run as`hsr-worker`, active, with`Restart=no`, `KillMode=process` and
`SendSIGKILL=no`. Release verification is separate from a new application-account
media read. The following [protected reads](retained-recording-output-v1.md) have
their own delivery gates.
