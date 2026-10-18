# Reader v5: remaining confirmed landmark gap

Date: October 18, 2026. Scope: DIFF-004 and regression verification of the existing reader. Profile: hsr-desktop-reader-v5.

## Result

PreviousLandmark now leaves the cursor's known enclosing landmarks and reaches an earlier landmark outside that context. From the Foundation fixture's final content, the sequence is Primary navigation, then NoMatch. The cursor remains at Primary on NoMatch. Nested and adjacent landmark paths are covered without moving browser focus or dispatching input. Parentage is observed, never inferred from the order of nodes.

This closes the remaining confirmed implementation mismatch from the targeted v4 comparison. DIFF-001 remains the intentional actual-focus activation contract. DIFF-002's native required state was fixed in v4. DIFF-003's context/live observations were added in v3/v4; unsolicited speech scheduling remains outside the structured observation contract.

## Regression evidence

Before the fix, nine focused cases executed: seven failed and two passed. Both the Foundation and nested Chromium cases reproduced the wrong destination. After the fix, the same nine plus a stale-profile evidence test all passed. The before/after TRX files are preserved under artifacts/reader-v5.

| Requirement | Evidence |
| --- | --- |
| Leave enclosing contexts, retain earlier sibling destinations, and preserve no-wrap/no-input behavior from descendant and landmark positions | LandmarkNavigationTests.PreviousLandmark_NestedContext_ReachesPreviousSibling; ChromiumGapTests.PreviousLandmark_ChromiumNestedContexts_SkipsAncestorsAndPreservesSibling |
| No earlier landmark preserves the current node | LandmarkNavigationTests.PreviousLandmark_OnlyEnclosingLandmarks_PreservesCursorOnNoMatch |
| Missing/cyclic ancestry skips only known parents; absent parentage does not imply enclosure | LandmarkNavigationTests.PreviousLandmark_IncompleteAncestry_SkipsKnownParentsWithoutGuessing; PreviousLandmark_UnobservedParentage_DoesNotInferEnclosureFromOrder |
| Unset cursor, forward landmark traversal and ordinary previous-node traversal retain their contracts | LandmarkNavigationTests.LandmarkNavigation_UnsetCursorAndForwardTraversal_RetainNodeOrder |
| Actual Foundation behavior matches the changed landmark sequence and preserves heading/link navigation | ChromiumGapTests.BackwardNavigation_Chromium_TraversesHeadingLinkAndLandmarkWithoutBrowserInput |
| Historical v4 observations cannot pass as v5 evidence | ReferenceValidationTests.Assessment_PreviousReaderProfile_RequiresFreshEvidence |

## Local validation

Windows and Ubuntu 24.04.4 WSL each discovered/executed 131 tests: Passed: 131, Failed: 0, Skipped: 0. This comprises 109 browser-independent cases and 22 real Chromium cases. The 122 previous test identities remain present. Each platform's TRX counters agree with all 131 individual result records. Clean locked-dependency builds passed with zero warnings/errors and architecture enforcement. Formatting verification passed on both platforms. No package version changed.

Windows used the pinned SDK 10.0.402 and isolated output:

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental --artifacts-path artifacts/reader-v5/windows -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --artifacts-path artifacts/reader-v5/windows --no-build --no-restore --logger "trx;LogFileName=reader-v5-windows.trx" --results-directory artifacts/reader-v5
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
```

Linux used an isolated source copy with the existing user-local SDK/browser dependencies and Chromium sandbox enabled. All 43 source/configuration/fixture/lock files match the Windows checkout byte-for-byte. The --widgets and --observe-live demonstrations passed on both platforms, reporting fixture goal/disabled route observed and idle change observed without observer input. Evidence: artifacts/reader-v5/{reader-v5-windows.trx,reader-v5-linux.trx,test-summary.json,source-manifest.json,linux-source-hashes.txt,discovered-windows.txt,discovered-linux.txt}. The reproducible Linux script is artifacts/reader-v5/validate-linux.sh.

## Fresh optional NVDA reading check

A fresh v5 --reference-case reading session opened the unchanged trusted Foundation fixture. Actual NVDA Speech Viewer output was inspected on October 18, 2026, with installed NVDA 2026.2 and Chromium 153.0.8010.12. No HSR commands were sent by the reference runner; no credential text was entered and no settings changed. Only relevant synthetic-fixture speech was transcribed.

Observed sequence:

1. Click the fixture heading, Control+End: Ready.
2. Shift+D: Primary navigation landmark, Read details link.
3. Shift+D: no previous landmark.
4. Control+Home: heading level 1, Screen reader fixture; H: main landmark, Controls heading level 2; H: no next heading.
5. Control+Home then K: Primary navigation landmark, Read details link; D: main landmark, Controls heading level 2.

This independently confirms the changed Foundation landmark sequence, heading order/levels, and link semantics. The reading case is Comparable in the fresh v5 worksheet. Six other cases remain NotRun; older v4 choices/protection observations are not promoted to v5 results. Exact NVDA configuration/add-ons remain unaudited and NvdaSettings remains null. Assessment stays Incomplete with REFERENCE_ENVIRONMENT_INCOMPLETE. Nested-landmark compatibility was tested against Chromium, not manually compared with NVDA.

One post-key refresh requested neither screenshot nor text and was rejected by the desktop tool. The subsequent fresh capture showed no previous landmark; the key was not repeated or counted twice. Completed synthetic fixture windows were verified closed, leaving NVDA and Speech Viewer available. No unrelated speech history was copied into artifacts.

## Remaining capabilities and external gates

No other confirmed implementation defect remains open in the comparison catalogue. This does not mean all reader capabilities are implemented: child frames, character/word/line text navigation, additional widget patterns and lossless live events/speech arbitration remain unsupported. The sampled live-observation limitations and uncertain/invalid authored semantics remain explicit. A full configured seven-case NVDA assessment has not been completed. Remote CI and deployment were not run. Capture privacy, credentials, arbitrary targets/egress and hosting/MCP remain deferred under the reader-first scope.
