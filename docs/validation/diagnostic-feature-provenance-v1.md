# Diagnostic feature provenance v1 validation

October 20, 2026, local Windows: **757 discovered/executed/passed, 0 failed/skipped**. All 731 preceding test identities are retained; 26 cases were added, including seven real Chromium cases. Discovery and TRX identity sets match. Nonincremental Release build: zero warnings/errors. Formatting verification passed. Receipt: artifacts/recording-validation/feature-provenance-windows.trx. The four-route CLI passed at artifacts/features/0afc0511de28493d8fa39e55eb40b173/index.md. [CI run 39496187180](https://github.com/jvelezc/ai-screen-reader/actions/runs/39496187180) passed all 757 tests on Windows and Ubuntu, both four-route feature demos, prior recording demos and packaging. Independent Linux isolation/public-reader acceptance also passed.

| Requirement | Evidence |
| --- | --- |
| Hints preserve unnamed semantics and link actual focus transitions without assigning feature success | DiagnosticHint_KeepsUnnamedSemanticsAndLinksActualFocusTransitionsWithoutAssigningFeatureSuccess |
| Unconfirmed, missing, foreign, resumed or changed sources retain diagnostic evidence with no path links | MissingForeignOrChangedSource_PreservesDiagnosticEvidenceWithoutPathLinks |
| Interrupted related attempts retain an unresolved result instead of an invented edge | InterruptedRelatedAttempt_RetainsNullResultAndDoesNotInventAnEdgeOrVerifiedFeature |
| Duplicate IDs/nodes, excess candidates, invalid provenance, editable/protected targets and oversize hints fail validation | InvalidDiagnosticContracts_AreRejectedBeforeEvidenceProjection |
| Untrusted hints are encoded; JSON binds the inventory to the run; prior artifacts cannot be overwritten | InjectedDiagnosticHint_IsEncodedAndWriterRetainsBoundInventoryWithoutOverwriting |
| Invalid inventory is rejected before any trace file is published | InvalidInventory_PreventsPublishingAnUnrelatedTraceBundle |
| Four real routes preserve reader snapshots, names, actual outcomes/key counts and related paths | FourRealRoutes_KeepDiagnosticHintsSeparateFromNamesAndActualPaths |
| Candidate bound, hidden/editable exclusions and oversized hints remain explicit | BoundsHiddenEditableAndOversizedHints_RemainExplicitWithoutInventedLabels |
| Excluded diagnostics, old epochs, foreign documents and cancellation cannot acquire a valid path | ResumedEpochForeignDocumentAndExcludedDiagnostics_CannotAcquireAReaderPath |
| A node removed during a diagnostic read remains unconfirmed without source/path links | NodeRemovedDuringDiagnosticRead_RemainsStaleAndCannotAcquirePathLinks |

The first focused run exposed a test assumption: replacing content with SetContentAsync need not change the browser's document identity. The test now separately proves resumed-epoch rejection and a genuinely foreign fresh-browser document. Document identity checks were not weakened. DiscoveryUnconfirmed deliberately covers unavailable or unstable evidence; it does not assert that a change was observed.

This is a bounded declared-hint inventory, not a general feature inference engine or autonomous path search. No general recording, sensitive entry, human control, hosted persistence privacy or new public input is enabled. Local Windows, remote CI, deployment, client acceptance and real assistive-technology results remain separate.
