# Reader v6: exposed text navigation

Date: October 18, 2026. Scope: next reader capability, following the confirmed v5 landmark fix. Profile: hsr-desktop-reader-v6.

## Implemented behavior

ReadCurrentText and bidirectional character/word/line commands now navigate exact exposed AX text runs. Character units preserve grapheme clusters; ranges carry source provenance, document/node identity and UTF-16 offsets. Word segmentation is deterministic and retains internal apostrophes; punctuation/symbol graphemes are separate units. Lines are newline-delimited within a run, including blank/trailing lines, rather than rendered visual lines. Separately exposed inline runs remain separate boundaries.

NoMatch preserves the selection; no traversal wraps or dispatches input. Focus-mode commands and semantic goal expectations return Unsupported. A changed document/run or removed node produces Unknown with an explicit TextCursorReset and no stale range. That invalidation persists until explicit re-anchoring, such as DocumentStart followed by NextWord. Semantic movement and reader input clear text selection.

The Chromium adapter observes StaticText/LineBreak AX names without trimming their Text field. Existing semantic names retain their prior normalization. InlineTextBox duplicates, hidden content, editable descendants and form values do not enter text navigation. This is the existing bounded exclusion protocol, not proof of general sensitive-data capture safety.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Grapheme clusters, CRLF and exact UTF-16 offsets; bidirectional/no-wrap/no-input navigation | TextNavigationTests.Characters_GraphemesAndCrLf_PreserveUtf16OffsetsAndReverseWithoutWrapping |
| Unicode words, internal/boundary apostrophes and punctuation | TextNavigationTests.Words_UnicodeApostrophesAndPunctuation_HaveDeterministicBoundaries; Words_BoundaryApostrophes_AreSeparateFromInternalApostrophes |
| CR/LF/CRLF, blank and trailing logical lines remain distinct from NoMatch | TextNavigationTests.Lines_CrLfBlankAndTrailingLines_AreDistinctFromNoMatch |
| Cross-run traversal does not duplicate inline text or infer text from control names | TextNavigationTests.TextRuns_CrossingBoundaries_SkipsDuplicateInlineAndControlNames |
| Directional unset cursors, semantic anchors, exact whitespace and changed granularity | TextNavigationTests.TextNavigation_UnsetCursor_StartsAtDirectionalBoundary; SemanticAnchor_RequiresObservedDescendantAndDoesNotReadNames; SemanticMovement_ClearsTextSelectionAndReadCurrentPreservesExactWhitespace; TextNavigation_GranularityChange_UsesSelectedStartOffset |
| Missing/cyclic ancestry does not invent containment | TextNavigationTests.SemanticAnchor_BrokenAncestry_DoesNotInventText |
| Empty/absent text and whitespace-only word navigation remain NoMatch; malformed UTF-16 is preserved without crashing | TextNavigationTests.TextNavigation_NoWords_ReturnsNoMatchWithoutInventedText; TextNavigation_IsolatedSurrogate_IsPreservedWithoutCrashing |
| Document/text/removal invalidation persists until re-anchoring | TextNavigationTests.TextCursor_ChangedObservation_RequiresReanchorBeforeReadingFreshText; ChromiumTextTests.TextCursor_ChromiumChangedPassage_ReportsInvalidationBeforeContinuing |
| Browse-only text commands share budgets and reject semantic goal expectations without input | TextNavigationTests.TextNavigation_FocusMode_IsUnsupportedWithoutInput; TextNavigation_SemanticExpectationAndBudget_DoNotDispatchInput |
| Real Chromium preserves exact source text while excluding hidden/editable content | ChromiumTextTests.ExposedText_Chromium_PreservesWhitespaceAndExcludesEditableOrHiddenContents |
| Actual character/line reading preserves browser focus and dispatches no input | ChromiumTextTests.TextNavigation_Chromium_ReadsGraphemesAndLinesWithoutInputOrFocusMovement |
| Actual document word traversal is exact and duplicate-free | ChromiumTextTests.TextNavigation_Chromium_TraversesExposedWordsOnceAndExcludesEditableText |
| Older v4/v5 profiles remain historical | ReferenceValidationTests.Assessment_PreviousReaderProfile_RequiresFreshEvidence; actual v5 worksheet assessment |

The focused suite executed 31 cases successfully: 27 browser-independent cases and four Chromium cases. Final solution runs below also include the final lone-CR line-boundary assertion and all existing reader regression tests. The suite is based on behavior partitions, not a coverage percentage or mutation-tool claim.

## Final local checks

Windows and Ubuntu 24.04.4 WSL each discovered/executed 162 tests: Passed: 162, Failed: 0, Skipped: 0. This comprises 136 browser-independent and 26 real Chromium cases. All 131 v5 test identities remain present, and discovery identities agree between platforms. TRX counters and individual result records each account for all 162 passes. Locked clean Release builds and Directory.Build.targets architecture enforcement passed with zero warnings/errors. Formatting verification passed on both platforms. SDK remains pinned to 10.0.402 and packages are unchanged.

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental --artifacts-path artifacts/reader-v6/windows -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --artifacts-path artifacts/reader-v6/windows --no-build --no-restore --logger "trx;LogFileName=reader-v6-windows.trx" --results-directory artifacts/reader-v6
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet artifacts/reader-v6/windows/bin/HeadlessScreenReader.Cli/release/HeadlessScreenReader.Cli.dll --text
```

Both platforms' text demos returned TextNavigationObserved=true and ReaderDispatchedNoInput=true. The widgets and idle-observation demos also passed on both platforms. Linux used an isolated source copy with the existing user-local SDK/browser dependencies and sandbox-enabled Chromium. All 48 source/configuration/fixture/lock files match the Windows checkout byte-for-byte.

Evidence is under artifacts/reader-v6: reader-v6-windows.trx, reader-v6-linux.trx, test-summary.json, discovery-summary.json, source-manifest.json, linux-source-hashes.txt, discovery listings, Windows/Linux text/widget/idle JSON transcripts, linux-workspace.txt and validate-linux.sh. The original architecture plan and prior comparison worksheets are preserved.

## Assistive technology and remaining scope

No fresh NVDA run was performed for v6. The v5 targeted reading observations remain historical: the current assessor rejects that worksheet with HSR_PROFILE_MISMATCH and REFERENCE_ENVIRONMENT_INCOMPLETE, exit 3. A fresh v6 template leaves all seven cases NotRun. Automated Unicode/text tests do not establish NVDA speech, visual-line behavior or general assistive-technology equivalence.

Still unsupported: child frames; rendered soft-line/paragraph text flow and words spanning multiple AX runs; language-sensitive word segmentation; editable text/caret/selection operations; broader widget patterns; lossless live events and speech arbitration. Capture privacy, credential entry, arbitrary-site/egress isolation and hosting/MCP remain deferred under the reader-first scope. Remote CI was not run and nothing was deployed.
