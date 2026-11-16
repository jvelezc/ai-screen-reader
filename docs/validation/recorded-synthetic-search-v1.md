# Recorded synthetic search experiments v1

November15,2026 source increment. These are local authored experiments, not a website review or a hosted search task. The two exact sources are `search-working` and `search-keyboard-blocked`; their fixed query is `sample course` and their goal is the focused `Sample course result` heading.

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --record-search-tasks search-keyboard-blocked artifacts/my-new-search-run
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --verify-search-tasks artifacts/my-new-search-run <printed-manifest-sha256>
```

Use a new directory. Keep the printed digest separately, preserve the ZIP and open `index.html` for the comparison. Each keyboard path has its own original video, interactive observed-state/action graph, reader timeline and structured records. The mouse comparison retains native focus, thirteen keyboard characters and native activation separately, with its own states/edges and original video. It is not represented as a zero-keyboard experiment.

Three fresh keyboard attempts use Enter, Enter and Space. Each has sixteen native keys: Tab, thirteen query characters, Tab and activation. Completed entry independently verifies the field value; it does not establish submission or search success. The recorder saves the first incomplete attempt and stops further input rather than retrying. Positive individual outcomes remain in originals even when the overall comparison is incomplete.

| Requirement | Evidence |
| --- | --- |
| Exact two source identities; unrelated fixtures and keys denied before output | `SearchRecorderRejectsOtherSourcesBeforeCreatingOutput`; `SearchRecorderRejectsOtherKeysBeforeCreatingOutput`; `SearchCommandRejectsUnknownSourcesAndMalformedArguments` |
| Independent working/broken keyboard outcomes with actual graph/input/video | `ActualRecordedSearchRetainsNativeCharactersGraphAndOriginalVideo` |
| Mouse focus, query and activation are distinct actual operations | `ActualMouseSearchFocusAndActivationHaveIndependentReceipts`; `MouseSearchCannotActivateWithoutTheObservedQuery`; `MouseSearchCannotSilentlyReplaceAnExistingQuery` |
| Three fresh keyboard contexts and a fourth mouse comparison, with source/producer binding and complete originals | `ActualSearchPackageRetainsFreshComparisonsAndRecomputesClaims` |
| Incomplete input cannot start another run or mouse action | `IncompleteSearchAttemptIsSavedAndStopsAllFurtherInput` |
| Derived structured claims, inventory, source and graph agree; changed originals rejected | `ActualSearchPackageRetainsFreshComparisonsAndRecomputesClaims`; `SearchVerificationRequiresATrustedLowercaseDigestBeforeReadingFiles` |
| Historical HTML is verified against its trusted original inventory, independent of current styling | `TrustedHistoricalViewerBytesDoNotDependOnCurrentRendering` |
| Literal space keys remain readable without changing original key strings | `LiteralSpaceHasAReadableLabelWithoutChangingRecordedKeys`; `ActualSearchPackageRetainsFreshComparisonsAndRecomputesClaims` |
| Every complete clip plays from zero to native ended without a media error; graph selection cannot invent a seek | `ActualSearchPackageRetainsFreshComparisonsAndRecomputesClaims` |
| Pointer graph preserves repeated letters, missing/orphan completions and changed observations | `RepeatedCharactersPairWithTheirOwnLaterCompletion`; `MissingOrInconsistentTextEventsCannotBecomeCompleteInput`; `ChangedActivationDocumentRemainsAnExplicitSeparateObservation` |
| Missing or inconsistent mouse facts remain incomplete | `MissingPointerFactsDoNotInventNodesOrActions`; `IncompleteMouseFocusPreservesActualDispatchAndMissingResult` |
| Truncated pointer-reader timelines remain explicit and cannot qualify | `TruncatedTimelineCannotQualifyEvenWithEveryRetainedInputPair`; `ActualSearchPackageRetainsFreshComparisonsAndRecomputesClaims` |
| Existing four-source v1 admission does not acquire search authority | `OriginalAdmittedMockCatalogueDoesNotAcquireSearchAuthority` |

Final focused native/package/projection run:42 discovered/executed/passed, zero failed/skipped. Two complete package scenarios each play all four original clips to their native end, observing decoded frames, duration, current time and absence of media errors. The first package test run passed6/8; two assertions incorrectly searched a detail paragraph for a command name displayed in its separate label. The test now asserts the actual command label, thirteen completed calls, explicit unavailable timing and unchanged video position. No production evidence or behavior was changed to satisfy that assertion.

The pre-review-fix Windows solution run passes4,171/4,171. Recorded-search commit8dc2d94 passes the final canonical Windows run4,172/4,172 and all nine CI43784162387 jobs, with4,172 discovered/executed/passed per OS, zero failed/skipped. Both uploaded TRX counters are independently checked. Full-solution format verification passes. The earlier search-input foundation at63662af separately passes all nine CI jobs/4,137 per OS.

Actual IAB inspection then identified blank literal-space labels and inaccurate generic seeking copy. The corrective source passes66 focused search/trace/viewer checks and format verification. Its verifier reads both original8dc2d94 showcase exports unchanged: working94 files/4,119,256 bytes and blocked91 files/4,219,412 bytes, four original videos each. Independent directory/ZIP checks match every byte and original manifest/archive fingerprint. Final corrective Windows/CI checks are recorded separately after completion. No hosted search deployment is claimed.

The markerless source pages do not provide calibrated event/video correspondence. Whole clips are real and playable, but selecting a recorded event does not infer a video position. Presentation labels a literal space key “Space character”; original event/key strings remain unchanged. The manifest states `LocalEmbeddedSyntheticSearchExperiments`, `UnverifiedWithoutCalibrationMarkers`, website assessment `NotAssessed`, external assistive-technology comparison `NotPerformed`, and legal case false. A supplied file hash does not establish actor identity, legal standing, liability or admissibility. HTML remains bound to the trusted original inventory rather than the current renderer; the verifier recomputes structured claims and graphs, not historical styling.

The existing hosted service-link task, four-source mock profile, public input restrictions and original schemas remain independent. Before hosted/public search, prove per-native-character authority and capture exclusion, no-replay worker fencing, independent parent validation, tenant-scoped originals, HTTP/private MCP and same-task Edge/customer delivery. See [decision0092](../decisions/0092-recorded-synthetic-search-experiments.md).
