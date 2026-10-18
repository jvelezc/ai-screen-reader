# Live NVDA comparison: October 18, 2026

All seven bundled scenarios were exercised against the running NVDA installation. Actual synthetic-fixture utterances were read visually from NVDA Speech Viewer, separately from Chromium's accessibility tree. Four selected cases were comparable and three had explained differences. No new HSR implementation defect was established by this run. This is a bounded behavioral comparison, not product-wide equivalence or conformance.

## Environment and evidence limits

- Installed NVDA executable: product version 2026.2, file version 2026.2.0.57664. The existing NVDA process was used.
- Headed Chromium: 153.0.8010.12; Playwright assembly: 1.63.0.0; Windows: Microsoft Windows 10.0.26200; HSR: hsr-desktop-widgets-v2.
- Foundation SHA-256: 625c58d518e9655e523ccb92ebe8a460fa87657ef61beffc77afb6d83aac091c.
- Widgets SHA-256: bf6c692f4e5a9d96b138d6658fd7108b3837bb8a61644947d45a295f6c1f2f74.
- Each case used a fresh reference session. Dialog and validation received additional fresh follow-ups. Session metadata is retained under artifacts/nvda-live-5dfbdc75433243cda73bfbc778d1f4eb. Completed fixture windows were closed; NVDA was left running.
- English speech and functioning browse/widget navigation were observed. Exact keyboard layout, automatic mode settings, verbosity and add-ons were not audited. NvdaSettings remains null in the new observed worksheet; assessment therefore stays Incomplete with REFERENCE_ENVIRONMENT_INCOMPLETE. The blank original worksheet is preserved.

Speech Viewer still reported higher Windows integrity than the desktop helper and exposed no transcript through UI Automation. Its screenshots were readable. Only relevant synthetic speech was transcribed into the worksheet; unrelated viewer history was excluded. No recording, credential entry, arbitrary-site navigation, security-setting change or elevation was introduced.

## Selected observations

| Case | Result | Actual NVDA observation and comparison |
| --- | --- | --- |
| reading | Comparable | Document start announced Screen reader fixture, heading level 1. H reached Controls, level 2. Shift+K reached Read details; D and Shift+D reached main and Primary navigation. Names, levels and landmark roles agree with HSR. |
| controls | Different | B reached Open details, the unnamed control (announced simply as button), and Unavailable with unavailable state. B then Enter on Open details revealed Details opened, level 2. NVDA activates its browse target; HSR explicitly requires browser focus first. |
| dialog | Comparable | Keyboard opening announced Review preferences dialog and Close review button, with the Close review focus outline visible. Tab/Shift+Tab wrapped between both buttons. Escape closed it and returned to Review preferences. |
| choices | Different | Checking terms announced checked. Tab/Right changed Standard to Express, announced checked 2 of 2. NVDA also announces required for the native checkbox; HSR's Chromium AX source omits that state. |
| combobox | Comparable | Down announced North 1 of 2, then South 2 of 2. Enter committed South and collapsed the list. NVDA also spoke the status update. The combobox focus outline remained visible; manual DOM owner/active-descendant IDs were not separately instrumented. HSR's separate Chromium tests establish those relations for its baseline. |
| validation | Different | Saving untouched fields announced Accept terms before saving, then the alert. Shift+X returned to the unchecked, required, invalid checkbox and its description. HSR agrees on error/invalid semantics, but does not schedule separate live-region speech and lacks this native required state. |
| save-route | Comparable | Terms, Express and South led to Preferences saved, heading level 2. Tab reached Continue to payment, announced unavailable with its explanation. Enter left the synthetic page unchanged. HSR stops disabled activation before dispatch; this does not establish that every payment route is inaccessible. |

The initial dialog/checkbox/combobox and invalid Save used mouse clicks to establish targets. Keyboard follow-ups covered dialog opening, navigation, selection, browse activation and successful Save. Immediate post-input captures occasionally lagged; later observations were used to confirm changes. UI Automation's stale address-bar focus field was not treated as reliable DOM focus evidence.

## Differences and disposition

1. **Browse activation:** intentional HSR safety/interaction contract. Keep explicit browser focus before dispatch.
2. **Native required state:** a confirmed existing Chromium AX limitation, documented in README. Preserve the missing observation and exposed description; do not invent it from diagnostic DOM attributes. Richer source semantics would require a separately designed adapter change.
3. **Context and live speech:** NVDA includes positions, group/dialog context and independent live updates. HSR provides structured observations and command-scoped output. These are documented capability differences, not regressions to patch solely for matching wording.

HSR-001, the previously reproduced focus-mode cursor synchronization defect, remains fixed. Its Windows and Ubuntu WSL TRX files were rechecked: each contains 77 discovered/executed results, 77 passed, 0 failed and 0 skipped, including 13 Chromium cases. Those automated runs preceded this documentation-only live comparison; tests were not rerun for prose changes. No Linux NVDA comparison, remote CI or deployment was performed.

Observed worksheet: artifacts/nvda-reference-observed-2026-10-18.json. Assessment: artifacts/nvda-reference-observed-2026-10-18-assessment.json. These preserve the incomplete configuration metadata instead of turning successful selected interactions into an unqualified acceptance claim.
