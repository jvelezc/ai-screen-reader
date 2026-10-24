# Hosted reader state and edge trace

Contract **1.3.0** adds `POST .../reader-sessions/{session}/trace` with strict integer `afterSequence` and `limit` (1–20). Read the observation, send a permitted browse command, then page its completed evidence. Existing command and timeline responses retain their shapes. See [HTTP examples](../protocol/core-api.http).

Each entry contains the actual fresh semantic snapshot, capture sequence, document/epoch, source and result reader states, and one directed `ReaderCursor` command edge. Stable session-scoped IDs connect the edge to both states. Cursor and browser focus remain separate. The complete `ReaderStep` preserves announcements, text-navigation details, outcome and verification. States are distinct observations, never merged into an inferred equivalence class.

The source and result states share one snapshot because a pure browse command sends no browser input. The source cursor is retained only if the fresh document, epoch and node still match; otherwise it is null. `refreshedFromStateId` connects the previous retained result to this new source by an **observation relationship**. It does not imply an input action or uninterrupted page state. `captureWindow` conservatively bounds capture in milliseconds from session admission; it is not calibrated recording timing. An explicit observation alone adds no command edge.

Retention is an immutable prefix: at most64 commands and8MiB of serialized entries. An entry must fit260096 bytes, leaving room in the262144-byte response. On the first command, total-byte or entry-size limit, later entries stay omitted; navigation can continue. `traceTruncated` and `truncationReason` (`CommandLimit`, `ByteLimit`, `EntryTooLarge`) expose the gap. `latestCommandSequence` continues advancing, while `retainedCommands` counts actual retained entries. `hasMore` refers only to retained pages. A false `hasMore` never proves an untruncated history.

Pages are bounded by count and serialized bytes, and never split a snapshot, its two states and its edge. Use `nextAfterSequence` to continue. Trace reads require the owning subject and fresh workspace authorization, spend no captures and respect lease expiry and cleanup quarantine. Confirmed retirement clears the trace. Requests with unknown fields, missing fields or string numbers are rejected. The existing100-capture session limit and two-minute lease remain unchanged.

This is **transient completed-command evidence**, not a durable attempt journal, delivery acknowledgement, keystroke path, shortest-path proof, accessibility-barrier finding or verified goal. Errors that retire the session are not archived here. The edge explicitly reports zero browser keystrokes, no possible input and `NotRequested` verification; the response reports zero dispatched inputs and no verified goal. Page-derived text remains untrusted source content.

The credential-free worker, private broker, read-only API filesystem, database, S3 policy and private SSH ingress are unchanged. Real application JWT/RLS acceptance, authenticated Edge-to-core connectivity, human/AT acceptance and public MCP/plugin publication remain distinct gates. Durable graph storage and objective-path analysis are later capabilities.

See [validation](../validation/hosted-reader-trace-v1.md).

The [text cursor ownership repair](0050-text-cursor-ownership.md) makes frame/node-document changes invalidate retained text offsets, even when node IDs and text are unchanged. Trace keeps that unknown/reset outcome alongside the fresh snapshot. It does not infer character-position continuity from matching node IDs.
