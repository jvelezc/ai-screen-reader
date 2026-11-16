# Synthetic search input v1

November15,2026 source increment. This is a prerequisite for broader task profiles, separate from the accepted hosted service-link/reader workflow. Public search and general editable entry remain disabled.

| Requirement | Evidence |
| --- | --- |
| Exact fixed query, invalid inputs rejected before capture/input | `SearchTextRequiresTheDeclaredSyntheticQuery` |
| Same document/epoch/node and editable unprotected actual focus | `SearchKeyRequiresTheSameEditableUnprotectedSearchFocus` |
| Explicit capability and focus required | `SearchTextRequiresFocusAndAnExplicitBrowserCapability` |
| Ordered native characters and independent value verification | `SearchTextRetainsEveryCompletedCharacterAndVerifiesValueWithoutClaimingSubmission`; `ActualReaderSearchTypesTheFixedQueryAndVerifiesItsNativeValue` |
| Partial/focus-changed input stops without repair/retry | `SearchTextStopsAfterFocusChangesWithoutRetry`; `ActualSearchFocusChangeStopsRemainingCharacters` |
| Browser failure/cancellation retain actual input uncertainty | `SearchTextPreservesUnconfirmedNativeInputWithoutRetry`; `SearchTextCancellationPreservesTheCompletedCharacterWithoutRetry` |
| Completed keys do not establish value or task success | `SearchTextDoesNotInferValueOrGoalFromCompletedKeys` |
| Capture exclusion and stale epoch deny new input | `SearchTextHonorsCaptureExclusionAndStaleEpoch` |
| Native fields cannot be overwritten or changed into general text input | `NativeSearchCannotOverwriteExistingContentOrEnterUnsupportedFields` |
| Fresh keyboard/pointer results distinguish working/broken search | `ActualSearchFixturesKeepKeyboardAndPointerOutcomesDistinct` |
| Typed input remains an actual graph edge with thirteen ordered input pairs | `ActualSearchTraversalRetainsEveryCharacterOnTheTypedEdge`; `SyntheticSearchPlansCannotAcceptOtherActivationKeys` |
| Private MCP uses actual reader; public and unrelated fixtures denied | `McpSyntheticSearchUsesTheSameReaderAndDeclaredFixtureCapability`; `PublicReaderWrapperCannotForwardSyntheticSearchEntry`; `OtherFixturesCannotObtainSyntheticSearchEntry` |
| Old responses/base profile unchanged | `LegacyReaderStepsOmitTheSearchExtension` |
| Hosted read-only schema and command authority unchanged | `ReaderLifecycleHttpPayloadsMatchPublishedContractsAndTruthConstraints`; `ReadOnlyHttpDoesNotAdvertiseOrAuthorizeSyntheticSearchInput` |

Focused Windows run:46 discovered/passed, zero failed/skipped, including actual Chromium, stdio MCP, graph traces and hosted HTTP contracts. Native page listeners independently observe thirteen keydown/input/keyup events; setting a value without keyboard dispatch cannot satisfy that check. The fresh pointer comparison uses a real click to focus the search field, normal keyboard text entry, and an independent mouse activation.

The first native run denied entry because internal snapshots lacked the outward capture epoch; PSR-SEARCH-001 retains that failure and corrected binding. The first full run passed4,128/4,129, exposing the new optional field incorrectly required by the hosted schema; PSR-SEARCH-002 preserves the failure, closed-contract correction and successful covering regression. The query-only event observer setup and developer volume exhaustion/archive relocation are separately recorded as PSR-TEST-004 and PSR-BUILD-001. Canonical pinned-SDK Windows passes4,137/4,137, zero failed/skipped, with independent agreement between TRX definitions, results and counters. Format verification passes. Source Windows/Linux CI remains a separate release gate for this increment.

Existing fixture commands remain available. For a private local MCP session: open `MockSearchWorkingV1`, capture its document, Tab to Search courses, execute `EnterSearchText` with `key` exactly `sample course`, Tab to Search and Activate with an observed heading goal `Sample course result`. Fresh `MockSearchKeyboardBlockedV1` has the same entry but keyboard activation does not reveal the result. Only an actual separate pointer experiment establishes its mouse outcome.

Source CI43777238287 passes all nine jobs, with4,137 discovered/executed/passed per OS, zero failed/skipped; both uploaded TRX counters are independently checked. The source-only recording prerequisite now continues in [recorded synthetic search experiments](recorded-synthetic-search-v1.md). Hosted/public search remains a separate authority, isolation and customer acceptance gate.
