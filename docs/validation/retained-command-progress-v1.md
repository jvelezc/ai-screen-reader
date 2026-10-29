# Retained command progress and process-crash recovery

October 28, 2026. Source implementation for
[decision 0064](../decisions/0064-retained-command-progress.md).
The full interrupted-recording recovery gate remains open.

| Requirement | Evidence |
|---|---|
| Await retained intent before any actual key | `RealChromiumWaitsForRetainedIntentBeforeSendingAnyKey`, `RealChromiumIntentWriteFailureStopsBeforeTheFirstInput` |
| Refuse mismatched acknowledgements and invalid starts | `RealChromiumWrongIntentAcknowledgementCannotProduceAnObservedResultOrBundle`, `InvalidStartProducesNeitherProgressNorRecording` |
| Bind exact execution, command, source and observed attempt | `WrongExecutionSequencePhaseCommandOrSourceIsRejectedBeforeInput`, `InconsistentObservedAttemptIsRejectedBeforeRetentionOrAcknowledgement` |
| Denied authority or failed storage cannot acknowledge input | `AuthorityOrStorageFailureCannotAcknowledgeTheNextCommand` |
| Validate committed records and retain only the committed prefix | `TruncatedChangedGappedForeignAndUnexpectedInventoryCannotYieldProgress`, `InterruptedNextWriteRetainsOnlyTheCommittedPrefix`, `ExistingAttemptCannotBeOverwrittenAndAnotherScopeCannotInspectIt` |
| Keep diagnostics separate from completion/replay authority | `RetainedPrefixSurvivesReopenWithoutBecomingCompletionOrReplayAuthority` |
| Match an actual Chromium bundle and reject changed attempts | `ActualChromiumFramedWorkerAgreesWithRetainedPrefixAndCompleteOriginalBundle` |
| Compare actual admitted producer and operator-only inspection | `CommandProgressPrefixMatchesActualAdmittedProducerAndCompleteRoute`, `LocalProgressInspectionNeedsNoDatabaseAuthorityAndCannotAcceptAnotherScope` |
| Kill after actual input, preserve the original attempt and recover capacity | `NativeCrashAfterRecordedInputRetainsUncertainProgressAndOwnedIntent`, `NativeRestartPreservesCommandPrefixWithoutReplayingCrashedAttempt`, `NewSeparateObservationCompletesAfterCrashedKeyboardRecovery` |

Local Windows discovers, executes and passes **2,434/2,434**, zero failed or
skipped, including 34 new cases. The sequential strict Release build has zero
warnings/errors; full format verification passes. The focused TRX also reports
34/34. Commands use the pinned 10.0.402 SDK: `dotnet build
HeadlessScreenReader.slnx -c Release --no-restore -warnaserror`, `dotnet test
HeadlessScreenReader.slnx -c Release --no-build --no-restore`, and `dotnet format
HeadlessScreenReader.slnx --verify-no-changes --no-restore`.

Native validation on the local Windows host with Linux containers passes
**51/51**, zero failed, unconfirmed or skipped. Receipt
`artifacts/worker-linux/288f344f62be40b1a65e84ee6fdf5beb/run.json` retains the
actual cases. A real worker process is killed after a saved `InputCompleted`
event. The original attempt stays leased until the disposable test database
expires it. Restart removes the independently verified owned container and
blocks that same attempt without a new generation, archive or key replay. All
progress filenames and hashes stay unchanged. A separate input-free job then
completes. Operator inspection also matches the completed route's actual
database producer and complete bundle. Test database and image are removed;
the hosted project is unchanged.

Windows/Linux CI, Linux-host native validation, exact private deployment and
original-account checks are separate delivery gates. Their post-commit receipts
are maintained in the [architecture Page](https://chatgpt.com/space/page_9a835b55f9808191a2accbc9900a3e25).

This increment does not recover interrupted original video, publish local
progress through tenant APIs, establish power-loss durability, exercise real
assistive technology, enable public MCP/plugins or collect arbitrary websites.
