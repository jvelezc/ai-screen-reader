# Transport-attribution V2 validation

Source increment: November 9, 2026; HTTP 1.22; 33 private MCP tools. No UI/Edge wire change. [Decision](../decisions/0089-versioned-transport-attribution.md).

## Reproduction and regression

Against the unchanged eca67aa evaluator, the initial 23-case regression run discovered 23, passed 3 and failed 20. Failures asserted the incorrect `WebsiteEvidenceReady` result for post-probe proxy errors, proxy truncation and aborted requests; expected result was `InsufficientEvidence`. The same 23 cases then passed with the versioned classifier. A separate discovery/serialization case was added before full release checks. Failed TRX remains preserved in private test scratch; it is not original website evidence.

- `PostProbeProxyFailureCannotProveAKeyboardBarrier` retains the original proxy issue pointer and verified-artifact condition while rejecting barrier readiness.
- `DroppedProxyEventsCannotProveAKeyboardBarrier` treats omitted proxy records as uncertainty.
- `ExpectedPreflightDenialDoesNotInvalidateACleanRepeatedBarrier` proves the exact isolation boundary, including the adjacent sequence outside it.
- `AbortedRequestCannotProveAFailedGoalIsAKeyboardBarrier` exercises all 16 browser resource categories; resource type does not prove causal independence.
- `ObservedGoalsSurviveTransportErrorsUnderCurrentPolicy` preserves directly observed success and the explanatory issue table.
- `AssessmentPolicyRequiresCompleteMatchingNetworkTraces` rejects unknown, empty and incomplete policy claims.
- `MissingPolicyRetainsLegacyAssessmentAndWireFormat` pins old omitted-property originals and the explicit new-policy distinction.
- `FrozenLegacyPublicationRemainsReadableWithoutReassessingItUnderNewPolicy` reads a frozen old publication through the real hosted HTTP handler, preserving its original bytes and issuing zero new task admissions.
- `CurrentPolicySurvivesOriginalRoundTripWithoutPromisingABarrierWhenDiscoveryFindsNoGoal` proves serialization and honest no-goal assessment.

## Release gates

Run the pinned-SDK full Release solution suite and required format verification. GitHub Actions independently runs Windows/Linux tests and the four actual isolated Linux Chromium task scenarios. Native receipts require the worker-produced `TransportAttributionV2` marker and decoded recordings. Local Windows, Linux CI, private exact-artifact promotion, component health, original-account browser use and actual assistive-technology comparison remain separate evidence. The living architecture Page records the accepted release and hosted receipts; code review or unit tests alone do not establish those gates.

## Preserved scope

No original report, failed task, media or publication is rewritten. No consequential input is repeated by recovery or diagnostics. No task logic moves into the UI or Supabase Edge. This change improves failure attribution within the supported bounded public navigation task; arbitrary forms, credentials, purchases and detailed WebSocket handshake evidence remain separate completion gates.
