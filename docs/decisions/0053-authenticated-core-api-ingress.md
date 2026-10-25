# 0053: Authenticated HTTPS for the core API

The user approved authenticated HTTPS for the core API on October 25, 2026,
then selected VPS IP `89.117.17.81` until DNS is configured. This changes the
previous SSH-only API reachability gate. Public MCP, client onboarding and
official plugins remain deferred. Core deployment and worker administration
continue through the existing restricted SSH deployment.

Use a separately owned Caddy import for `https://89.117.17.81`. The existing
site files and main Caddyfile remain intact. An explicit Let's Encrypt ACME
issuer selects the `shortlived` profile for a publicly trusted IP certificate;
Caddy manages renewal. Neither an internal certificate nor disabled client
verification establishes hosted connectivity. See the
[issuer profile documentation](https://caddyserver.com/docs/caddyfile/directives/tls#issuers)
and [IP certificate availability](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability).

Publish only `/api/session`, `/api/workspace`, `/api/workspaces/*` and
`/openapi/v1.json`. Legacy portal assets, `/api/config`, `/internal/ready`
and unknown paths return 404 at this site. The core retains its existing
bounded request bodies, original-user authentication and workspace RLS.
No CORS or direct browser-to-core consumer path is introduced.

Caddy sends the fixed private upstream Host `localhost:6086` to the existing
loopback service on port8086. It preserves the original caller bearer and
request Origin; foreign browser origins still fail core policy. It does not
interpret forwarded headers as tenant authority. Worker and broker listeners
remain private. No retry is configured for state-changing API requests.

The small root operator validates a candidate before creating its one owned
import. Main/unrelated site fingerprints are fenced before and after validation
and reload. Conflicting files or symlinks are rejected. Failed activation
removes only matching owned bytes and reloads the previous configuration;
uncertain rollback remains an error. Installation or reload receipts explicitly
keep `TlsAccepted=false` until a separate external TLS/protocol check passes.

The host-level import survives normal exact-artifact CI backend promotions on
the same private port. Do not grant the restricted CI account arbitrary Caddy
write access. See [operator validation and deployment steps](../validation/core-api-ingress-v1.md).
The Lovable consumer remains UI -> existing-project Supabase Edge Functions ->
core API. Original-user Edge and visible UI acceptance are subsequent gates.
