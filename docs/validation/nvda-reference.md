# NVDA reference comparison

Status: all seven bundled scenarios were exercised with live NVDA 2026.2 on October 18 against hsr-desktop-widgets-v2; see the [observations and explained differences](nvda-live-comparison-2026-10-18.md). Exact NVDA configuration metadata remains unaudited. The current hsr-desktop-reader-v7 profile adds [bounded child frames](reader-frames-v7.md) after [v6 text navigation](reader-text-v6.md) and the [v5 enclosing-landmark fix](reader-landmarks-v5.md). No fresh NVDA run was performed for v7. Historical worksheets are preserved and rejected as current-profile evidence.

The [targeted v4 comparison](nvda-v4-targeted-comparison.md) now records one Comparable case, one Different case and five NotRun cases, plus a protected-edit supplement. Required state is confirmed; backward landmark behavior differs. Configuration metadata is still incomplete, so this is not a completed seven-case assessment.

The [fresh targeted v5 reading check](reader-landmarks-v5.md) confirms the changed landmark sequence as well as heading/link semantics. Its new worksheet records reading as Comparable, with six other cases NotRun and configuration metadata incomplete. The original v4 Different finding remains historical; it is not edited into a pass.

Use this optional procedure to compare selected behaviors of the current HSR profile with real NVDA. HeadlessScreenReader is an independent reader for AI agents; NVDA is neither a runtime dependency nor a required acceptance gate. Generated HSR announcements and passing Chromium tests do not establish NVDA behavior. The original architecture plan is preserved; this clarification supersedes its mandatory NVDA comparison requirement. Privacy and isolation gates remain unchanged.

## Start a case

Build the solution and install the pinned Chromium using the README commands. Start your chosen NVDA installation and record its actual version and configuration. This runner does not install or launch NVDA.

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --reference-case dialog
```

This opens a headed Chromium window with one trusted fixture and prints the case instructions, actual browser/Playwright versions, host/runtime, HSR profile, start time, and fixture content SHA-256. It sends no HSR reader commands and does not capture speech, screenshots or video. Use the physical keyboard with NVDA. Close the fixture window to end the session, or Ctrl+C in the terminal; it expires after ten minutes. Closing the window does not mark any reference case passed.

Run each case in a fresh session; close and relaunch between cases. Do not refresh the about:blank page, because that discards the fixture. Move focus into the document if the browser toolbar has focus. Stay on the bundled page and leave Account/Secret fields untouched.

The following IDs are available through --reference-case and --help:

| Case | Compare |
| --- | --- |
| reading | Foundation heading levels/order, Read details link, Primary navigation landmark and main landmark |
| controls | Named and unnamed buttons, disabled Unavailable, and browse activation versus actual DOM focus |
| dialog | Review preferences dialog name/role, initial Close review focus, Tab/Shift+Tab containment, Escape and focus restoration |
| choices | Accept terms checked state; Standard to Express radio selection and focus |
| combobox | Delivery region expansion; North then South active options/selection; Enter commitment and collapse |
| validation | Save with fields unchanged; focused Accept terms before saving alert and checkbox invalid state |
| save-route | Check terms, choose Express and South, save, observe delayed Preferences saved heading, then inspect disabled Continue to payment |

NVDA provides single-letter browse navigation and a browse/focus mode toggle. HSR's typed mode commands are its own interface. Ensure widget keys reach the page in focus mode, and record any automatic mode changes. NVDA may consume Escape to leave focus mode before the page receives it. [NVDA browse mode and keyboard reference](https://api.nvaccess.org/documentation/userGuide.html#BrowseMode)

NVDA's optional Speech Viewer displays its spoken text. If you use it, record only the relevant synthetic-fixture observations in the worksheet. Automatic speech collection is not part of this runner. [NVDA Speech Viewer](https://api.nvaccess.org/documentation/userGuide.html#SpeechViewer)

## Record observations

A fresh, unfilled worksheet is already generated locally at artifacts/nvda-reference-worksheet.json. Preserve filled results. To create another worksheet under a new filename:

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --reference-template |
  Set-Content -Encoding utf8 artifacts/nvda-reference-new.json
```

Fill NvdaVersion, BrowserVersion, WindowsVersion, NvdaSettings and ObservedAtUtc (an ISO 8601 timestamp). Include NVDA keyboard layout, language, browse/focus automation, relevant verbosity settings and add-ons in NvdaSettings. Copy the browser version from the runner's environment output; retain its fixture hash and session metadata with your notes.

Each of the seven cases starts at Result=NotRun. Fill NvdaObservation with what actually happened and what NVDA announced, including uncertainty about focus. Fill ComparisonNotes with how that compares to HSR's observed behavior. Use one of:

- Comparable: the selected semantics agree; describe wording or presentation differences in the notes.
- Different: the observed behavior differs; DifferenceExplanation is required. An unresolved difference should be stated as unresolved, not guessed away.
- CannotObserve: record what prevented observation; the overall review remains pending.
- NotRun: the case has not been exercised; the overall review remains pending.

Keep the case IDs and hashes. New fixture content or an HSR profile change requires a fresh comparison. The validator checks against the current build's embedded fixture text. It does not independently verify that NVDA ran, authenticate the operator, or validate the truth of manual observations.

## Review results

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --assess-reference artifacts/nvda-reference-worksheet.json
```

The assessor requires exactly the current seven unique cases, current schema/profile/hashes, environment metadata, observations and comparison notes. Different cases also require an explanation. It reads at most 1 MiB of UTF-8 JSON, accepts a UTF-8 BOM, and rejects unknown fields and unknown/numeric result values.

| Exit | Meaning |
| --- | --- |
| 0 | Selected cases reviewed from operator-supplied data, possibly with explained differences |
| 2 | Invalid arguments, unreadable/oversized worksheet, or invalid JSON/schema fields |
| 3 | Incomplete or pending reference evidence |

SelectedCasesReviewed never means product-wide compatibility or accessibility conformance. The generated blank worksheet currently returns exit 3, zero comparable cases, seven NotRun cases, and REFERENCE_ENVIRONMENT_INCOMPLETE.

## Differences to investigate

HSR requires actual browser focus before activation and does not relocate it to a virtual-cursor target. Its Escape always dispatches a browser key. It announces a composite's exposed active option while retaining the DOM owner separately. Its radio state wording is checked; it does not arbitrate unsolicited live-region speech. Chromium CDP may expose different detail from the platform accessibility API used by NVDA. Compare semantics, focus and timing, and preserve any discrepancy rather than demanding identical speech strings.

For an HSR baseline, use the existing automated tests and synthetic --widgets/standard demo transcripts from the same build. The runner's case metadata names a relevant test for each comparison. Automated baseline success must remain separate from manual NVDA findings.

## Automated checks for this increment

Windows and Ubuntu 24.04.4 under WSL2 each discovered and passed 73 tests, with 0 failed and 0 skipped: 61 browser-independent cases and 12 real Chromium cases. All 43 previous cases remain present. Release builds with warnings as errors, locked restore and formatting verification passed on both platforms. The Linux run uses the same user-local SDK and extracted browser dependencies documented in the widget milestone report.

| Requirement | Evidence |
| --- | --- |
| Unrun reference evidence stays unrun | ReferenceValidationTests.Template_LeavesEveryReferenceCaseUnrun |
| Complete metadata and selected observations can be reviewed, with scope preserved | ReferenceValidationTests.Assessment_CompleteObservations_ReviewsOnlySelectedCases |
| Differences need explanations; unobserved cases remain pending | ReferenceValidationTests.Assessment_Difference_RequiresExplanationAndPreservesFinding; ReferenceValidationTests.Assessment_UnfinishedCase_RemainsPending |
| Missing/duplicate/unknown cases, stale profile/hash/schema, missing environment/observations and invalid result cannot complete | ReferenceValidationTests.Assessment_InvalidEvidence_CannotComplete |
| Strict parsing, UTF-8 BOM and byte bounds | ReferenceValidationTests.Parser_UnknownFieldsOrResults_AreRejected; ReferenceValidationTests.Parser_Utf8Bom_PreservesPendingWorksheet; ReferenceValidationTests.Parser_AtByteLimit_AcceptsValidJson_AboveLimitStopsReading |
| Actual embedded fixture identity and browser lifetime without automatic activation | ChromiumReferenceTests.ReferenceFixture_IdentifiesActualContentAndClosesWithoutAutomaticInput |
| HSR heading, link and landmark baseline | ChromiumReferenceTests.ReferenceFixture_ExposesHeadingLinkAndLandmarkNavigation |

Local evidence: artifacts/reference-windows/reference-windows.trx, artifacts/reference-linux/reference-linux.trx, artifacts/nvda-reference-assessment.json. Automated tests of the lifetime use headless Chromium; they do not validate a headed NVDA interaction. The Windows/Ubuntu CI workflow now checks that a blank reference worksheet remains pending, but no remote job has run. No deployment or real assistive-technology result is claimed.

Use the manual cases to identify useful differences without making NVDA equivalence a product requirement. HSR's own structured behavior and real-browser tests remain its primary acceptance criteria. Capture exclusion remains the next independent feasibility experiment; credential entry and recordings remain unavailable until that protocol is proven.
