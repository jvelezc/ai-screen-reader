# Caller-owned workspace catalogue v1

November 2, 2026. **Core HTTP1.17 privately delivered; consumer selection and
original-account catalogue acceptance remain open.** Core d604eca, UI adapter
e196c47 and Edge12 implement the protocol. Earlier publication-diagnostics
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
individual execution IDs remain distinct.

All nine core CI jobs pass on the first attempt in run41412138378: Windows and
Ubuntu each3309; database509, native71 and operations172. Private deployment
41414140624 is Ready for d604eca. All151 installed files match the independently
read CI archive; manifest SHA256
`21d57f50df756763c4d41011d6260f3234057963480cbc0d6781d450354bddca`
and native image
`sha256:5533c5f405f7e3d6426f1ee55b9e56e5f60fb26e015d7656c3e1abd3bd7031b2`
agree. Both services are healthy/unfrozen with Restart=no and native0.

Live HTTPS verification caught a deployment gap: the existing wildcard forwarded
catalogue child paths but omitted the new exact root, producing404 before core
authentication. The two new static route-contract tests reproduce that omission
(one failed, one passed), then both pass with only `/api/workspaces` added to the
explicit route list. All174 local Linux operations checks pass in a separate
Python container, with0 failed/skipped and no hosted deployment performed by the
unit suite. Caddy's existing compare-and-replace operator validates/reloads the
owned site with rollback; independent readback confirms its new SHA256
`eb66c4e4515bcfcfb88c2b8c73857c943453dcacb5c21b51f6b2e98c954bed34`
and unchanged unrelated configuration. Actual trusted HTTPS now returns401 on
both catalogue routes,400 on a forbidden limit query, and404 on the private
portal/config/MCP/readiness paths. Static routing tests do not attest TLS or RLS.

The separate consumer adapter adds27 cases and passes all800 on both CI platforms
in run41416153454, plus local frozen-dependency/type/lint/build checks. Existing
Edge11's three files are checked against the previous canonical source before
replacement; active Edge12's three files match e196c47 exactly on readback.
Original-caller manual authentication and the fixed upstream remain unchanged.
The actual chooser and original-account acceptance are still separate gates.

| Requirement | Evidence |
| --- | --- |
| Original-bearer RLS and explicit bounded continuation | `ProviderCatalogUsesOriginalRlsBearerFixedBoundAndExplicitContinuation`, `ActualHttpCatalogPagesByPathWithoutCreatingOrRescopingWorkspace` |
| No partial/unordered/corrupt directory | `MalformedProviderCatalogCannotBecomeVisibleWorkspaceMetadata`, `UnconfirmedOrderBoundOrHiddenLookaheadCannotAuthorizePage`, `ContinuationCannotRepeatOrMoveBeforeItsOriginalCursor` |
| Fresh denial, safe errors and no actor/query injection | `ProviderDenialOrOutageCannotBecomeEmptyCatalogOrRawDisclosure`, `CatalogPreservesGlobalQueryDenialBeforeIdentityLookup`, `ProviderRevocationIsRecheckedBeforeNextPageWithoutCachedNames` |
| Actual HTTP contract and contradictory-schema rejection | `ActualHttpWorkspaceCatalogMatchesPublishedPageAndCursorContract`, `PublishedWorkspacePageSchemaRejectsImpossibleContinuationOrBound` |
| Actual database scope and revocation between pages |8 new pgTAP cases, including `CatalogContinuationRetainsOriginalCallerRls`, `ForeignCursorCannotGrantForeignMembership`, `NextCatalogPageCannotRestoreRevokedMembership` |
| HTTPS root coverage and private-route preservation | `test_caller_workspace_catalog_paths_are_forwarded_to_core`, `test_private_portal_and_mcp_paths_are_not_forwarded`; actual post-replacement401/400/404 checks |

## Next

Connect notebook workspace selection to the delivered core and existing
Supabase Edge adapter, then verify it with the original account. Keep credentials
server-side, original caller scope and result/media clearing. Actual original-user
provider expiry/revocation across observation, graph, original paging and playback
remains the subsequent acceptance gate; local provider fixtures cannot replace it.

Product completion is followed by complete documentation, then the dedicated
best-practice/complexity refactoring phase requested by the user.
