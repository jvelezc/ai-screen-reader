# Reader v9 trees and tabs

Date: October 19, 2026. Profile: hsr-desktop-reader-v9. Scope: trusted single-select tree views and manually activated tab interfaces, following v8 range widgets.

The increment adds source-tagged tree hierarchy, scoped exposed-peer context, six browse commands, hierarchy-bearing events and actual focused keyboard activation. Focus, active descendant, expansion and selection remain distinct. See [decision 0010](../decisions/0010-observed-tree-and-tab-context.md).

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Six semantic commands move in both directions without wrapping/input; empty and focus-mode cases stay explicit | CompositeSessionTests.CompositeNavigation_BothDirections_NoWrapOrInput; CompositeNavigation_EmptyAndFocusMode_RemainExplicit |
| Tree context retains observed ancestry/level and scopes peers separately from declared logical positions | CompositeSessionTests.TreeContext_ScopesExposedPeersToKnownParentAndLevel_SeparatesLogicalSet |
| Flat, missing, invalid, cyclic, cross-root and conflicting-document context cannot invent peer sets | CompositeSessionTests.TreeContext_UncertainOrCrossDocumentOwnership_DoesNotInventPeers |
| Separate tablists and panel context remain scoped | CompositeSessionTests.TabContext_IndependentTablistsAndPanels_AreScoped |
| Actual Tab routes composite controls into focus mode | CompositeSessionTests.CompositeTabFocus_UsesFocusMode |
| Direct/active-descendant activation uses observed focus and owner; unresolved, wrong, foreign and cross-document ownership sends no input | CompositeSessionTests.CompositeActivation_ObservedFocusAndOwnership_DispatchesEnter; CompositeActivation_UnresolvedWrongOrCrossDocumentOwner_IsUnsupported |
| Unfocused or disabled treeitems cannot produce an invented input success | CompositeSessionTests.CompositeActivation_UnfocusedOrDisabledItem_DoesNotDispatch |
| Invalid/positive levels and historical hierarchy changes remain explicit | CompositeSessionTests.TreeLevel_AnnouncesValidityAndRetainsHistoricalEvent |
| Real AX exposes typed levels and selected/expanded states, excludes hidden content and separates heading/tree levels | ChromiumCompositeTests.CompositeCapture_Chromium_ExposesLevelsAndSelection_WithoutHiddenChildrenOrPanels |
| Real tree expansion, active descendant and selection remain distinct; Enter goes to the actual owner | ChromiumCompositeTests.TreeKeyboard_Chromium_ExpansionFocusAndSelectionRemainSeparate_EnterUsesObservedOwner |
| External collapse removes hidden items without inventing reader movement or input | ChromiumCompositeTests.ExternalCollapse_Chromium_RemovesHiddenItemsWithoutInventingCursorMovement |
| Roving tab focus does not imply selection; explicit activation changes the visible panel; browse movement does not repair focus | ChromiumCompositeTests.Tabs_Chromium_RovingFocusDoesNotSelectUntilActivation_VisiblePanelFollowsSelection |
| Real roving treeitem focus routes Tab/Shift+Tab, arrows and Enter to the actual item | ChromiumCompositeTests.RovingTree_Chromium_RealTreeitemFocusRoutesKeysAndActivation |
| Real browse traversal does not expand hidden content, move focus or send keys | ChromiumCompositeTests.CompositeBrowse_Chromium_DoesNotDispatchKeysOrExpandHiddenContent |
| The existing command budget still stops further observation/input; prior reference profiles cannot pass as v9 | ScreenReaderSessionTests.CommandBudget_AtLimit_PreventsFurtherObservationAndInput; ReferenceValidationTests.Assessment_PreviousReaderProfile_RequiresFreshEvidence |

The reviewed focused suite contains 46 cases: 40 browser-independent and six real Chromium cases. The first 45 passed together and the added real roving-tree case passed separately before final full-suite validation. The first executed run passed 41 of 43: two tests incorrectly expected only the immediate tree/tablist container, excluding the real enclosing main landmark. Assertions now require the complete exposed ancestor sequence. Review added known document-conflict cases. No test was skipped or engine failure reclassified as a website barrier.

## Validation

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --logger "trx;LogFileName=reader-v9-windows.trx" --results-directory artifacts/reader-v9
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --composites
```

The --composites demo verifies ReadOnlyBrowseObserved, TreeHierarchyObserved, TreeActivationVerified, ManualTabSelectionVerified and HiddenContentExcluded. Its JSON includes steps, ordered events and the fixture/browser environment. The fixture remains embedded and network-blocked, with Chromium sandboxing enabled and the existing keyboard allowlist. One regression deliberately uses an explicitly separate keyboard controller to collapse a tree; the reader's later observation reports no input and does not retain a stale cursor.

Final local Windows validation discovered and executed 277 cases: Passed: 277, Failed: 0, Skipped: 0. This comprises 230 browser-independent and 47 real Chromium cases, retaining all 231 v8 test identities. TRX counters and individual result records agree with solution discovery. Clean locked Release build passed with zero warnings/errors and architecture enforcement; format verification passed. The composite demo passed all five summary flags. Local evidence is retained in artifacts/reader-v9.

The [CI workflow](https://github.com/jvelezc/ai-screen-reader/actions/workflows/verify.yml) runs the full suite and composite demo on Windows and Ubuntu after every push to main; remote receipts are separate from local results. Local Ubuntu WSL is not rerun for this increment.

No fresh v9 NVDA comparison, hosted deployment or customer acceptance is claimed. Generalized tables/treegrids, multiselection, typeahead, asynchronous tree loading, cross-target/arbitrary-site frames, rendered/cross-run text flow, editable text, speech arbitration, durable artifacts and capture/egress isolation remain outside this increment. Milestones 0 and 1 remain partial.
