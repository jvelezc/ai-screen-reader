# Private read-only viewer feasibility

The first private viewer transport is a bounded local experiment for header-capable clients. It uses a process-provisioned identity, one exact owned scan/session, a short-lived bootstrap exchanged once, independently expiring read authority, actual revocation/drain and the owned capture fence. It serves acknowledged semantic observations. It enables no pixels, browser UI, VNC, human input, public listener or Supabase identity-provider integration.

~~~powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --private-viewer
~~~

The fixed demonstration opens one trusted Chromium fixture, starts a loopback-only Kestrel server on an ephemeral port, exercises the protocol with an actual HTTP client, then closes both. It prints a sanitized receipt/report path, never the identity credential, bootstrap or read secret. No service remains running for another AI or user to connect to. The existing local MCP reader remains a separate usable adapter.

## Identity, scope and authority

The trusted host provisions an unpredictable 256-bit process credential for one subject. The proof authenticator derives subject identity from the Authorization header; request bodies cannot claim a subject or tenant. A broker is bound to that subject and one canonical scan/session. Its composed owner must match both identifiers. This is an authentication boundary for the experiment, not a hosted identity provider or tenant membership check.

A bootstrap lives 1–60 seconds and can be exchanged once, including under concurrent exchange attempts. Invalid identity/scope or a malformed request cannot consume a valid bootstrap. An exchanged connection has a separate 1–120 second monotonic lifetime and a new secret. At most 16 bootstraps are issued per broker; consumed/expired grants do not reset that budget. Every read and revoke checks authenticated identity, exact scan/session, connection ID and secret. At most four actual reads are active, with one per connection. Wall-clock display timestamps never authorize a request.

Revoke/expiry closes read admission before cancelling delivery and draining actual source and transport tasks. Pending sources cannot start a new publication after that point. Already admitted delivery may settle before the stop acknowledgement; bytes already received cannot be revoked. A cancelled caller wait cannot release actual work. Even a source that forgets to await its sink cannot make the broker forget a pending send. Failed drain stays closed after late completion; escaped callbacks cannot publish after source settlement. No viewing grant can execute a reader command, type, click or acquire input ownership.

## A retained view, with actual capture fencing

OwnedScanSession caches only an acknowledged result or persisted resumed baseline. The view is explicitly LastPersistedObservation, with source epoch, ordinal and cumulative simulated-assistance provenance. It is not a fresh capture or a new goal claim. The viewer causes no AX request or native input. Unknown and unsupported observed semantics remain unchanged.

Actual delivery is a tracked, serialized owned operation. Its phase/epoch check and delivery start share the same admission lock; exclusion clears the cache and waits for actual publication tasks before acknowledging the gap. Queued, excluded, resuming, stale or missing views cannot publish. A resumed baseline becomes available only after its actual persistence acknowledgement and the new automation grant. The same Chromium process/context/document remains alive.

## HTTP boundary

CLI composition uses the SDK-pinned Microsoft.AspNetCore.App shared framework; Domain and Exploration remain hosting-framework-free. Kestrel listens only on IPv4 loopback with an ephemeral port and explicit bounded connection/header/body settings. Inherited URL/endpoint configuration is cleared. Every request requires the process identity. Host mismatch, Origin and Cookie headers are rejected; this first proof supports explicit header clients, not a browser login flow. No CORS, cookies, public listener, raw browser endpoint, arbitrary resource path or input route is exposed. Credentials are never accepted in query strings.

POST /viewer/exchange exchanges the bootstrap; GET /viewer/{scanId}/{sessionId}/observation?epoch=... requires read credentials in headers; POST /viewer/{scanId}/{sessionId}/revoke acknowledges actual drain. Errors use sanitized Problem Details. Bodies are bounded at 4 KiB, responses at 4 MiB; duplicate/unknown JSON properties are rejected. Responses disable caching, sniffing and framing. Request/body logging providers are absent. An authenticated fixed /openapi/v1.json documents the narrow protocol without introducing a generator package. [Developer request examples](../../src/HeadlessScreenReader.Cli/private-viewer.http) contain placeholders only.

See [validation](../validation/private-viewer-v1.md) for exact cases, Windows/Ubuntu CI and distinct remaining gates. Local append-only files remain process-acknowledged evidence, not crash-durable hosted storage.

## Next: private pixel viewer feasibility

This next-step contract is now implemented in the [private pixel relay](0033-private-pixel-viewer.md). Its [current next increment](0033-private-pixel-viewer.md#next-accessible-private-viewing-client) is an accessible private viewing client; human control remains separate. The original contract follows for traceability.

Prove a private, read-only pixel transport on the existing headed Linux/Xvfb session. Reject keyboard, pointer, clipboard and browser-control messages at the actual relay boundary, keep raw VNC/CDP endpoints private, and bind viewing to scoped expiring/revocable authority. Test connection shutdown and owned cleanup with actual pixels before introducing any human input lease. Accessible controls, supported remote authentication, actual human/AT acceptance, hosted membership and real credential entry remain separate gates. Public MCP hosting and official plugins stay deferred.
