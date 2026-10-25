# Private HTTP-backed read-only MCP

The core now offers `--hosted`, a stdio consumer of its independent HTTP
contract 1.3.0. It uses the API's owned Foundation session and configured isolated
worker instead of launching a second local browser. Default local MCP retains
its behavior. This adds no public MCP listener or plugin.

Process-scoped `HSR_MCP_API_ORIGIN`, `HSR_MCP_ORGANIZATION` and
`HSR_MCP_ACCESS_TOKEN` configure one HTTP loopback root origin, workspace UUID
and original application-user bearer. The origin defaults to
`http://localhost:6086/`, the existing SSH forward. Keep credentials out of
arguments, chat, source and committed client configuration. MCP treats the
bearer as opaque: the core authenticates the caller and checks current
membership and session subject on each operation. A management/dashboard login
is separate; no service-role key or synthesized identity substitutes for it.

The transport disables redirects, proxies, cookies and automatic retries.
Each request has a 20-second deadline covering headers and actual body reads,
with a 262,144-byte upstream bound. Status, JSON media type, no-store, response
shape and reader profile are checked. Safe errors retain only a bounded control
code, HTTP status and validated support UUID; response bodies and exception
text cannot disclose credentials.

Seven tools expose status, Foundation admission, snapshots, read-only commands,
timeline, graph trace and close. ScreenReader owns the shared pure browse policy
used by HTTP and this client. Hosted mode denies other fixtures, headed mode,
URLs, keyboard input, activation, goal predicates, editable entry and recordings.
It registers no local workspace. Domain and the browser adapter acquire no MCP
or HTTP dependency; HTTP remains independently usable.

One process owns one remote session and serializes operations. Caller
cancellation cannot detach a dispatched request. A lost admission may have
created a reader: without a valid receipt, new admission is blocked and cleanup
remains unconfirmed. A known session is replaced only after matching Closed
acknowledgement. Failed cleanup retains ownership for an explicit close retry.
Disposal attempts known cleanup; graceful shutdown returns exit 3 and
`HOSTED_READER_CLEANUP_UNCONFIRMED` when unconfirmed. Abrupt process death cannot
guarantee cleanup; the backend's independent two-minute lease and recovery
remain authoritative.

Snapshots capture at offset 0 without observationId. Reusing that ID pages the
same immutable snapshot, including offset 0 retries, after a fresh API access and
lifetime check. Commands invalidate pages and require the published document ID.
A changed document requires a new observation. Unconfirmed command/observation
responses block further capture/navigation; history and explicit cleanup remain
available for inspection/recovery.

MCP replies are bounded to 131,072 encoded JSON bytes. Snapshot, event and trace
pages shrink by actual size without splitting or skipping an entry. An oversized
first entry fails without inventing continuation. Trace preserves its schema,
transient retention, truncation reason, latest command and source/capture/result
links; ReaderCursor facts are checked against the step and wire mechanism.
Edges are not keyboard paths, verified objectives, durable archives or difficulty
scores. Oversized command errors preserve actual reported input uncertainty.

Real stdio/API/Chromium tests use explicitly synthetic original callers. These
do not establish real provider JWT/RLS, deployed tenant reader acceptance,
human/AT testing, Edge reachability or public onboarding. Those remain separate
gates. Lovable continues through existing-project Supabase Edge Functions to
the independently usable core; public MCP/plugin publication stays deferred.
See [validation](../validation/private-http-mcp-v1.md).

[Decision0052](0052-private-mcp-durable-scans.md) subsequently adds three
fixed-fixture durable scan/report tools using the existing API. Current hosted
discovery has ten tools. Live reader and durable job lifetimes remain separate;
transient reader trace is not durably archived by that extension.
