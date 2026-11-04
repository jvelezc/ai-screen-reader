# Customer HTTPS application publication

November 4, 2026. Prototype: **https://screenreader.privensic.com/**.
This receipt establishes publication and startup, not full product completion
or the original account's signed-in production journey.

## Source, CI and actual deployment

- UI source `5cf37d836c17ce508834401a48bce2a7d5d58c2e`.
  [UI CI41889858433](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/41889858433)
  passes all three jobs: 952/952 application tests across 43 files on each OS,
  strict typing, lint and default build. Deployment tests discover 9 per OS:
  Linux 9 pass; Windows 6 pass and 3 intentional Linux-only promotion skips.
  Local Windows application 952/952, typing, lint, both default/static builds
  and the same 9-case Windows result pass. Lint retains six existing warnings.
  Independent VPS Linux passes 9/9 deployment tests.
- [CI deployment41890575912](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/41890575912)
  publishes only the successful current main artifact through the dedicated
  restricted SSH account. Its retained receipt verifies 47 actual HTTPS files,
  **1,444,237 bytes**, commit, sizes, SHA-256, root HTML, security/cache headers
  and reserved-path denials. Manifest/build SHA-256 is
  `e21505b5bbc3d387082b5cf86b7fe531054b3776dd1555c32e63810b7691ee77`;
  root HTML SHA-256 is
  `92b4d72358bc58079ab2a02818de579aeffec67dd0dba28013e78dd326b31646`.
- Core ingress sourcec40d7c5b4e11ff1b3d08fdb417f5cd391f14a5df passes
  [all nine CI41882847817 jobs](https://github.com/jvelezc/ai-screen-reader/actions/runs/41882847817).
  The complete live Caddy configuration validates. Only its site configuration
  was reloaded; the privately deployed engine remains237d309/HTTP1.18/private
  MCP0.3/26 tools. Worker automatic promotion remains held.

This is a separate static application deployment. Caddy serves public assets;
all application API calls use existing Supabase Edge. No browser-to-core,
frontend-server API proxy, service credential or public MCP endpoint is added.
The restricted key cannot run an arbitrary SSH command or access core resources.
Old immutable assets and source/build-specific releases remain for active tabs
and operator rollback. A server Ready receipt alone is not browser acceptance.

## Auth and Edge configuration

Existing projectgpnijaxrgtwzwtrqsbcu has Edgehsr-core v14. All three deployed
source files exactly match reviewed source; bundle SHA-256 is
`dced1157715f24988571da772a6e0c2aef8581f3b473ef916d46c8da41a0e44c`.
The exact production origin is allowed alongside existing local origins.
Preflight authorization never grants a workspace or substitutes for getUser.

The normal signed-in project dashboard confirms Site URL
`https://screenreader.privensic.com/` and five exact callbacks:

- `http://localhost:6086` (preserved)
- `https://screenreader.privensic.com/`
- `http://localhost:6087/`
- `http://127.0.0.1:6087/`
- `http://localhost:3000/` (previous default preserved)

Signup asks to return to the current application origin. Confirmed email remains
required; no account/provider roles, credentials or confirmation policy change.
A localhost session does not transfer to a different origin. Normal persistent
SDK sign-in is preserved; browser tokens/passwords were not extracted.

## Startup and regression evidence

The live HTTPS document initially inherited the fixture's script-blocking CSP.
Separate default CSP handling and deferred header deletion preserve the fixture
policy while allowing only the customer's explicit same-origin application scripts.
The original two fixture routes retain their bodies and CSP. Missing assets,
`/api/*`, `/mcp*`, `/manifest.json` and unrecognized `/ci/*` routes do not fall
through to the SPA. Actual production Edge preflight accepts the exact origin
(204), rejects a lookalike (403), and denies an anonymous session request (401).
The live document is no-store; actual hashed assets are immutable.

Production startup also recovered from React418 when trying to hydrate a
workspace absent from the empty static shell. The static build now mounts that
workspace as a client application. The default Lovable build retains its normal
SSR hydration path; both wait for a complete document. Framework module
attributes are preserved. The actual production-mode preview and fresh published
Chrome tab render the sign-in form with **zero captured startup errors**.
This is agent-operated browser evidence, separate from human or real-AT acceptance.

| Requirement | Evidence |
| --- | --- |
| Wait for parser completion and start exactly once in either mode | `waits for a complete document and starts once (static=%s)` for true/false |
| Mount static content without hydration; retain default SSR behavior | The same test asserts the exact document target, one create/render or hydrate call and no opposite-mode calls; `starts immediately when ready (static=%s, state=%s)` covers interactive/complete in both modes |
| Preserve framework module markup | `test_preserves_the_framework_module_tag_for_client_hydration`, three original async-attribute forms and unchanged decoded attributes |
| Bind source and original bytes; reject unsafe archive members | `test_rejects_wrong_commit_and_changed_original_bytes`, `test_rejects_duplicate_traversal_absolute_and_unlisted_members` |
| Retain active data on collisions and rebuilds | `test_rejects_asset_collision_and_keeps_active_original`, `test_supports_a_rebuilt_same_commit_without_overwriting_an_earlier_build` |
| Prove actual public file/policy delivery | CI41890575912 verifies all47 HTTPS file hashes, root CSP/no-store,404 reserved paths and405 POST; original-account acceptance is explicitly separate |

## Still open

The public browser offers no saved original-account login. Its first HTTPS
sign-in has been requested through the normal application form; it is not
replaced by an administrator or fabricated session. URL admission, saved-result
reads, download, refresh/reconnect and scope isolation through that production
account remain pending. Earlier original-account local acceptance stays valid
only in its documented local/hosted-Edge/core scope.

Public URL reviews collect saved page information with **zero target inputs**,
no task attempt and no evidence-readiness verdict. Versioned mock scenarios,
bounded real website tasks, graph/video/repeat/comparator evidence and the
remaining intervention, authority, operations and human/AT gates are in the
[completion plan](../completion-plan.md). Final documentation and broad
refactoring follow product acceptance. Public MCP/plugins remain deferred.
