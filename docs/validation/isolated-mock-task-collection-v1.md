# Isolated mock task collection validation

The actual local Podman Linux VM run collected all four immutable-image mock
scenarios. Each collection used13 control boundaries and30 parent authority
checks. Two revocations were denied, three completed/uncertain replay attempts
were denied, nine owned container names were confirmed absent and no intent remained.
The nine removal receipts include replay-denied names never allocated as containers.
Final execution-bound image: `sha256:2f4cc10c2dc44b32a5e3d7422d8c391de9adc509979bdb12c6630a515b3eb370`.
This was executed from Windows through the existing local Podman connection;
it is separate from GitHub Linux CI and the private VPS deployment.

| Requirement | Evidence |
| --- | --- |
| Exact four sources and distinct task/profile identity | `ClosedMockSourcesMatchExactBrowserBytesAndDoNotAcquirePublicOrShopIdentity`, `MockSourceCannotAcceptAnArbitraryWebsiteOrAnotherFixture`, `MockJobRejectsMissingScopeChangedSourceAndReplayGeneration` |
| Complete identity, source and fixed sequence before authority | `MockControlCannotAcknowledgeForeignOrOutOfOrderIntent`, `ChildRejectsMalformedOrOversizedStartBeforeAnyAuthorityRequest` |
| Durable uncertain grant before acknowledgement and fresh recheck | `ParentPersistsEachUncertainIntentAndRechecksAuthorityBeforeAcknowledging`, `AuthorityLossNeverAcknowledgesTheDeniedBoundary`, `FailedIntentPersistenceCannotReleaseAnyAcknowledgement` |
| No reuse of old permission, no partial result on EOF | `ChildRejectsMissingForeignOrChangedAckWithoutPublishingABundle`, `AnEarlierOpenAcknowledgementCannotAuthorizeTheNextKeyboardInput`, `EofAfterAPartialSequenceCannotBecomeACompletedCollection` |
| Real Chromium four-scenario originals, graphs and fresh attempts | `RealMockChildReturnsBoundOriginalsGraphsAndIndependentAttemptsAfterThirteenAuthorityGates`, `ParentCanValidateActualOriginalPackageWithoutConvertingUnknownGoalsIntoABarrier` |
| Denial before input or after prior input remains incomplete | `RealMockChildLosesAuthorityBeforeKeyboardOrPointerAndNeverPublishesPartialOriginals`, `DeniedAuthorityOrInvalidJobCannotAllocateANativeIntentOrInputFence` |
| Parent rejects changed/resealed source, graph, media, counts and unsupported claims | `ParentRejectsResealedInventedScopeChangedOriginalsAndInconsistentExperimentRecords` |
| An earlier package cannot acquire a different job/worker/execution through its transport receipt | `EarlierOriginalsCannotBeRelabeledByChangingOnlyTheTransportExecution` |
| Bounded ZIP paths/bytes and exact native identity | `ArchivePathsCannotEscapeOrIntroduceAmbiguousNames`, `CompressedOversizedEntryIsRejectedBeforeItsPayloadCanBeAllocated`, `MockContainerAcceptsOnlyTheExactImmutableImageCommandAndScope`, `MockContainerCannotAcquireAnotherProfileOrNativeIdentity` |
| Actual native execution, revocation, replay fence and owned absence | `--check-isolated-mock-tasks` with the published CLI and actual Podman image; retained safe digest/count receipt; CI `Test-ObservationWorker.ps1` adds `IsolatedMockTasksBindFourSourcesOriginalsAndInputAuthorityAndDenyReplayAfterLossWithOwnedRemoval` |

The final local focused suite passes85/85, failed/skipped0. The final pinned
nonincremental build passes with zero warnings/errors; solution format verifies.
The current-source Podman run retains four unchanged original ZIPs and verifies
their readback hashes. The final full Windows run passes3935/3935, failed/skipped0,
in10m58s; its parsed TRX confirms every discovered test executed. A pre-binding
snapshot passed3934 and is historical. Exact CI results remain a separate gate
before promotion.
No production deadline was relaxed and no browser fake replaces
the native Chromium or container paths. CI's orchestration job allowance is30
minutes: the earlier Windows run42330894939 took over23 minutes before the new
tests. Reader/capture/command/worker runtime deadlines are unchanged. Input mutation cases deliberately reseal
the manifest to test semantic checks beyond byte correspondence; they are not
claims of executing production-code mutations or measuring coverage.

The original markerless clips play in full, without verified event seek positions.
The comparison is between authored routes and a separate fresh pointer action;
it is not global unreachability, a legal decision or NVDA parity. No website-side
evidence assessment is promoted by this transport. No Supabase schema, hosted job,
HTTP/MCP task route, Edge Function, customer UI or real-site task was changed in
this gate. The original-account session was not used for these local checks.
