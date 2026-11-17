# Saved search comparison history v1

HTTP1.26.0/private MCP44 adds a read-only route for reopening existing controlled search comparisons. It does not broaden customer-site task execution, admit a replacement job or make an accessibility assessment.

Each request requires the original caller's current session and workspace membership. The database returns only that caller's comparisons for the selected `search-working` or `search-keyboard-blocked` source. Pages contain at most ten metadata records, ordered by creation time descending and UUID ascending for equal timestamps. A continuation identifies the last returned job and must belong to the same caller, workspace and source. Denial remains distinct from an authorized empty history.

## Consumer contract

Authenticated fixed routes:

```text
GET /api/workspaces/{organization}/search-demonstrations/history/{sourceId}
GET /api/workspaces/{organization}/search-demonstrations/history/{sourceId}/{beforeJob}
```

Query parameters are rejected. The response schema is `hsr-search-task-history-v1`, with `organizationId`, `sourceId`, `items`, `nextBeforeJobId`, `websiteInputStarted:false` and `isAssessment:false`. The 128KiB response budget and existing per-job metadata validity apply. Foreign creator/source/organization, duplicated jobs or intents, reversed order, repeated cursor and inconsistent continuation are closed invalid responses. The private MCP tool is `search_demonstration_history(sourceId,beforeJobId?)`; it uses the protected HTTP route. Existing latest/exact status, original-file and admission contracts remain unchanged.

The separate Lovable consumer uses the existing authenticated Supabase Edge `search.history` adapter. Its explicit saved-comparison selector opens the existing request/job through the original result/file routes. Source/account transitions discard late history, old graph and media. No admission is repeated on open or reload. The consumer preserves sub-millisecond timestamp ordering and distinguishes same-second display labels. This remains sample evidence, separate from a customer's website review.

## Source verification

| Requirement | Evidence |
| --- | --- |
| Bounded, ordered, metadata-only history | `HistoryKeepsBoundedOrderedMetadataWithoutStartingInput` covers0/1/9/10 records and continuation; asserts exact project call and zero input calls. |
| Wrong scope, creator, duplicates, ordering, count and cursor deny | `HistoryRejectsForeignAmbiguousAndUnboundedMetadata`; `InvalidHistorySelectorsAreDeniedBeforeAnyProjectCall`; `HistoryDenialIsNotAnEmptyListOrAReplacementTest`. |
| Older adapters remain closed | `OlderProjectAdaptersRemainClosedWithoutImplementingHistoryOrStartingWork`. |
| Actual authenticated HTTP preserves original caller and safe diagnostics | `HttpHistoryUsesTheOriginalCallerAndDeniesQueriesAndLaterRevocation`; `BothHistoryRoutesDenyUnauthenticatedRequestsBeforeReadingMetadata`. |
| Strict RPC shape and narrow private MCP | `HistoryRpcPinsTheOriginalActorAndSerializesItsTypedWorkerShape`; `HistoryRpcCannotAcceptPromotedClaimsOrUnexpectedFields`; `PrivateHistoryRejectsPromotedAndMixedPagesWithoutLeakingTheOriginal`. |
| Real private stdio reaches HTTP and denies later authority without input | `RealStdioHistoryReopensMetadataAndDeniesLaterAuthorityWithoutAnyNewTest`; existing actual Chromium/stdio tool inventory checks retain all earlier lifecycle assertions. |
| Current SQL session/member/caller/source and keyset behavior | Disposable pgTAP `tests/database/search-history.sql`:24 new checks, including tied timestamps,10+3 pagination, foreign cursor, revocation, real session expiry and unchanged job/queue counts. Full database831/831, run5ab4f2b1c3064c3f9795b9137f27c081; owned container removed, hosted database unchanged by tests. |
| Windows canonical and formatting |4,519 discovered/executed/passed, zero failures/skips; repository-pinned SDK, Release and warnings-as-errors. `dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore` passes. |

The first canonical4519-case run retained4515 passes/four failures in older route/tool inventory expectations. All four inventories were updated to the actual additive surface; covering81/81 and the corrected full4519/4519 pass. Earlier failed receipts are retained. UI source verification, both CI platforms, exact deployment, additive hosted migration, Edge publication and original-account browser acceptance remain independent gates; their results must be appended after observation. No original is rewritten and no new test is admitted to replace an existing recording.
