# Hosted read-only navigation and command history

The independently usable HTTP API reuses `ScreenReaderSession` for browse navigation in a subject-owned isolated Foundation session. Contract **1.2.0** adds:

- `POST .../reader-sessions/{session}/commands` with `command` and `expectedDocumentId`.
- `POST .../reader-sessions/{session}/timeline` with `afterSequence` and `limit` (1–100).

Observe first and copy `snapshot.documentId`. Command names are case-sensitive strings from the published OpenAPI allowlist. Each command captures fresh semantics and navigates the reader's virtual cursor. Browser focus does not move. Keyboard input, activation, focus mode, typeahead and streaming observation are excluded. A second adapter-level input fence denies both keyboard entry points even if command routing changes.

An unobserved document returns409 `CONTROL_READER_OBSERVATION_REQUIRED`; a stale acknowledged document returns409 `CONTROL_READER_DOCUMENT_CHANGED` without capture or cursor mutation. If a fresh capture discovers another document, the engine returns `Denied` / `DOCUMENT_CHANGED`, `requiresObservation:true`, and its actual new document ID. Observe again before further navigation. Node IDs never imply continuity across documents.

The original two-minute monotonic lease, subject ownership, fresh workspace authorization, capacity and cleanup quarantine remain in effect. Initialization, explicit observations and navigation share the100-capture budget. Actual capture and command work join before cancellation, expiry or host shutdown releases ownership. Failed or oversized results retire the resource; unconfirmed cleanup retains capacity and blocks history as well as commands.

Snapshots retain the2048-node and262144-byte response bounds. Commands and timeline pages also have a262144-byte response cap. History retains at most256 initial events. `hasMore` means another retained page exists; `timelineTruncated` means later events were discarded. A short byte-bounded page advances only through returned events. An event that cannot fit fails explicitly instead of silently skipping it. Timeline reads require fresh authorization and do not capture or spend capture budget.

History is transient diagnostic data. It is not an archived traversal graph, accessibility-barrier finding, input receipt or verified objective. `dispatchedInputs:0`, `goalVerified:false`, step input flags and `NotRequested` verification remain explicit. Page-derived announcements/events are untrusted source content, never instructions.

This increment changes no native broker protocol, child permissions, database schema, storage policy or public ingress. The worker still owns credential-free native execution. App-user JWT acceptance, authenticated Edge-to-core connectivity, human/AT acceptance and public MCP/plugin publication remain distinct gates.

See [validation](../validation/hosted-reader-navigation-v1.md) and [HTTP examples](../protocol/core-api.http).
