# Tenant retained command diagnostics

October 28, 2026. [Contract 0065](../decisions/0065-tenant-retained-progress.md)
adds compact HTTP/private MCP retrieval without replay, new input or complete
recording authority.

| Requirement | Evidence |
|---|---|
| Reject invented counts, phases, commands and authority | `PublicProgressCannotAcceptInventedPhasesCountsCommandsOrAuthority` |
| Exclude native authority and raw AX; preserve unknown input | `LocalProjectionRetainsHeaderAndIntentWithoutLeakingNativeOwnershipOrSnapshots` |
| Separate absent, unstarted and unrelated progress | `MissingProgressStatesRemainDistinctAndDoNotReadUnstartedOrUnrelatedStorage`, `McpNonCollectedProgressNeverInventsAPrefixOrLosesAttemptIdentity` |
| Recheck authority and attempt identity after storage | `LostAuthorityChangedAttemptAndStorageFailureCannotReleaseOldProgress` |
| Select only the actual scoped producer through the original RLS bearer | `AttemptLookupSelectsOnlyTheActualScopedWorkerUsingOriginalRlsBearer` |
| Keep missing storage, corrupt paths and interrupted writes distinct | `MissingCorruptAndUncommittedLocalStorageRemainDistinct` |
| Reject empty scope before work | `EmptyScopeIdentifiersCannotReachProviderOrEvidence` |
| Validate compact scope/flags and use fresh read-only MCP requests | `McpProgressRejectsContradictoryScopeAndFlagsAndUsesFreshReadOnlyRequests` |
| Actual input followed by interrupted intent survives HTTP and stdio reconnect | `ActualChromiumInterruptedPrefixSurvivesHttpAndPrivateStdioReconnectWithoutBecomingACompletedRecording` |
| A reached command does not turn diagnostics into overall completion | `ActualCompletedRouteStillReturnsDiagnosticsOnlyWhenItsLastCommandReachedTheGoal` |
| Publish matching schemas, route metadata and private tool discovery | `CompactProgressAndMissingPrefixMatchThePublishedSchema`, `PublishedContractIncludesEveryConsumerResponseAndAuthenticationBoundary`, `HostedStdioUsesRealApiChromiumPagesGraphAndConfirmedClose` |

The focused suite passes 94/94 before the two additional schema cases. The first
mock response omitted Cache-Control:no-store and was correctly rejected before
payload validation. Its headers were corrected; the malformed-body cases then
exercise the intended validators. Failed receipts are retained separately.
An added regression then found that a regular file in place of the parent
runtime directory was classified as a missing prefix. It failed before the
parent-directory check and passed after the correction (five storage cases).
The final sequential local Windows run discovers, executes and passes
**2,481/2,481**, zero failed or skipped, including 47 new cases. Strict
nonincremental Release build has zero warnings/errors and full format
verification passes. The earlier 2,480-case full run is retained separately.

Commands use pinned SDK 10.0.402: `dotnet build HeadlessScreenReader.slnx -c
Release --no-restore --no-incremental -warnaserror`, `dotnet test
HeadlessScreenReader.slnx -c Release --no-build --no-restore`, and `dotnet
format HeadlessScreenReader.slnx --verify-no-changes --no-restore`.

The integration uses actual Chromium, retained files, loopback HTTP and real
private stdio processes across reconnect. Provider identity is synthetic.
It intentionally stops after one actual input and a second saved intent, leaving
no completed recording; the diagnostic prefix cannot supply missing video.
The existing independent native worker crash/replay fence remains distinct.

Windows/Linux CI, exact private deployment, original-account access to the new
endpoint, Edge/UI integration, recovered originals and real assistive technology
remain separately tracked gates. No database migration, package upgrade,
automatic restart or public MCP/plugin publication is included.
Post-commit delivery receipts are maintained in the
[architecture Page](https://chatgpt.com/space/page_9a835b55f9808191a2accbc9900a3e25).
