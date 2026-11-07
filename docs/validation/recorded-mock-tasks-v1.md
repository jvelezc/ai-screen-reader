# Recorded mock task validation

This [increment](../decisions/0086-recorded-mock-task-experiments.md) collects local
v1 mock experiments. No real GentleBirth task, tenant-hosted task, real AT encounter,
website-evidence readiness or legal case is established.

| Requirement | Evidence |
| --- | --- |
| Exact four-scenario source fingerprints and fixed keyboard plans | `MockTaskSourcesMatchTheIndependentAuthoredCatalogue` |
| Other fixtures, arbitrary keys, URLs and pre-cancelled work cannot allocate an output/recording channel | `MockRecorderRejectsOtherSourcesBeforeCreatingFiles`, `MockTaskPlansCannotAcceptCallerSuppliedInput`, `InvalidMockCliArgumentsCannotAllocateAnOutputOrAdmitAWebsite`, `AlreadyCancelledMockCollectionCannotAllocateOutputOrClaimARecording` |
| Actual Chromium retains source identity, named/unnamed semantics, attempted/completed keys, goal evidence, graph, original video and closed capture | `ActualMockRecordingsRetainSourceInputsGraphOriginalVideoAndUnknownAlignment` |
| Repeated Enter, Space and pointer comparison use fresh documents and run IDs with separate mechanisms | `ActualMockSuiteRetainsFreshRepeatSpaceAndPointerExperimentsAndDetectsChangedOriginals` |
| Errors, missing media, reused identity, source mismatch, capture/cleanup uncertainty, wrong key and invented goal facts cannot become a complete comparison | `InconsistentOrPartialExperimentsCannotBecomeACompleteComparison` |
| Whole video plays to native Ended; unknown timing cannot cause an invented graph seek; the report reflows at 390px | `MarkerlessMockVideoPlaysToNativeEndedWithoutInventingGraphSeekPositions` |
| Extra/pending/changed files, invented public scope, timing or legal findings and modified source/input declarations are rejected | `MockPackageVerifierRejectsChangedInventorySourceScopeAndInventedClaims` |
| Existing four-route signatures, recorder behavior and capture lifecycle remain valid | `ActualAuthoredKeyboardRouteRetainsOriginalMediaGraphAndIndependentOutcome`, `FourOriginalRecordings_LinkRealCaptureAndInputIntervalsWithoutChangingRouteClassification`, `FinalOriginalTransferCannotExtendActualSourceAcquisitionAfterTheRouteEnds` and existing capture/viewer regressions |

The first combined run found that a static page could not supply the older
three-frame animated-fixture startup condition. The separate mock profile now
requires one real initial source frame; animated fixture behavior, timeouts,
decoder bounds and timing calibration requirements remain unchanged. The next
combined native run passes 66/66. A subsequent focused pass covers 41 new cases,
including malformed/partial evidence and native playback. Final full checks,
CI and any hosted delivery are independent gates; use the exact source commit's
results before promotion.

## Maestro: served fixture pointer behavior

Actual private web run `824cae9c73b945698ee305714105db04` passes all four flows;
JUnit reports 4 tests, 0 failures. It exports eight screenshots, one before and
one after pointer activation for each page. The evidence ZIP is returned under
the private worker's local output ID `6f4e673b9c5f4f9ea91e19898ef60308`.
The earlier bootstrap run only opened/captured a page and is not counted as a
feature pass. No Maestro screenshot is relabeled as a reader keyboard execution.

| Scenario | Pointer goal after a fresh page | Screenshots |
| --- | --- | --- |
| checkout-working | Observed | Before and pointer-result |
| checkout-keyboard-blocked | Observed | Before and pointer-result |
| control-named | Observed | Before and pointer-result |
| control-unnamed | Observed | Before and pointer-result |

These assertions describe the served synthetic pages and mouse activation only.
An independent fresh HTTPS read of all four pages matches their pinned authored
SHA-256 values (1930, 1952, 1910 and 1948 bytes respectively). Matching source
bytes do not merge the independent executions or their evidence.
The core recordings separately retain keyboard results: the working page reaches
the goal; the blocked page retains Unknown/TimedOut after completed Enter/Space
calls. Neither outcome becomes a legal conclusion or site-wide coverage claim.
