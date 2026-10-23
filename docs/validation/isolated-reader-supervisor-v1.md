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

## Target-host inspection compatibility

Source `e529e30` passed all nine jobs in [CI 39896507611](https://github.com/jvelezc/ai-screen-reader/actions/runs/39896507611):
Windows and Ubuntu TRX each confirm1,734/1,734, all32 new cases. The native Linux
release proof passed27/27, including Linux signal cases. Exact private deployment
39897505967 and independent worker/control host readback passed.

The additional persistent-reader VPS canary failed before acceptance. Inspection
of a separately journaled owned container showed Podman4.9.3 serializing
`Config.Entrypoint` as the string `"dotnet"`; Docker/newer Podman returned the
array `["dotnet"]`. All image, argument, user and resource values matched. The
canary's failed intent and the diagnostic container were recovered with the
normal ownership-checked reader recovery; no unknown container was deleted.

The repair accepts only these two representations of the same executable.
Strings containing arguments, null/other values and extra array elements stay
denied. `ReaderInspectionPinsExactImmutableImageOwnershipAndFixedEntryPoint`
adds exact-string acceptance and argument/null rejection cases. The fixed
argument vector, immutable image, owner labels and isolation guards remain
required. Replacement full checks, all-nine CI and exact-artifact VPS canary
acceptance remain required; the earlier general host readback does not pass the
persistent-reader canary.

Repair validation: focused35/35 and full local Windows1,737/1,737, zero
failures/skips; nonzero TRX counters checked. Nonincremental Release build
and complete format verification pass. The three new provider-shape cases
exercise the actual target-host representation while retaining negative checks.
Replacement all-nine CI and the VPS reader canary are still required.
