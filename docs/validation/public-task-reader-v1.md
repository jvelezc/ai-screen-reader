# Public task reader integration

November15,2026 source increment. HTTP1.23 /34 private MCP tools. See [decision0090](../decisions/0090-public-task-reader-execution.md).

| Requirement | Evidence |
| --- | --- |
| Actual reader execution | `ReaderTaskUsesRealReaderTabAndRoleBasedActivation`; `ActualChromiumReaderCommandsAndFocusMatchTheRecordedSource` |
| Uncertain/denied input without retry | `ReaderTaskUncertainInputIsNeverPromotedToCompleted`; `ReaderTaskDeniedInputRemainsUnattemptedAndIsNotRetried` |
| Bounded text and truthful provenance | `ReaderAnnouncementIsBoundedWithoutSplittingUnicodeOrLosingItsFullFingerprint`; `ReaderRecordsRejectUnsupportedClaimsAndChangedEvidence` |
| Original event and snapshot binding | `ReaderRecordsMustMatchTheirOriginalEventAndInitialSnapshot`; `ReaderInitialObservationMustMatchTheRetainedSnapshotContents` |
| Missing records cannot support readiness | `ReaderProfileWithoutRecordedCommandsCannotEstablishReaderTaskReadiness` |
| Historical/strict-client compatibility | `HistoricalTasksDoNotFabricateReaderTranscripts`; `ReaderApiIsOriginalBoundAndLegacyEventsKeepTheirExactShape` |
| Paging, digest and current authority | `ReaderPagesBoundEscapedTextWithoutSkippingCommands`; `PrivateMcpReadsOnlyPinnedReaderOriginalsAndDeniesRevokedAccess` |
| Real isolated Linux collector | Four native working/broken scenarios additionally require the reader profile, initial read and reader record for every input. CI/native receipt required. |

Final pinned-SDK full Windows tests and format verification, Windows/Linux CI, exact artifact promotion, existing Edge/UI deployment and original-account browser acceptance are pending. This source increment is not yet an accepted production release. Fixture setup failures are retained privately; they are not website findings.

The supported public task still follows one service link, without form submission, sign-in or purchase. It does not establish site-wide conformance or legal standing. Browser recordings contain no external screen-reader speech. Event/video alignment is unverified; intermediate snapshot contents are not retained. Historical originals remain unchanged.
