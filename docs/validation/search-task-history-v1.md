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

## Production and original-account acceptance — November16

Coredc934129526df1dbd648d4943654ddd3db4dbb24 passes all nine CI43986354431 jobs. Independently downloaded Windows/Linux TRX each4519/4519, native Linux85/85 and database831/831 are verified. Promotionee62bd1e9c1041c89b744a200a1faaf7 independently matches164 installed files/4,012,401,892bytes and immutable imagesha256:dfde22c62746961fceff3df1817420aa7339d8ac68ca15e84503afa4fe254c5e; all13 health components are Ready/Live, zero recovery attempts and zero input replay. The additive hosted migration is version20261117005411, normalized UTF-8/LF SHA256 57977c0ddaf2f410c5009e9991ff45622632240cdd54e8cdf73d6e4c5f141473. Its authenticated-only function grant denies anon and worker roles. Source migration naming is aligned to the API-assigned version without changing SQL.

Edge22 is ACTIVE and all three reviewed source files read back exactly, fingerprintdac6c29bc449f0f1cd624669f72e34fd5dcbf2a6cecbdf60f850e41a2e1e1501; existing caller authentication and platform settings are preserved. UI5e76ef5 passes all three CI43986567839 jobs with1211/1211 per OS. Publication43987342544 attempt1 correctly holds before the supporting core is ready and changes no customer files. Attempt2 passes the live contracts and verifies47 files/1,536,860bytes over HTTPS.

The same persisted application account reopens working jobc4fa95a5-5393-4c78-acd4-460dcfd84321 and blocked job028a2532-b30d-4bc3-b96e-cdb761d4af8a through their per-sample history, without using Run search comparison. The previously missing working path2 now verifies four states/three actions and four original reader texts; its matching native clip ends7.48seconds with errornull/readyState4. Blocked path4 verifies six states/five actions, explicitly labels the zero-key focus-mode transition, and its matching native clip ends7.24seconds without error. Earlier receipts retain the complete five-clip playback of each source and the other actual paths.

Switching samples removes old result/path/video before new data appears. Reload restores the same blocked source/result; switching back and activating Open by keyboard restores the original working job. At320px, document width305px and Tab reaches Open saved comparison; viewport reset. Independent scoped metadata still contains exactly two original jobs, one per original request, and the unchanged aggregate metadata fingerprint1a5613c1c84261efe52495a909a692e9. That MD5 is only a before/after metadata comparison; original-file custody remains the separately verified SHA256 protocol. No original was rewritten and no replacement task was admitted.

Browser facts and screenshots are retained under .git/testagent/search-history-20261116 and artifacts/browser-acceptance-20261116. PSR-UX-006 records a remaining presentation gap: the exact result restores after reload while the history selector still shows its placeholder. The result/source are not mixed; the current choice marker needs consumer correction. Public event/video seeking, external AT speech and broader customer-site tasks remain separate gates.
