# Restart-time recording cleanup acknowledgement repair

October31,2026. Implements [decision0069](../decisions/0069-recording-removal-recovery.md).
HTTP remains1.12 with eighteen private MCP tools. There are no schema, dependency,
Edge, UI, credential or automatic-restart changes.

| Requirement | Evidence |
| --- | --- |
| Repair the retired-intent gap while preserving actual Chromium originals and producer | `RetiredIntentRecoveryPreservesActualChromiumBytesAndOriginalProducerWithoutPublication` |
| Wire repair into executor startup without running native input or publication | `ExecutorStartupRepairsRetiredIntentWithoutCallingTheRuntimeOrPublishing` |
| Existing exact acknowledgement is idempotent | `RepeatedRecoveryAndParentAcknowledgementPreserveTheExactOriginalMarker` |
| Finish only an exact completed pending copy | `ExactCompletedPendingAcknowledgementResumesTheRenameWithoutSealingMedia` |
| Missing removal does not retire an intent or invent cleanup | `MissingNativeRemovalRemainsAwaitingAndDoesNotRetireOrAcknowledgeTheIntent` |
| Preserve corrupt copies and reject foreign native facts | `CorruptAcknowledgementIsNeverOverwrittenOrBlessed`, `ForeignOrUnconfirmedDurableRemovalCannotRepairAStage` |
| Reject changed scope, oversized/unknown inventory, changed header bytes and linked paths | `ChangedScopeDirectoryCannotSelectAHeaderFromAnotherAttempt`, `InvalidStageInventoryCannotBeAcknowledged`, `ParentCannotAcknowledgeAChangedHeaderEvenWhenItsDecodedFieldsAreEqual`, `LinkedDirectoriesCannotSupplyOrReceiveARecoveryAcknowledgement` |
| Legacy/empty roots, independent siblings and cancellation remain explicit | `LegacyRootWithoutRecordedStagesIsUnchanged`, `AnEmptyRecordedOutputRootDoesNotManufactureRecoveryWork`, `EveryAttemptIsReconciledIndependentlyWithoutRetiringAnUnconfirmedSibling`, `CancelledRecoveryCannotCopyAnAcknowledgement` |
| A local marker cannot replace the actual ownership record | `LocalMarkerCannotSubstituteForAMissingOwnershipJournalRecord` |

The focused Windows run discovers and passes135/135, failed/skipped0, including
25 new cases and the existing storage/API/MCP/ownership regressions. One real
Chromium keyboard bundle supplies the new positive byte-preservation test.
Unit ownership-journal fixtures are not real native-removal or provider-revocation
acceptance. The full Windows Release run discovers, executes and passes
2,682/2,682 in7m29s, with zero failures/skips. All25 new cases are present and
Passed in the actual TRX. The strict nonincremental SDK10.0.402 build has zero
warnings/errors; whole-solution format verification passes. PowerShell parser
validation of the native runner reports no errors. Remote CI and exact
deployment are pending for this source state.

The Linux native runner now requires61 independently counted requirements
(59 on its non-Linux path). New checks reproduce the retired-intent/copy gap
by moving only the actual completed acknowledgement into a retained fixture
file, then invoke database-free recovery twice and resume an exact pending
rename. This is a deliberately constructed durable gap, not a claimed process
crash. Existing real crash-after-input recovery additionally requires an actual
matching cleanup acknowledgement while the bundle and seal remain absent.
Those native checks are pending until their actual CI receipt passes.

This increment does not reconcile publication, recover unfinished child media,
exercise actual full-disk/provider-outage/cancellation failures or authorize
replay of an uncertain task. Damaged pending metadata remains a visible error.
The existing original-account saved-ZIP receipt at core25fdf07 / UI f7c1127 /
Edge9 predates this repair and is not a new consumer-recovery acceptance.
