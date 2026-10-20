# Reader v13 focused listbox typeahead validation

See [the contract](../decisions/0016-focused-listbox-typeahead.md) and [copy-paste Claude instructions](../prototype/copy-to-claude.md). Use reader_execute with command Typeahead and key equal to the complete prefix in the trusted Typeahead fixture. Public typing remains denied.

| Requirement | Evidence |
| --- | --- |
| Reject null/empty, whitespace, punctuation, Unicode, controls and 17-character prefixes before capture/input | TypeaheadSessionTests.InvalidPrefix_IsDeniedWithoutCaptureOrInput |
| Exact 1100ms idle wait, lowercase input, actual focus/goal and separate selection | TypeaheadSessionTests.Typeahead_WaitsExactIdleBoundaryThenVerifiesActualFocusWithoutChangingSelection |
| Exact 16-character bound | TypeaheadSessionTests.MaximumPrefix_SendsExactlySixteenCharacters; ChromiumTypeaheadTests.ChromiumPrefix_UsesActualFocusedListboxAndPreservesMultiselection |
| Digit prefixes work only through the scoped operation | TypeaheadSessionTests.NumericPrefix_IsScopedAndObservedWithoutEnablingRawDigitKeys |
| Unmatched observed focus remains Unknown | TypeaheadSessionTests.UnmatchedObservedFocus_IsUnknownWithCompletedInputAndUnchangedSelection |
| Browse mode and disabled current/owner cannot type | TypeaheadSessionTests.BrowseMode_CannotTurnPrefixIntoInput; DisabledFocusOrOwner_BlocksBeforeInput |
| Ownership, identity, editability and complete ancestry required | TypeaheadPolicyTests.ScopedInput_AuthorizesOnlyActualOwnedActiveOrRovingFocus; UnsupportedOwnershipOrEditableFocus_CannotReachTypedInput |
| Scope changes and cancellation/partial failures stop further input and retain counts/uncertainty | TypeaheadSessionTests.FocusChangedDuringReset_IsDeniedBeforeFirstCharacter; MidPrefixFailure_PreservesCompletedCharactersAndStopsFurtherInput; CancellationDuringReset_DoesNotDispatchInput |
| Unsupported adapters and public typing default to denial | TypeaheadPolicyTests.BrowserContract_DefaultsToDeniedForAdaptersWithoutTypedInput; PublicReadOnlyPolicy_DeniesTypedListboxInputEvenWithValidFocus |
| Real repeated prefixes, multiple matches and disabled/hidden exclusions | ChromiumTypeaheadTests.RepeatedPrefix_ResetsBufferAndVisitsOtherExposedMatchButNeverDisabledOrHiddenOption |
| Real roving focus and selection-following-focus behavior | ChromiumTypeaheadTests.RovingPrefix_ObservesRealOptionFocusWithoutInventingSelection; SelectionFollowsFocus_IsReportedAsObservedWidgetBehavior |
| Real editable override and stale document rejected | ChromiumTypeaheadTests.EditableOverride_AndStaleDocumentCannotAuthorizeTypedInput |
| Real stdio MCP bounds, typed input, focus goal, selection, timeline and raw-key denial | ChromiumMcpTests.StdioTypeahead_AdvertisesBoundsAndVerifiesFocusedPrefixWithSelectionSeparate |

Focused runs passed 43 cases: 36 deterministic core and seven real Chromium/MCP cases. FakeTimeProvider proves before/at idle boundary and cancellation without wall-clock sleeps. Browser acceptance uses actual sandboxed Chromium and the production MCP gate. A single-select example deliberately exposes selection-following-focus; multi-select examples preserve their selected set.

Local Windows passed 568 discovered/executed cases with zero failures/skips: 478 core and 90 real Chromium. All 525 previous prototype identities remain. Clean locked Release build has zero warnings/errors; architecture enforcement and formatting verification passed. Exact discovery/result identities and TRX counters are checked. Remote receipts are tracked in GitHub Actions and the architecture Page.

The --typeahead CLI demo passes PrefixAndResetVerified, SelectionSeparate, NoMatchRemainsUnknown, RovingFocusVerified and RawPrintableKeyDenied. Both CI platforms run it. Claude model acceptance still requires client login. Fresh assistive-technology and hosted deployment acceptance remain separate.
