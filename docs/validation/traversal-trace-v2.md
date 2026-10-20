# Directed traversal trace v2 acceptance

October 20, 2026. Graph projection `execution-trace-v2`; immutable receipt schema `hsr-traversal-v1`; reader profile `hsr-desktop-reader-v13`.

| Requirement | Evidence |
| --- | --- |
| State nodes and action edges expose the actual command/key path | `TraceEdges_CarryExactCommandAndActualOrderedKeyEvents`; extended real Chromium `SuccessfulCommerceRoute_VerifiesActualKeyboardDestinationWithSourceEvidence` |
| Browse-only commands never acquire fictitious browser shortcuts | `BrowseOnlyTrace_RecordsReaderCommandWithoutInventingBrowserShortcut` |
| Repeated, incomplete, orphaned and missing key evidence stays explicit | `RepeatedKeyAttempts_MatchCompletionsInOrderAndPreserveUncertainLastInput`; `TruncatedEvidence_IsExplicitAndNeverTreatedAsZeroCostPath`; `OrphanCompletion_StaysObservedWithMissingAttemptMarkedIncomplete`; `MissingInputEvents_CannotHideKnownCompletedKeyboardCalls` |
| SVG nodes/arrows link to passive evidence, without executable page content | `Svg_HasDirectedStateNodesActionEdgesAndSafeEvidenceLinks`; `Svg_XmlEscapesNamesAndReplacesInvalidXmlControlsWithoutExecutingPageContent`; extended `Writer_RetainsJsonGraphAndReadableReportWithoutOverwritingOriginals` |
| Wrong destinations, missing observations and unresolved actions never fabricate a reached goal | `Svg_WrongDestinationHasOnlyObservedNodesAndDoesNotDrawGoalAsReached`; `Svg_NoObservationAndUnresolvedInputNeverCreateFictitiousResultNode` |
| Exposed labels preserve unnamed controls, cursor/focus separation and complete Unicode evidence | `NodeLabels_PreserveUnnamedSemanticsAndSeparateFocusFromVirtualCursor`; `Svg_LongUnicodeLabelsRemainWellFormedAndKeepFullEvidenceInTitles` |
| Invalid links and existing artifact collisions are rejected | `InvalidRouteNumber_CannotCreateMisleadingArtifactLinks`; `ExistingDiagram_CannotBeLinkedToNewUnrelatedEvidence` |
| Future feature-path search and reasonable-effort assessment remain explicit concepts | [Decision 0018](../decisions/0018-feature-path-traces.md); diagrams/report state that reasonable effort is not assessed |

Run the existing `--traversal` CLI. Every new batch includes `route-N.svg`, its equivalent Markdown table/transcript, and the JSON receipt/projected graph. Open its printed index for all four alternatives. Green marks an explicitly verified objective; amber marks an observed disabled control; neutral nodes show ordinary retained observations. Dashed arrows have no reliable result node. Color always has a matching text label.

The four real fixture diagrams were rendered in Chromium and visually inspected. Every evidence link resolved to its matching Markdown observation or event anchor; no text overflow was observed at the diagrams' native 800-pixel width. No browser input is dispatched by rendering or selecting report evidence. Fresh assistive-technology validation is not claimed.

This adds 15 tests and strengthens the existing real Chromium and persistence acceptance cases. It implements trace representation and rendering only. Bounded feature search, minimum-effort path discovery, state clustering, automated challenge judgments and a calibrated burden rubric remain future work. A no-match/budget outcome is not proof of impossibility, and graph size is not a difficulty score. Capture exclusion remains the next foundation gate.

Local Windows: 625 discovered/executed/passed, zero failed/skipped, including 94 real Chromium cases. All 610 prior identities are retained. The full nonincremental warnings-as-errors build, formatting verification and four-route CLI passed. Remote Windows/Linux CI is tracked separately in GitHub Actions. Local Linux, hosted deployment and fresh assistive-technology validation were not repeated for this increment.
