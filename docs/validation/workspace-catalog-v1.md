# Caller-owned workspace catalogue v1

November 2, 2026. **Source in progress; not delivered.** Current private delivery
remains core38ba353/HTTP1.16, UIe1d1571 and existing Edge11. Publication-diagnostics
acceptance and the original account's retained recordings remain unchanged.

## Purpose and boundary

Let independent API consumers discover workspaces visible to their current
application identity. This enables isolated actual access-change tests while the
original account's working workspace and saved sign-in remain available. A list
item or cursor does not grant access, create a workspace, restore membership,
admit a scan, send website input or verify an objective.

HTTP1.17 adds GET`/api/workspaces` and
GET`/api/workspaces/page/{after:guid}`. Each request authenticates again and uses
the original bearer against the existing organization RLS, selecting only id/name.
The fixed51-row ordered read yields at most50 visible workspaces and an explicit
last-visible nextCursor only when another row is observed. Empty/exactly-full
last pages keep null continuation. Invalid, duplicate, reversed, excessive or
pre-cursor data stays unconfirmed; no partial page is presented as complete.

Global protected-route query denial remains unchanged, including bearer/actor/
limit query attempts. Provider denials/outages retain safe distinct failures
and cannot become an empty catalogue or reveal raw messages. No database schema
or grant changes are needed. Existing private MCP stays tenant-configured with
21 tools; discovery does not broaden that configured scope.

## Verification so far

Two behavioral red cases prove the absent adapter/HTTP route; anonymous denial
already passes. The first3 discovery cases then pass. The expanded focused run
passes143 with0 failed/skipped, including original-bearer/profile/query bounds,
actual HTTP paging, malformed data, invalid lookahead/order/cursors, safe failures,
global query denial and provider rechecks before continuation. Six schema cases
include actual HTTP payloads plus independently mutated continuation/bound/UUID
contradictions. The bounded test evaluator initially lacks minItems and misses
the empty-continuation contradiction; adding that strict array keyword makes all
six pass without weakening a production constraint.

The pinned Linux database harness passes509 discovered/executed/passed,
failed/skipped/unconfirmed0, including8 new actual RLS/cursor/multiple-membership/
revocation cases. It runs as an independent PowerShell process and exits0;
run.json independently confirms Ready/completed and owned-container removal.
The strict no-incremental Release build has0 warnings/errors and format
verification passes. Full Windows discovers/executes/passes3,309, failed/skipped0,
including39 new cases. Fresh TRX counters, unique execution IDs and the exact
focused/full case multiset are independently verified. Five long JSON arguments
share a truncated display name, so39 executions have35 displayed names; the
individual execution IDs remain distinct. Both CI platforms, exact private
delivery and actual application-user/consumer acceptance remain separate gates.

| Requirement | Evidence |
| --- | --- |
| Original-bearer RLS and explicit bounded continuation | `ProviderCatalogUsesOriginalRlsBearerFixedBoundAndExplicitContinuation`, `ActualHttpCatalogPagesByPathWithoutCreatingOrRescopingWorkspace` |
| No partial/unordered/corrupt directory | `MalformedProviderCatalogCannotBecomeVisibleWorkspaceMetadata`, `UnconfirmedOrderBoundOrHiddenLookaheadCannotAuthorizePage`, `ContinuationCannotRepeatOrMoveBeforeItsOriginalCursor` |
| Fresh denial, safe errors and no actor/query injection | `ProviderDenialOrOutageCannotBecomeEmptyCatalogOrRawDisclosure`, `CatalogPreservesGlobalQueryDenialBeforeIdentityLookup`, `ProviderRevocationIsRecheckedBeforeNextPageWithoutCachedNames` |
| Actual HTTP contract and contradictory-schema rejection | `ActualHttpWorkspaceCatalogMatchesPublishedPageAndCursorContract`, `PublishedWorkspacePageSchemaRejectsImpossibleContinuationOrBound` |
| Actual database scope and revocation between pages |8 new pgTAP cases, including `CatalogContinuationRetainsOriginalCallerRls`, `ForeignCursorCannotGrantForeignMembership`, `NextCatalogPageCannotRestoreRevokedMembership` |

## Next

Complete local/CI/delivery checks before consuming the new API through the
existing Supabase Edge adapter and notebook workspace selection. Keep credentials
server-side, original caller scope and result/media clearing. Actual original-user
provider expiry/revocation across observation, graph, original paging and playback
remains the subsequent acceptance gate; local provider fixtures cannot replace it.

Product completion is followed by complete documentation, then the dedicated
best-practice/complexity refactoring phase requested by the user.
