# Comparison attempt and reader issue catalogue

Started: October 17, 2026. Updated: October 18, 2026.

Current status: the [October 18 live comparison](nvda-live-comparison-2026-10-18.md) exercised all seven scenarios using actual Speech Viewer output. Four selected cases were comparable; three have explained differences. Exact NVDA configuration metadata remains incomplete. Earlier failed attempts below are retained as historical evidence.

Subsequent implementation: [context/live improvements in v3](reader-context-v3.md) add exposed container context, scoped peer positions and typed live-change events, with 96 passing Windows/Linux tests. The live NVDA observations below cover v2 and are not promoted to acceptance of the new profile.

Latest reader-first increment: [v4 gap closure](reader-gaps-v4.md) adds source-tagged native/ARIA semantics, logical positions, backward navigation, protected edits and bounded idle observation. The native checkbox-required implementation gap in DIFF-002 is now addressed through an explicit native-property source, not claimed as AX data. Final Windows/Linux suites each pass 122 tests. A [targeted fresh v4 NVDA check](nvda-v4-targeted-comparison.md) confirms required/protected semantics and backward headings/link behavior, and records the new landmark difference DIFF-004. Full reference assessment remains incomplete; older observations remain historical.

HeadlessScreenReader remains an independent screen reader for AI agents. NVDA is an optional external benchmark. This catalogue separates confirmed HSR defects from comparison-environment limitations; it does not treat a tool failure as an accessibility barrier.

Earlier v5 follow-up: [landmark gap closure](reader-landmarks-v5.md) resolves DIFF-004 by skipping known enclosing landmarks during PreviousLandmark. Windows/Linux each pass 131 tests, including 22 Chromium cases. A fresh NVDA reading check confirms the changed Foundation sequence. Other v4 observations remain historical; a full configured seven-case v5 assessment remains incomplete.

The v6 increment: [exposed text navigation](reader-text-v6.md) adds source-tagged ranges, character/word/logical-line traversal and explicit invalidation. Windows/Linux each pass 162 tests, including 26 Chromium cases. No fresh v6 NVDA comparison was performed; all earlier comparison observations remain historical.

Earlier profile v7: [bounded child frames](reader-frames-v7.md) adds nested trusted document observations, frame/generation identity, actual child focus and explicit coverage exclusions. Windows/Linux each pass 190 tests, including 36 Chromium cases. No fresh v7 NVDA comparison was performed. The hosted platform and full reference acceptance remain open.

Latest profile v8: [range widgets](reader-ranges-v8.md) add source-tagged values/bounds/value text for noneditable slider, meter and progressbar roles, range navigation, numeric predicates and range-bearing events. The full suite contains 231 cases. No fresh v8 NVDA comparison is claimed; current remote results are recorded in [GitHub Actions](https://github.com/jvelezc/ai-screen-reader/actions/workflows/verify.yml). Hosted and capture-privacy gates remain open.

## Findings

| ID | Classification | Finding | Resolution |
| --- | --- | --- | --- |
| HSR-001 | Confirmed reader defect; fixed | In focus mode, an external browser-focus change or active-descendant change could update the announcement while retaining the previous CursorNodeId. Announcement events also referenced that stale node. Loss of focus could leave a cursor pointing at an existing but unfocused node. | Refresh now synchronizes the focus-mode cursor with the observed effective focus, including null. Browse-mode virtual cursor independence and document replacement behavior are preserved. |
| ENV-001 | Setup prerequisite; dismissed by user | NVDA showed a Usage Data Collection privacy prompt. No privacy choice was made by the agent. | User reported dismissing the prompt; this is not an HSR defect or accessibility finding. |
| ENV-002 | Desktop activation failure; comparison unverified | Earlier desktop calls produced no observed menu/focus change while the user was also interacting. After the user requested another idle retry, a new fixture, fresh window binding and a reset desktop connection were used. The control click then returned `failed to activate captured window`, including after one fresh rebind/retry. No NVDA Speech Viewer utterance was observed. | This is a desktop-tool activation failure, not evidence of an NVDA or page-navigation defect. Speech Viewer setup and manual foregrounding were requested to establish a usable comparison boundary. Actual NVDA reference cases remain unverified. |
| ENV-003 | Initial preconditions verified on retry | The earlier desktop observation showed terms checked and a validation message before a controlled sequence was established. The fresh retry showed terms unchecked, Standard selected, no chosen region and no validation alert. | The retry starts cleanly, but no post-input benchmark result is credited because activation failed. |
| ENV-004 | Speech Viewer inspection limited | After the user opened Speech Viewer, the tool reported: `Accessibility is limited because the target window has higher Windows integrity than the Computer Use helper.` The returned tree exposed only the window/title bar, not the speech transcript. | Speech Viewer is present, but its accessibility tree cannot establish fixture utterances. No security settings or process elevation were changed. This diagnostic does not establish the cause of the separate Chromium activation failure. |
| ENV-005 | Capture and target disagreement; retry failed | On the next user-ready attempt, a fresh fixture was listed and its accessibility tree showed the clean preference workflow. The accompanying screenshot instead showed the game desktop and Speech Viewer. Explicit fixture activation followed by capture returned `no screenshot targets found for process:C:\Users\josej\AppData\Local\ms-playwright\chromium-1243\chrome-win64\chrome.exe`. A fresh window listing, binding and one activation/capture retry returned the same error. | No fixture navigation input was sent in this attempt. The mismatched screenshot was not used for coordinates or credited as a fixture observation. Real NVDA comparison remains incomplete; a working foreground/capture boundary must be established before continuing. |

The native UI automation observation is not NVDA speech. At the end of the October 17 attempts no Comparable result was credited. October 18 results instead use actual Speech Viewer output and are recorded separately from generated HSR announcements. No product-wide NVDA equivalence is claimed.

## HSR-001 reproduction and fix

1. Enter focus mode with browser focus on one exposed control.
2. Move focus using an external keyboard controller, or externally change a composite's active descendant while keeping DOM focus on its owner.
3. Read the current node without dispatching another browser key.

Before the fix, the announcement and FocusedNodeId/ActiveDescendantNodeId reflected the new observation while CursorNodeId could still identify the previous target. The stale ID could mislead an AI client correlating an announcement with its semantic node. When focus disappeared, the announcement could report no current content while the cursor still named an old node.

The fix changes observation synchronization only in focus mode. It does not move browser focus, inject input, change activation policy, or make the browse cursor follow every browser-focus change. Input from the external controller is explicit in the Chromium regression fixture; the reader's subsequent read reports zero browser keystrokes.

The pre-fix focused run discovered five cases: four failed and one passed. The failures reproduced normal focus change, active-descendant change, loss of focus and the real Chromium path. After the fix, the same five cases passed.

| Requirement | Evidence |
| --- | --- |
| Normal focus changes update result and announcement node IDs without input | ScreenReaderSessionTests.FocusMode_ExternalFocusChange_ReadsActualBrowserFocus |
| Loss of browser focus clears the stale cursor | ScreenReaderSessionTests.FocusMode_LostFocus_ClearsCursorEvenWhenOldNodeStillExists |
| Composite cursor follows the active option while preserving its DOM owner | WidgetSessionTests.FocusMode_ExternalActiveDescendantChange_SynchronizesAnnouncedCursor |
| Browse-mode cursor remains independent | ScreenReaderSessionTests.BrowseMode_ExternalFocusChange_PreservesIndependentVirtualCursor |
| Actual external keyboard movement is observed in Chromium | ChromiumFixtureTests.FocusMode_ExternalKeyboardFocus_ReportsMatchingCursorAndAnnouncement |

## Final automated validation

| Gate | Result |
| --- | --- |
| Windows Release build | Passed with 0 warnings/errors and locked dependency restoration, using separate artifacts output |
| Windows tests | 77 discovered/executed, 77 passed, 0 failed, 0 skipped; 64 browser-independent cases and 13 Chromium cases |
| Ubuntu 24.04.4 WSL Release build | Passed with 0 warnings/errors and locked dependency restoration |
| Linux tests | 77 discovered/executed, 77 passed, 0 failed, 0 skipped; all 13 Chromium cases exercised |
| Formatting | Verified on Windows and Linux |
| CLI widget workflow | Passed on Windows and Linux, with saved preferences and observed disabled route |
| Remote CI | Not run |
| Deployment | Not performed |
| Real NVDA comparison | Seven scenarios observed October 18; four comparable and three explained differences. Configuration metadata remains incomplete; see linked live report. |

The initial Windows full rebuild encountered locks from the running reference fixture's DLLs and Playwright driver. It did not run the full tests. The successful build used --artifacts-path artifacts/focus-windows, leaving the comparison process intact. That build passed without weakening warnings or changing project dependencies.

The successful Windows command sequence was:

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental --artifacts-path artifacts/focus-windows -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --artifacts-path artifacts/focus-windows --no-build --no-restore --logger "trx;LogFileName=focus-final-windows.trx" --results-directory artifacts/nvda-audit
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
```

Linux used a separate source copy beneath the existing user-local SDK directory and the same process-scoped browser dependency path documented in the widget milestone. ChromiumSandbox remains enabled. Source/configuration/lock identity is checked separately from build outputs.

Local TRX evidence is retained at artifacts/nvda-audit/focus-before.trx, focus-after.trx, focus-final-windows.trx, and focus-final-linux.trx. The final counts are checked against the XML counters and individual result records.

The October 18 follow-up below supersedes the earlier lack of NVDA utterances. No additional implementation defect was established by the live interactions.

## October 18 retry

The previous fixture and Speech Viewer windows were closed. A fresh dialog reference session was launched; the existing NVDA process was still running. The initial capture again disagreed with its target, but explicit Chromium activation then produced a matching screenshot and accessibility tree. A coordinate click on Review preferences visibly opened the native dialog, and a subsequent settled accessibility observation exposed its name and both buttons. Desktop activation and page input therefore worked in this attempt.

Immediate post-click snapshots sometimes retained the previous tree or image; a later independent observation confirmed the dialog. An input API return alone was not credited as successful state change. The NVDA menu shortcuts attempted with Insert and numpad modifiers exposed no menu window, and Speech Viewer was not present. The user was asked to reopen Speech Viewer. These page observations establish neither NVDA speech nor a completed reference case; the worksheet remains pending.

After the user reopened Speech Viewer, its screenshots exposed real synthetic-fixture utterances despite the limited UI Automation tree. The seven cases and fresh dialog/validation follow-ups were exercised. Completed synthetic browser windows were closed. See the live report for case details and the new worksheet; earlier environment failures do not invalidate these later successful observations.

| ID | Classification | Observed difference | Disposition |
| --- | --- | --- | --- |
| DIFF-001 | Intentional interaction contract | NVDA activated Open details from its browse target; HSR requires actual browser focus before activation. | Preserve explicit focus/input facts. |
| DIFF-002 | Original source limitation; fixed with provenance in v4 | NVDA announces native checkbox required; this Chromium AX source omits it, while retaining invalid state and the exposed requirement description. | v4 added a fixed read-only native-property supplement with explicit source provenance. Targeted v4 NVDA comparison confirmed required state. No diagnostic inference is presented as AX data. |
| DIFF-003 | Structured context/live capability added; speech presentation remains distinct | NVDA adds group/position context and separate live/status utterances. | v3/v4 added exposed ancestry/peer context, declared logical positions and bounded idle live observations. These are structured observations; NVDA speech timing/arbitration remains outside the contract. |
| DIFF-004 | Observed v4 navigation difference; fixed in v5 | From the end of content inside main, NVDA Shift+D reaches Primary navigation directly, then reports no previous landmark. HSR v4 first returned main, then Primary navigation. | v5 skips known enclosing ancestors, preserves earlier siblings and unknown parentage, and sends no input. Deterministic and actual Chromium regressions pass; a fresh NVDA reading check confirms Primary then no previous landmark. The targeted v4 worksheet remains Different as historical evidence. |
