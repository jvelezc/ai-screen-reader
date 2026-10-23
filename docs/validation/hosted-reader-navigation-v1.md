# Hosted read-only navigation validation

Local checks, CI, private deployment and real tenant acceptance are recorded separately. Source implements the bounded read-only navigation contract in [decision0048](../decisions/0048-hosted-read-only-navigation.md).

| Requirement | Observable evidence |
| --- | --- |
| Fresh semantic cursor progression and ordered paging | `CursorProgressionUsesFreshSemanticsAndPaginatesOrderedCommandEvents` |
| Prior observation, stale fence and new-document acknowledgement | `DocumentFencesRequireObservationAndChangedDocumentMustBeAcknowledgedAgain` |
| Input/unknown commands and extra request fields denied | `UnreviewedOrInputCommandsCannotCaptureOrChangeReaderState`; `HttpCommandRejectsMalformedOrExtraInputFields` |
| Exact shared capture quota and incomplete retained history | `CommandsShareTheExactCaptureQuotaAndHistoryTruncationRemainsExplicit` |
| Fresh subject/workspace authorization, including revocation | `NavigationAndTimelineRecheckScopeBeforeRevealingOrCapturing`; HTTP authentication/other-subject lifecycle tests include both new routes |
| Capture joins on cancellation/expiry/failure; cleanup quarantine | `FailedCommandsJoinCaptureAndRetireBeforeReturning`; `FailedCommandCleanupQuarantinesBothNavigationAndHistoryUntilConfirmedClose` |
| Byte-bounded paging with no skipped event and page validation | `LargeHistoryIsPagedByBytesWithoutDroppingTheNextEvent`; `AnOversizedEventFailsAtItsCursorWithoutSkippingOrRetiringTheSession`; `TimelineRejectsInvalidPageBounds` |
| Real Chromium/Kestrel navigation, focus unchanged, no input or goal events | `RealChromiumHttpNavigationRetainsCursorAndReportsMatchingEventsWithoutInput` |
| Published consumer contract and strict integer requests | `ReaderLifecycleHttpPayloadsMatchPublishedContractsAndTruthConstraints`; `NavigationRequestSchemasMatchTheReadOnlyPolicyAndPagingBounds`; `HttpTimelineRequiresThePublishedIntegerFields` |
| Actual packaged separate worker | Extended `IsolatedReaderHttpProof` checks heading→button progression, fresh captures, paged matching events and actual native retirement; `Test-ObservationWorker.ps1` requires those facts |

Local Windows passes **1,847/1,847**, zero failures/skips, including46 new cases. The strict nonincremental Release build has zero warnings/errors and complete solution format verification passes. Real Chromium/Kestrel navigation is included in that suite.

Delivery receipts are recorded after each gate completes. At the time of this source increment, current-source CI, packaged native Linux and exact private promotion require fresh runs; the architecture Page carries subsequent live delivery receipts. Synthetic identity tests do not establish application-JWT acceptance. The prior isolated lifecycle source3471fd3 is accepted privately; the confirmed application-user reader lifecycle remains open independently of the earlier accepted one-shot shop/S3 workflow.
