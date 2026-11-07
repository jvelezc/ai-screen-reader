# Captured-page screening validation

[Decision 0085](../decisions/0085-service-journey-screening.md) implements a narrow
diagnostic screen and suggestions for untested service journeys. It does not
execute a real GentleBirth task, collect task video, establish a barrier, decide
a legal case or satisfy the evidence checklist. The public read-only profile
remains unchanged.

| Requirement | Evidence |
| --- | --- |
| Preserve exposed names and inspect only interactive roles for potential naming issues | `OnlyExposedInteractiveRolesProducePotentialNameIssues` |
| Suggest observed service journeys without execution, authority or invented purpose | `ObservedLabelsSuggestServiceJourneysWithoutExecutionOrAuthority`, `UnknownPurposeAndNoninteractiveTextStayUnclassified` |
| Inspect later nodes and preserve original limitations without an accessibility pass | `InspectionCoversTheWholeStoredTreeWithoutRepairingExposedNames`, `EmptyCaptureAndSourceLimitationsCannotBecomeAnAccessibilityPass` |
| Reject ambiguous node identities | `DuplicateNodeIdentityIsRejectedInsteadOfMergingUnrelatedEvidence` |
| Use actual Chromium accessible names in named/unnamed fixtures | `ActualChromiumScreeningPreservesNamedAndUnnamedControlsWithoutTaskClaims` |
| Bind screening to the complete verified original without new work or altered original bytes | `ScreeningUsesTheCompleteDigestPinnedOriginalWithoutTaskOrLegalClaims` |
| Preserve counts, truncation and raw names under item and byte limits | `ScreeningReportsOmittedIssuesAndJourneysWithoutClaimingCompleteCoverage`, `ScreeningByteBudgetTrimsItemsWhileRetainingCountsAndOriginalNames` |
| Current membership denial, changed producer or stale digest cannot release earlier data | `CurrentWorkspaceDenialWinsOverComputedScreening`, `ChangedProducerCannotReleaseScreeningForAnEarlierOriginal`, `AStaleDigestCannotBeUsedToScreenANewerPage` |
| Malformed captured identity returns a conflict without partial data | `MalformedCapturedIdentityReturnsSafeConflictWithoutPartialScreening` |
| HTTP authentication and path-only selectors apply to the new route | `HttpScreeningUsesExistingAuthorizationAndRejectsQueryScope` |
| Private MCP preserves potential issues and untested journeys; invalid identities cause no network request | `PrivateScreeningPreservesPotentialIssuesUntestedJourneysAndPinnedOriginal`, `PrivateScreeningRejectsMalformedIdentityBeforeTransport` |
| Private MCP fits its own byte envelope without shortening names or silently removing source limitations | `PrivateScreeningBoundsItsEnvelopeWithoutShorteningOriginalLabels`, `PrivateScreeningCannotSilentlyDropOversizedSourceLimitations` |
| Reject invented execution, authority, task success, evidence readiness, legal conclusions, numeric purpose, noninteractive roles, overlapping issue/task nodes, changed digest/capture/counts and extra capability fields | `PrivateScreeningRejectsInventedExecutionEvidenceAndScope` |
| Actual stdio MCP consumes the real Kestrel endpoint and denies data after reconnect/access loss | `StdioPublicToolsConsumeScopedHttpBytesAndDenyAfterReconnectWithoutNewWork` |
| OpenAPI describes the actual string purpose and fixed uncertainty states | `ScreeningContractPreservesPotentialClassificationUntestedTasksAndStringPurpose`, `PublicContractDocumentsEveryActualRouteWithPathOnlySelectorsAndAcceptedAdmission` |

The HTTP/provider harness uses synthetic caller/provider replies. The two
Chromium cases use the real browser and fixed fixture bytes. These are separate
from original-account hosted acceptance and real assistive-technology validation.

The initial focused run found a string-enum mismatch between the HTTP serializer
and strict MCP client. Dedicated screening JSON options now match both endpoints;
existing HTTP schemas/serialization are unchanged. The round-trip regression
includes a named course link and an unnamed button, so an empty result cannot
hide this mismatch. No test deadline, assertion or worker policy was relaxed.

Final focused local Windows validation passes **61/61 discovered/executed**,
failed/skipped 0, including actual Chromium and stdio MCP → HTTP. Solution format
verification passes. Builds use the pinned SDK, warnings as errors and the
`Directory.Build.targets` dependency gates. The first full run passes 3,803 of
3,804; the only failure is the older exact tool list expecting 26 instead of 27.
That expectation is corrected and its actual reader/browser path passes in the
final focused run. Final full Windows and remote CI remain independent gates;
their run results must be checked for the exact source commit before promotion.

The existing deployed UI does not consume this source increment yet. Private
promotion, Edge/UI integration, real-account acceptance and real AT validation
are not established by these local checks.
