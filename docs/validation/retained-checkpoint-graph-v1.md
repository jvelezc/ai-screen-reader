# Retained checkpoint graph and byte retrieval

October 30, 2026. [Contract 0066](../decisions/0066-retained-checkpoint-graph-reads.md)
adds core API/private MCP retrieval of the original attempt's retained path and
full canonical checkpoint exports. No new input or automatic replay is added.

| Requirement | Evidence |
|---|---|
| Header, baseline, unresolved intent, unknown input and explicitly observed zero input remain distinct | `GraphPreservesHeaderBaselineUnresolvedIntentAndUnknownObservedInput`, `ObservedZeroInputRemainsDistinctFromMissingInputAndOverallGoalVerification` |
| Reconstruct every checkpoint byte without leaking native wrappers; distinguish source/export hashes and exact EOF | `CanonicalCheckpointPagesReconstructAllSemanticsAndKeepSourceAndExportHashesDistinct` |
| Reject invalid sequences, hashes and byte offsets without advancing | `InvalidCheckpointPagesNeverAdvanceOrReleaseBytes`, `McpRejectsInvalidPageArgumentsBeforeNetwork` |
| Keep every byte immediately below, exactly at and immediately above the page boundary | `CheckpointPageBoundaryPreservesEveryByteAndOnlyAdvancesWhenBytesRemain` |
| Reject empty workspace or scan IDs before provider/storage access | `EmptyGraphAndCheckpointScopeCannotReachProviderOrStorage` |
| Changed prefix requires fresh same-attempt retrieval, never replacement or replay | `AppendingTheSameAttemptInvalidatesOldPageIdentityWithoutReplayOrReplacement` |
| Keep absent/unstarted/unrelated graph states explicit | `MissingGraphStatesStayExplicitAndCannotYieldCheckpointBytes` |
| Recheck original caller and actual producer for graphs and bytes, including failed storage and cancellation | `EveryGraphAndByteReadRechecksAuthorityEvenAfterStorageFailure` |
| Exclude uncommitted writes and validate the entire prefix before releasing any checkpoint | `UncommittedWritesStayOutsideGraphAndCorruptionAfterRequestedCheckpointFailsClosed` |
| Reject invented topology, zero input and completion/replay authority | `GraphContractRejectsInventedTopologyInputAndAuthority`, `McpRejectsContradictoryGraphsWithoutRetryOrInput` |
| Reject altered page bytes, digests, scope and offsets | `McpRejectsAlteredCheckpointIdentityBytesAndPagination` |
| Preserve safe upstream denials without retry or private error text | `McpPreservesSafeUpstreamDenialsWithNoRetry` |
| Reject anonymous reads, query/worker selectors, missing fields and duplicate properties before evidence access | `HttpRejectsAuthoritySelectorsAndMalformedRequestsBeforeEvidence` |
| Actual Chromium interrupted/reached paths survive real HTTP and private stdio reconnect; complete exported events remain linked | `ActualChromiumPathAndFullCheckpointEventsSurviveHttpAndPrivateMcpReconnect` |
| Publish matching request/response schemas and private tool discovery | `RetainedGraphAndCheckpointShapesUseStringEnumsAndSeparateExportAndSourceHashes`, `PublishedContractIncludesEveryConsumerResponseAndAuthenticationBoundary`, `HostedStdioUsesRealApiChromiumPagesGraphAndConfirmedClose` |

Local Windows validation passes **2,568/2,568**, zero failed or skipped, including
87 new cases. The actual TRX reports confirm discovery and execution counts.
The focused 208-case run passed before two additional
HTTP/schema cases. Its earlier run caught a test expecting camelCase inside a
canonical worker export; the actual worker wire format is PascalCase. The test
was corrected without changing the export or weakening its complete-byte check.
The added schema regression also exposed root-relative pointers to repeated
models in the new OpenAPI component. Rebased component references pass that same
regression; existing component generation is unchanged. A strict nonincremental
Release build has zero warnings/errors and the twelve new/regression boundary,
missing-state and schema cases pass. Full format verification also passes.
The first full run retained its single schema failure (2,559 passed / 1 failed);
the final corrected-source run passes all2,568. Post-commit CI/deployment and
original-account delivery receipts are recorded separately.

Commands use pinned SDK10.0.402: `dotnet build HeadlessScreenReader.slnx -c
Release --no-restore --no-incremental -warnaserror`, `dotnet test
HeadlessScreenReader.slnx -c Release --no-build --no-restore`, and `dotnet format
HeadlessScreenReader.slnx --verify-no-changes --no-restore`.

The Chromium cases execute real input and retain complete source, result and
event semantics, using a synthetic provider identity. One case deliberately
stops after the second intent; another reaches the declared fixture goal while
the diagnostic response remains NotAssessed. This is separate from the actual
native process-kill/fenced-cleanup acceptance retained by earlier increments.

The new graph/page routes are core-first. Existing Edge v7 and notebook compact
progress remain compatible; consuming full graphs/checkpoints through them is a
subsequent gate. Original-account access, exact deployment, real membership loss,
interrupted video, offhost retention, storage/disk failure acceptance and real
assistive technology must not be inferred from synthetic-provider tests.
Public MCP/plugin publication remains deferred. Post-commit receipts are retained
in the [architecture Page](https://chatgpt.com/space/page_9a835b55f9808191a2accbc9900a3e25).
