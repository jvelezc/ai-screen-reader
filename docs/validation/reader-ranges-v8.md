# Reader v8 range widgets

Date: October 18, 2026. Profile: hsr-desktop-reader-v8.

The range increment adds source-tagged current values, bounds and value text for noneditable sliders, meters and progress bars. NextRange/PreviousRange are browse-only navigation. Steps and node events retain structured range data. Exact numeric goal predicates use exposed consistent values, with missing values remaining unknown. See [decision 0009](../decisions/0009-source-tagged-range-observations.md).

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Unknown, partial, boundary, contradictory and nonfinite range data remain distinct | RangeSessionTests.RangeConsistency_UnknownBoundariesAndInvalidNumbers_AreDistinct |
| Announcements use observed value text or invariant numbers and explicitly absent values | RangeSessionTests.ReadRange_AnnouncesObservedTextNumbersOrUnknown_UsingInvariantCulture |
| Browse navigation is bidirectional with no wrap or browser input; focus-mode navigation is unsupported | RangeSessionTests.RangeNavigation_BothDirections_NoWrapOrInput; RangeNavigation_FocusMode_IsUnsupported; ChromiumRangeTests.RangeNavigation_Chromium_IsReadOnlyAndLeavesUnknownProgressExplicit |
| Native/ARIA range data has exact values and source provenance; editable/unrelated values stay excluded | ChromiumRangeTests.Ranges_Chromium_ExposeNativeBoundsAndSourceTaggedAriaText_ExcludeEditableValues |
| Actual focused keyboard input changes and clamps native and fractional ARIA sliders | ChromiumRangeTests.NativeSlider_Chromium_ActualKeyboardFocusChangesAndClampsObservedValues; AriaSlider_Chromium_FractionalValuesAndTextFollowActualFocus |
| Change events preserve historical numeric data and text-only changes; unchanged reads stay quiet | RangeSessionTests.RangeEvents_OnlyChangedValuesEmitAndRetainHistoricalStructuredData; RangeEvents_TextOnlyChange_IsObservedWithoutNumericChange |
| Numeric verification requires exact consistent values and requested focus; read-only polling refreshes result range | RangeSessionTests.RangeExpectation_RequiresExactObservedConsistentValueAndFocus; RangeVerification_RefreshesResultAfterReadOnlyPolling_WithoutInput |
| Unknown progress cannot prove completion or a barrier; button input verifies only the named progress predicate | RangeSessionTests.UnknownProgress_DoesNotVerifyCompletionOrClaimBarrier; ChromiumRangeTests.Progress_Chromium_VerifiesExactValueAfterButtonInput_WithoutTreatingUnknownAsComplete |
| Activation does not invent focus or Enter/Space semantics for sliders | RangeSessionTests.RangeActivation_DoesNotInventFocusOrEnterSpace |
| Prior-profile reference evidence cannot pass as v8 | ReferenceValidationTests.Assessment_PreviousReaderProfile_RequiresFreshEvidence |

The focused range suite passed 41 cases: 36 browser-independent and five real Chromium cases. An initial test build failed on ambiguous target-typed constructors and ran no tests. The first executed suite passed 36 of 38: two assertions incorrectly matched label text as a control and expected pre-Tab document focus to contain range data. Those tests now select exposed roles and verify the exact pre-key range history. A subsequent review added partial-bound and text-only-change cases. No case was skipped or a product limitation hidden.

## Validation commands and acceptance

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --logger "trx;LogFileName=reader-v8-windows.trx" --results-directory artifacts/reader-v8
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --ranges
```

The --ranges demo reports RangeNavigationObserved, SliderValuesVerified, ProgressValueVerified and UnknownProgressPreserved as true. Its output includes range-bearing steps, ordered events and the fixture/browser environment. Range operations are part of the existing command budget and input gate. The demo uses embedded synthetic markup, Chromium sandboxing and the unchanged local key allowlist.

Final local Windows validation discovered and executed 231 cases: Passed: 231, Failed: 0, Skipped: 0. This includes 190 browser-independent and 41 real Chromium cases, retaining all 190 prior test identities. Clean locked Release build passed with zero warnings/errors and architecture enforcement; format verification passed. Discovery names, TRX counters and individual result records agree. The fixture rejects attempted aria-valuetext reads on excluded fields; the final browser acceptance suite exercises that guard. Results are retained in artifacts/reader-v8.

Local Ubuntu WSL was not rerun for this increment. The [GitHub Actions workflow](https://github.com/jvelezc/ai-screen-reader/actions/workflows/verify.yml) runs the full suite and range demo on Windows and Ubuntu for every push to main; its run receipts are the remote acceptance record, separate from local checks.

No fresh v8 NVDA comparison, deployment or customer acceptance is claimed. v7 and earlier NVDA worksheets remain historical. Editable text, generalized tables, cross-target/arbitrary-site frames, rendered/cross-run text flow, speech arbitration, durable artifacts, capture privacy and hostile-site isolation remain outside this increment. Milestones 0 and 1 remain partial.
