# Reader v7 bounded child frames

Date: October 18, 2026. Profile: hsr-desktop-reader-v7. Scope: next reader capability after v6 text navigation, explicitly selected by the user.

## Implemented result

Trusted embedded same-target about:srcdoc/about:blank child documents are now read in owner order, including nested content. Nodes, text ranges, current-frame results and timeline events retain document/frame identity. NextFrame/PreviousFrame are browse-only virtual navigation. Actual child focus and active-descendant ownership remain separate; activation still requires the actual focused target, including when controls in different frames share a name.

Coverage explicitly distinguishes Observed, Excluded, Unsupported, Unavailable and LimitExceeded. Hidden owners and unsupported data documents are not read. Defaults are eight captured documents and three child levels, with bounded configurable maxima and recorded environment limits. A child document change invalidates its text cursor while retaining the main document cursor/identity. Frame removal retains a historical detachment event. Per-document live-region baselines suppress initial replacement-document text while preserving actual updates.

The frame fixture contains main, child, nested child, hidden child and unsupported data child. The normal capture records three Observed documents, one Excluded and one Unsupported. No child secret, hidden canary or unsupported child text appears in normalized snapshots. Only the fixed trusted fixture and existing keyboard allowlist are used.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Nested reading order, unique document-scoped IDs and observed owner links | ChromiumFrameTests.Frames_Chromium_NestedOrderIdentityAndExcludedCoverageAreExplicit |
| Child and nested actual focus, activation verification, active descendants and live status retain ownership | ChromiumFrameTests.Frames_Chromium_ChildFocusActivationAndActiveDescendantKeepOwners; FrameSessionTests.ChildFocus_ContextAndAnnouncementRetainChildDocumentIdentity |
| Matching names in other frames do not authorize activation | FrameSessionTests.CrossFrameActivation_SameName_RequiresActualTargetFocus |
| Child radio peers never cross a document boundary | ChromiumFrameTests.Frames_Chromium_RadioPeersDoNotCrossDocumentBoundary; FrameSessionTests.ChildFocus_ContextAndAnnouncementRetainChildDocumentIdentity |
| Frame navigation is bidirectional, bounded and browse-only, with no input | FrameSessionTests.FrameNavigation_BothDirections_DoesNotWrapOrDispatchInput; FrameNavigation_FocusMode_IsUnsupported |
| Frame budget boundaries retain explicit coverage, without inventing child observations/goals or barriers | ChromiumFrameTests.Frames_Chromium_BudgetsRetainCoverageWithoutPretendingChildWasRead; FrameSessionTests.IncompleteFrameCoverage_IsReportedWithoutAnAccessibilityBarrier |
| Valid maximum limits are accepted/recorded; invalid neighboring limits reject before launch | ChromiumFrameTests.FrameCapture_Chromium_MaximumValidBounds_AreReported; FrameSessionTests.FrameCapture_InvalidBounds_AreRejectedBeforeBrowserLaunch |
| Child replacement/removal invalidates only the affected document's cursor | ChromiumFrameTests.Frames_Chromium_ReplacementOrRemovalInvalidatesChildTextWithoutResettingMain; FrameSessionTests.ChildGenerationChange_InvalidatesTextEvenIfNodeIdsAndContentAreReused; ChildGenerationChange_PreservesMainDocumentTextCursor |
| Removed-frame history and document-specific live-region baselines remain explicit | FrameSessionTests.FrameDetachment_RetainsHistoricalDocumentAndExplicitEvent; ChildLiveChanges_ReplacementSeedsBaselineWithoutSuppressingMainUpdates |
| Historical v4/v5/v6 evidence cannot pass as current v7 | ReferenceValidationTests.Assessment_PreviousReaderProfile_RequiresFreshEvidence; actual historical worksheet assessment |

The final focused frame suite passed 28 cases: 18 browser-independent and ten real Chromium cases. A first compile attempt found a CLI local-name collision and ran no tests. The first executed frame suite had two failing immutable-array identity assertions; those were corrected to compare complete record sequences. A later formatting gate failed on two files after Linux tests passed; final formatting and same-source checks below supersede that attempt. Review also added the child live-baseline regression and retained the direct focus-owner observation. No failure was skipped or weakened into a pass.

## Final automated validation

Windows and Ubuntu 24.04.4 WSL each discovered/executed 190 tests: Passed: 190, Failed: 0, Skipped: 0. This comprises 154 browser-independent and 36 real Chromium cases. All 162 v6 identities remain present and discovery identities agree between platforms. TRX counters and individual result records account for every case.

Both platforms passed clean locked-dependency Release builds with zero warnings/errors, Directory.Build.targets architecture enforcement and format verification. SDK remains 10.0.402; central package versions are unchanged. All 53 source/.NET build configuration/fixture/lock files in the manifest match byte-for-byte across the Windows checkout and isolated Linux source copy. No project was added.

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental --artifacts-path artifacts/reader-v7/windows -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --artifacts-path artifacts/reader-v7/windows --no-build --no-restore --logger "trx;LogFileName=reader-v7-windows.trx" --results-directory artifacts/reader-v7
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet artifacts/reader-v7/windows/bin/HeadlessScreenReader.Cli/release/HeadlessScreenReader.Cli.dll --frames
```

The new --frames demonstration passed on both platforms: ChildFrameObserved, ChildFocusObserved, ChildActionVerified and IncompleteFrameCoverageReported were all true. Existing --text, --widgets and --observe-live demos also passed on both platforms. The CI workflow now includes the frame demo; no remote job was run.

Evidence under artifacts/reader-v7 includes final Windows/Linux TRX and discovery, test-summary.json, discovery-summary.json, source-manifest.json, linux-source-hashes.txt, Windows/Linux demo transcripts and validate-linux.sh. The Chromium sandbox remains enabled on both platforms. The raw protocol probe contains only its authored synthetic fixture data.

## Remaining scope and acceptance

Cross-target/out-of-process and arbitrary-site frames, popups, rendered/cross-run text flow, editable text operations, broader widget patterns and speech arbitration remain unsupported. Frame-tree/generation consistency does not make semantic reads atomic. Unsupported/hidden subtrees can contain further unobserved content; the reported inventory is not complete website coverage. Unavailable/limited frame observation never becomes an accessibility barrier.

No fresh v7 NVDA comparison, remote CI, deployment or customer acceptance was performed. Older NVDA observations remain historical. The fresh v7 worksheet remains unrun. Capture privacy, credential entry, external egress/target policy, human takeover, hosted orchestration and durable evidence gates remain open. The architecture status Page continues to mark Milestones 0 and 1 partial and hosted components planned.
