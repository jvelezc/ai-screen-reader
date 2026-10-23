# 0047: Authenticated HTTP sessions use the trusted reader worker

Status: implemented; delivery and tenant acceptance are tracked in [validation](../validation/isolated-reader-http-v1.md).

The HTTP service authenticates each request with Supabase and checks current workspace visibility using that caller's bearer. It reserves a server-selected subject, organization and session before initialization. The two-minute lease includes startup. The initial native capture counts toward the existing 100-capture limit; subsequent HTTP observations always request a fresh capture. No URL, credential entry, input or recording operation is added.

The production HTTP process keeps its existing read-only systemd filesystem and `NoNewPrivileges` configuration. It does not execute Podman or write ownership journals. The existing trusted worker's `--poll` process hosts a narrow IPv4-loopback broker and owns the separate `.reader-runtime` journal. A protected process-lifetime descriptor binds an ephemeral port, immutable image, instance and random capability. Unix files are created with mode0600 before their contents are written. Authority never appears in HTTP responses, logs or retained CI artifacts.

The broker admits only Ready, Open, Observe and Retire. A bounded length-prefixed protocol validates caller scope, request/instance identity, fixed image, document continuity, observation bounds and zero input/goal claims. It accepts no executable, URL, environment dictionary, database credential, user JWT or storage credential. The isolated browser retains its existing closed environment, network-none policy, resource limits and fixed entrypoint.

The API and worker both reserve capacity before launch. The broker enforces at most two sessions and one per authenticated subject across API connections. The API joins actual initialization and cleanup after HTTP cancellation. Unknown cleanup retains its reservation; confirmed container absence can release capacity while the failed operation still returns an error. A missing native shutdown acknowledgement never becomes a successful Closed result.

Retirement binds organization/session and only reconciles matching failed-launch intents. Whole-journal recovery is restricted to startup and operator recovery, preserving sibling live sessions. A changed worker instance cannot silently reopen an old HTTP session. The worker must complete durable recovery before publishing its new descriptor. HTTP readiness depends on a live authenticated broker exchange.

Deployment stops the API before the worker and before either ownership scope is reconciled. Pending reader intents require the existing narrow `--recover-reader` mode and an empty inventory afterward; an unsupported older release or unknown cleanup holds closed. Empty reader inventories preserve legacy rollback compatibility. No systemd permissions, public listener or shared Caddy configuration are broadened.

`--check-isolated-reader-api` runs the packaged Kestrel API against a separate credential-free worker process and actual restricted Chromium. Its synthetic identities explicitly do not prove application JWT acceptance. `--host-reader` is a private operator mode for that process boundary, supplied only fixed operator configuration and drained on stdin EOF. Unknown termination retains durable ownership for recovery. The production worker uses the same broker through `--poll`.

Lovable remains a separate consumer through the existing project's Supabase Edge Functions. Private SSH deployment, signed-in tenant acceptance, authenticated Edge-to-core reachability, public MCP/plugins and human/AT acceptance remain independent gates.
