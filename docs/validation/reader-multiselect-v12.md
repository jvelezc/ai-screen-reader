# Reader v12 exposed multi-select listboxes

Date: October 19, 2026. Profile: hsr-desktop-reader-v12. This increment implements the bounded multi-select listbox feature selected after v11 paragraph reading. See [decision 0013](../decisions/0013-exposed-listbox-selection.md).

The trusted Fruit fixture initially exposes Apples selected, Pears unselected and Plums disabled. It also has a hidden selected option that the reader never reports. ArrowDown changes the active descendant to Pears without selecting it; Activate sends Space through the focused listbox, verifies Apples and Pears among exposed options, then deselects Pears with a second Space. A second fixture list exercises actual roving option focus.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Source-tagged multiselectability and selection; unknown states, exposed-only scope and immutable history | MultiSelectSessionTests.SelectionContext_PreservesSourcesUnknownsAndExposedOwnerScope; FocusedActivation_SelectsAndDeselects_WithoutRepairingFocus; FreshFocusedRead_EmitsSelectionContextWithoutInventingBrowseCursor |
| Browse-only listbox/option navigation in both directions, no wrap, no focus/selection changes | MultiSelectSessionTests.OptionBrowse_IsScopedNonwrappingAndReadOnly; ListboxBrowse_TraversesBothDirectionsWithoutWrapping; ListboxBrowse_EmptyAndFocusModeAreExplicit |
| Actual focus required; owner active descendant and roving focused option can select/deselect | MultiSelectSessionTests.FocusedActivation_SelectsAndDeselects_WithoutRepairingFocus; BrowseActivation_RequiresActualFocusAndNeverRepairsIt |
| Disabled option and owner block input | MultiSelectSessionTests.DisabledOptionOrOwner_IsBlockedWithoutInput |
| Missing/cyclic/foreign ancestry, nested options/root boundaries, unknown/conflicting states and stale descendants cannot authorize input | MultiSelectSessionTests.Activation_UncertainStaleOrForeignOwnership_IsUnsupportedWithoutInput; SelectionContext_ExcludesOptionsWithoutKnownLocalOwnership; SelectionContext_RejectsAmbiguousIntermediateAncestors; RemovedOption_ClearsBrowseCursorAndCannotBeActivatedByStaleIdentity |
| Exact exposed-set goal pins live listbox/document/frame identity; no retry or invented barrier on failed verification | MultiSelectSessionTests.ExposedSetGoal_RequiresExactKnownStateAndLiveIdentity; ExposedSetGoal_EmptyAndNonemptySetsVerifyWithoutClaimingHiddenSelection; UnchangedSelection_TimesOutAsUnknownWithoutRepeatingSpace |
| Real Chromium provenance, hidden exclusion, scoped browse and unchanged focus/selection | ChromiumMultiSelectTests.MultiSelectCapture_Chromium_PreservesSourcesAndExcludesHiddenSelectedOption; OptionBrowse_Chromium_IsScopedNonwrappingAndLeavesFocusAndSelectionUntouched |
| Real Chromium multi-selection/deselection under both focus models | ChromiumMultiSelectTests.ActiveDescendantSelection_Chromium_SelectsTwoThenDeselectsOneThroughActualOwner; RovingSelection_Chromium_MovesFocusWithoutSelectionThenTogglesFocusedOption |
| Real disabled/focus-required/unsupported single-choice and externally removed options | ChromiumMultiSelectTests.DisabledSelection_Chromium_IsBlockedAndBrowseActivationRequiresFocus; SingleChoiceActivation_Chromium_RemainsUnsupportedAndDoesNotChangeSelection; ExternalRemoval_Chromium_DropsStaleOptionAndRefusesToRepairActiveDescendant |
| Older reference evidence cannot count as v12 | ReferenceValidationTests.Assessment_PreviousReaderProfile_RequiresFreshEvidence |

The focused suite passes 54 cases: 47 core and seven real Chromium. Self-review retained combobox Enter behavior and added malformed intermediate-ancestor and initial focused-read event cases.

## Validation

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --logger "trx;LogFileName=reader-v12-windows.trx" --results-directory artifacts/reader-v12
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --multiselect
```

Local Windows validation discovered and executed 431 tests: Passed: 431, Failed: 0, Skipped: 0. This comprises 357 core and 74 real Chromium cases; all 377 v11 test identities remain. Solution discovery, TRX counters and individual result records agree. Clean locked Release build passed with zero warnings/errors and architecture enforcement; formatting passed. Local evidence is retained under artifacts/reader-v12.

All six CLI checks passed: SourceTaggedSelectionObserved, ExposedSelectionScopePreserved, ReadOnlyBrowseObserved, ActualFocusRequired, MultipleSelectionAndDeselectionVerified and DisabledOptionBlockedWithoutInput. CI runs this demo and the complete suite on Windows and Ubuntu after direct commits to main. Exact remote receipts are tracked in GitHub Actions and the architecture Page. Local WSL, fresh v12 assistive-technology checks, hosted deployment and customer acceptance were not performed.

This contract describes exposed options in trusted ARIA fixtures. It cannot verify hidden/virtual selections, generalized native select-multiple modifier behavior, unknown keyboard models, typeahead or asynchronous loading. Raw PressKey retains its explicit allowlisted focus-mode contract. No arbitrary sites, credentials, recordings or hosted services were added; Milestones 0 and 1 remain partial.
