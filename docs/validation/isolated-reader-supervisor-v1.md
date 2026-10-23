# Isolated reader supervisor validation

See [decision 0046](../decisions/0046-isolated-reader-supervision.md).

The focused Windows suite passes **32/32**, zero failures/skips. The actual CLI
closure runs real Chromium; adversary-process cases separately test malformed
output and lifecycle errors. The earlier shared-helper extraction passed all
87 existing ownership/one-shot worker regression cases before adding behavior.

| Requirement | Evidence |
| --- | --- |
| Persistent real browser and native exit before independent removal | `ActualCliSessionReusesDocumentAndConfirmsExitBeforeIndependentRemoval` |
| Separate journal, exclusive reader ownership and preserved queue intent | `SeparateReaderJournalRetainsItsOwnExclusiveLockWithoutAcquiringQueueOwnership` |
| Closed configuration and exact image/entrypoint/ownership | `InvalidConfigurationReleasesReaderLockAndCannotCreateNativeIntent`; `ReaderInspectionPinsExactImmutableImageOwnershipAndFixedEntryPoint` |
| Bad output and changed identity cannot succeed | `InvalidInitialOutputIsSanitizedAndStillJoinsRemoval`; `ChangedResponseBindingRetiresTheConnectionWithoutObservationSuccess` |
| Close requires ACK, EOF, successful client exit and empty diagnostics | `CloseAcknowledgementRequiresCleanEofExitAndDiagnostics` |
| Uncertain removal is retained and retried | `FailedRemovalRetainsOwnershipAndRetryDoesNotSendAnotherClose`; `FailedOpenWithUnconfirmedRemovalRetainsOwnershipUntilExplicitRetry` |
| In-flight cancellation joins removal | `InFlightCancellationJoinsRemovalInsteadOfReturningAReusableLiveConnection` |
| Exact capture and lifetime limits | `ParentCaptureBudgetAdmitsExactlyOneHundredAndThenOnlyClose`; `ParentExpiryAllowsObservationImmediatelyBeforeTwoMinutesAndRetiresAtTheBoundary` |

Full local Windows checks pass **1,734/1,734**, zero failures/skips; the TRX
contains all 32 new cases. Nonincremental Release build passes with zero
warnings/errors; full format verification passes. The Windows-launched
Linux/Podman harness passes **25/25**, zero failures/skips/unconfirmed cases,
including all four new persistent-reader cases. Its actual container reader
receipt records three same-document captures, 31 Foundation nodes, bounded
capacity/reuse, confirmed shutdown/removal and no database/input/goal claim.
The disposable database and image were removed. Current-source native-Linux
CI (including its two Linux-only signal cases) remains a separate gate. A protocol-adversary
process is not native isolation acceptance. The Linux harness additionally
checks three actual container lifecycles, retained document identity, capacity
denial/reuse, durable intent retirement, missing-image failure and separately
locked interrupted-create recovery without a database credential.

This increment does not enable the supervisor in the hosted HTTP registry or
change host recovery ordering. A subsequent private promotion alone would not
establish authenticated hosted reader acceptance. The previously accepted
signed-in one-shot/S3 workflow and actual assistive-technology acceptance remain
distinct gates.
