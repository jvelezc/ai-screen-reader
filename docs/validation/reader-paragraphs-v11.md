# Reader v11 paragraph passages

Date: October 19, 2026. Profile: hsr-desktop-reader-v11. The selected increment from the [reader acceptance matrix](reader-acceptance-matrix.md) reads a logical paragraph across inline AX text runs, following bounded native tables in v10. See [decision 0012](../decisions/0012-observed-paragraph-passages.md).

The fixture splits “international” between emphasis and strong text. The reader returns “Read international text with linked words.” and a Passage containing each exact source fragment. It does not add separators, infer rendered lines, activate the link or change focus.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Exact joined text and immutable per-run document/node/frame/source/UTF-16 provenance | ParagraphSessionTests.ParagraphRead_JoinsExactRunsAndRetainsImmutableProvenance |
| Both directions, no wrap/input, descendant re-anchoring and independent single-run cursor | ParagraphSessionTests.ParagraphNavigation_BothDirectionsDoesNotWrapOrDispatch; ReadCurrentParagraph_ReanchorsDescendantAndClearsSingleRunCursor |
| Missing/cyclic/foreign ancestry, unobserved source text and overlapping text remain unknown | ParagraphSessionTests.ParagraphOwnership_UnobservedOrConflictingSourcesRemainUnknown; ParagraphOverlappingRuns_CannotDuplicateSourceText |
| Unsupported controls/images/blocks never produce partial text | ParagraphSessionTests.ParagraphBoundaries_UnsupportedContentCannotProducePartialPassage |
| Named links without text cannot be substituted or silently omitted | ParagraphSessionTests.ParagraphNamedLink_WithoutExposedTextCannotInventOrOmitItsLabel; ChromiumParagraphTests.Paragraph_Chromium_NamedEmptyLinkCannotBecomeInventedText |
| Before/at/after run and UTF-16 limits | ParagraphSessionTests.ParagraphLimits_RunCountIsBounded; ParagraphLimits_Utf16LengthIsBounded |
| Empty/outside anchors, focus mode, semantic goals and command budgets stay explicit | ParagraphSessionTests.ParagraphEmptyAndOutsideAnchor_DoNotBorrowOtherText; ParagraphCommands_FocusModeAndSemanticGoalsCannotDispatchOrVerify; ParagraphBudget_StopsAdditionalCaptureAndInput |
| Mutation, removal and new documents cannot reuse old passages | ParagraphSessionTests.ParagraphRefresh_UsesFreshTextAndNeverReturnsRemovedDocumentPassage |
| Real Chromium joins inline words, retains sources and leaves focus/page unchanged | ChromiumParagraphTests.Paragraph_Chromium_JoinsInlineWordAndPreservesSourcesWithoutInput |
| Real hidden/editable exclusion, Unicode, breaks and unsupported content | ChromiumParagraphTests.Paragraph_Chromium_HiddenEditableTextExcludedAndUnicodeBreaksPreserved; Paragraph_Chromium_ControlAndImageBoundariesDoNotProducePartialText |
| Real link descendant and child-document isolation, boundaries and external mutation/removal | ChromiumParagraphTests.Paragraph_Chromium_CurrentLinkReanchorsWithoutActivationAndPreviousLeavesContainer; Paragraph_Chromium_ChildDocumentPassageDoesNotMixParentSourcesOrWrap; Paragraph_Chromium_ExternalMutationAndRemovalCannotReuseOldPassage |
| Older reference evidence cannot count as v11 | ReferenceValidationTests.Assessment_PreviousReaderProfile_RequiresFreshEvidence |

The focused suite passes 40 cases: 32 core and eight real Chromium. Self-review added the named-empty-link regression to prevent accessibility labels from being invented as paragraph text.

## Validation

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --logger "trx;LogFileName=reader-v11-windows.trx" --results-directory artifacts/reader-v11
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --paragraphs
```

Final local Windows validation discovered and executed 377 cases: Passed: 377, Failed: 0, Skipped: 0. This comprises 310 core and 67 real Chromium cases; all 337 v10 test identities remain. Solution discovery, TRX counters and result records agree. Clean locked Release build passed with zero warnings/errors and architecture enforcement; formatting passed. Evidence is retained under artifacts/reader-v11.

The CLI passed all six flags: ParagraphTextJoined, SourceFragmentsPreserved, BoundariesPreserved, HiddenAndEditableExcluded, ExactUnicodeAndBreaksObserved and ReadOnlyBrowseObserved. CI exercises this demo and the full suite on Windows and Ubuntu after direct commits to main. Remote CI receipts are tracked in GitHub Actions and the architecture Page. Local WSL, fresh v11 assistive-technology checks and deployment were not performed.

Unsupported cases include rendered lines, words spanning runs in the existing word commands, cross-paragraph passages, nontext paragraph boundaries, unobserved named-link text, editable text and speech arbitration. No arbitrary-target navigation, credential entry, recording, hosted deployment or fresh v11 NVDA comparison is claimed. Milestones 0 and 1 remain partial.
