# Tenant-owned read-only reader sessions

The core HTTP API now owns transient browser sessions independently of the local
stdio MCP process. HTTP contract version 1.1.0 adds four operations:

| Method | Workspace-relative path | Result |
| --- | --- | --- |
| POST | `/reader-sessions` with `{}` | 201, owned session metadata and retrievable Location |
| GET | `/reader-sessions/{session}` | Current owned session metadata |
| POST | `/reader-sessions/{session}/observations` with `{}` | Fresh exposed source semantics |
| DELETE | `/reader-sessions/{session}` | Closed only after actual browser cleanup |

All paths begin `/api/workspaces/{organization}`. Every operation revalidates the
application bearer with Supabase Auth through the existing host middleware,
rechecks current workspace RLS using that original bearer, and binds the resource
to both subject and organization. Session IDs are identifiers, not bearer grants.
Another workspace member cannot read or close someone else's session. The registry
stores IDs and browser resources, never user tokens. Unauthorized and missing
resources use safe indistinguishable 404 responses.

The host opens only its bundled `Foundation` fixture. Requests cannot choose URLs,
HTML, fixture files, keys, credentials or recordings. Observations preserve the
Domain snapshot, including nullable source facts and frame/document identity;
page content is untrusted data. No input is dispatched, no goal is verified, and
these transient observations are not archived scan evidence. The existing
`synthetic-shop` durable scan/report surface remains separate.

Limits are two browsers per process, one per subject across workspaces, two
minutes of monotonic lifetime, 100 observations, 2048 nodes and 262144 serialized
response bytes. There is no implicit replacement. The host serializes admission,
capture and close. A one-second maintenance timer closes expired idle resources;
every operation also checks the exact expiry boundary. Expired sessions cannot
renew themselves by polling. A restarted host loses these transient sessions.

After native work starts, caller cancellation joins capture/launch and cleanup
before releasing ownership. Oversized, failed, expired or canceled observations
are discarded. Native cleanup failures return
`CONTROL_READER_CLOSE_UNCONFIRMED` and quarantine the slot; no successful close or
capacity release is fabricated. An authorized explicit close can retry cleanup.
Shutdown attempts cleanup for every owned browser and reports failures.

This is the first tenant-owned session capability, not complete HTTP/MCP parity.
The existing stdio MCP workspace and its input policy are unchanged. Next, adapt
the shared reader engine's document-fenced navigation and timeline to owned HTTP
sessions, keeping attempted input, observed state and verified goals separate.
Do not copy a process-global MCP workspace into a multi-user host.

Validation includes deterministic lifecycle/authorization/cancellation tests,
real Kestrel authentication and contract checks, and a real Chromium HTTP
open/read/observe/close route. CI and deployed acceptance are distinct. Private SSH
hosting remains active; this change does not expose a public listener or make the
VPS reachable from hosted Supabase Edge Functions. The Lovable UI remains a
consumer through project `gpnijaxrgtwzwtrqsbcu` Edge Functions. App-user JWT,
hosted fixture, private S3, human/AT and deployment acceptance remain explicit gates.
