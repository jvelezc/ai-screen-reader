# Targeted NVDA comparison for reader v4

Date: October 18, 2026. HSR profile: hsr-desktop-reader-v4.

After the user reopened Speech Viewer, actual NVDA output was inspected for the changed required/protected semantics and backward navigation. This is a targeted comparison, not a rerun of every reference case or product-wide compatibility validation. No implementation change was made during this check.

## Environment

Installed NVDA product 2026.2, file version 2026.2.0.57664; running NVDA with English speech observed. Headed Chromium 153.0.8010.12, Playwright assembly 1.63.0.0, Windows 10.0.26200. Exact NVDA layout, mode automation, verbosity and add-ons remain unaudited.

The reference runner used the final isolated v4 build. Choices, reading and a protected-field supplement each used a fresh trusted fixture session. Foundation and Widgets hashes match the automated baseline. Runner metadata is retained beneath artifacts/reader-v4/nvda-fresh-6a8f7584616d4b8baf4dc20bbcb67ea1. Completed fixture windows were closed; NVDA and Speech Viewer were left available.

Only relevant synthetic-fixture speech was transcribed. Speech Viewer screenshots provided the observations; the browser accessibility tree was not substituted for NVDA output. No text was entered, no privacy/security settings were changed, and no unrelated viewer history was copied into the worksheet.

## Findings

| Area | Observed NVDA behavior | Comparison |
| --- | --- | --- |
| Required checkbox | Tab announced Accept terms, check box, not checked, required, invalid entry, and Required to save preferences. Space checked it. After leaving and returning with Shift+Tab, NVDA announced checked and required with the description. | Comparable with v4's unchecked/checked required announcements. HSR exposes Required.Source=NativeHtmlProperty. The prior missing-required-state mismatch is addressed for this fixture. |
| Radio group | Tab announced Delivery speed grouping and Standard checked 1 of 2. Right announced Express checked 2 of 2. | Checked transition and group/position agree with HSR's observed state and exposed-peer context. This does not verify a logical total in a virtualized set. |
| Backward headings | From document end, Shift+H reached Controls heading level 2, then Screen reader fixture heading level 1. | Comparable with v4's previous-heading fixture path. |
| Backward link | From document end, Shift+K reached Read details link, with Primary navigation context. | Comparable with HSR's previous-link result. |
| Backward landmark | Control+End reported main landmark Ready. Shift+D then reached Primary navigation; another Shift+D reported no previous landmark. | Different: HSR DocumentEnd then PreviousLandmark returns main first, then Primary. HSR enumerates earlier landmark nodes in AX order; NVDA skips the enclosing landmark context already occupied by its reading position. Recorded as DIFF-004, an unresolved compatibility/design difference. No equivalence claim or automatic wording-only patch. |
| Protected edit supplement | Focusing the synthetic Secret field announced Secret, edit, protected, with masked content. No typing occurred. | Protection semantics agree with HSR's protected textbox announcement and native-property provenance. This supplements the comparison; it does not complete the full controls case or prove credential-capture privacy. |

The landmark difference is distinct from a failed input, denied policy or accessibility barrier. The current HSR behavior is covered by ChromiumGapTests.BackwardNavigation_Chromium_TraversesHeadingLinkAndLandmarkWithoutBrowserInput. Whether the profile should instead navigate between enclosing landmark contexts requires an explicit behavior decision; the observation is preserved rather than hidden as a pass.

## Recorded result and limits

The new artifacts/reader-v4/nvda-reference-targeted.json records choices as Comparable and reading as Different, with its explanation. The other five cases remain NotRun; the protected-field supplement is documented here without marking the full controls case complete. The original worksheets are untouched.

The current assessor returns exit 3: Incomplete, Comparable=1, Different=1, NotRun=5, CannotObserve=0, REFERENCE_ENVIRONMENT_INCOMPLETE. NvdaSettings remains null rather than using an invented configuration. No HSR_PROFILE_MISMATCH is present because these are genuine current-profile observations.

Declared logical positions, the idle observation API, the other five complete reference scenarios and full NVDA configuration were not manually validated in this targeted check. Existing final Windows/Linux automated results remain 122 discovered/executed, 122 passed, 0 failed, 0 skipped on each; no tests were rerun for these documentation/worksheet changes. No CI or deployment was performed.
