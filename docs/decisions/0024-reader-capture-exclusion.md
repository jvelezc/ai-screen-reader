# Reader-owned capture exclusion

Trusted local hosts can now coordinate exclusion through ScreenReaderSession.ExcludeCaptureAsync and ResumeCaptureAsync. The browser's optional ICaptureControl contract lives in Domain; the reader still has no Playwright dependency. This authority is not exposed through MCP and grants no human input.

Exclusion closes reader admission and reader-owned timeline retention before starting adapter exclusion. The adapter closes capture/key admission immediately while whole reader calls drain. Only after both drains and adapter stop acknowledgement succeed does the session clear its cached observation, cursor, mode, text and live-region state and return a reader-owned exclusion lease. Reader and adapter leases are separate; foreign, stale and consumed authority is rejected.

Every command, queued command, idle capture and timeline read respects the fence. An in-flight result arriving after closure is rejected, and its late events or announcement cannot enter the reader timeline. Cancelling a caller's wait does not cancel the underlying transition or acknowledge its completion. Idle polling delays are interrupted; an old idle iterator cannot silently restart after an exclusion/resume epoch.

Drain, adapter stop and fresh baseline acknowledgement have a five-second transition bound. Missing acknowledgement, invalid adapter state or a non-advancing baseline faults the reader closed with a payload-free code. Late completion cannot reopen it. Dispose after all calls finish and create a fresh session; this increment adds no automatic recovery.

The trusted owner must clear sensitive content before requesting resume. The adapter must return a fresh observation epoch. The reader preserves its prior epoch marker so the next ordinary capture records a baseline reset without inventing focus/widget/live transitions through the gap. Browser process and document context survive.

## Scope and remaining privacy gate

This fences operations and retention owned by one reader session and its adapter. It cannot revoke immutable results already issued to an external consumer or govern that consumer's files. The authored traversal wrapper and future hosted evidence pipeline do not yet participate in a scan-wide persistence transaction. Default sessions start no recorder; separately opted-in synthetic collector proofs remain separate. Credential entry, arbitrary recording, human takeover and hosted capture privacy remain disabled.

See [validation](../validation/reader-capture-exclusion-v1.md).

## Next: diagnostic feature provenance

Add a bounded diagnostic feature inventory to the controlled fixture report. Each candidate must retain its diagnostic source and exact document/node relationship, with an explicit distinction between reader-exposed names and diagnostic-only inference. Preserve unnamed controls as unnamed in the reader. Tie candidate objectives to immutable source observations and authored node/edge paths; do not infer successful access, unreasonable effort or an accessibility barrier from an inferred label or graph size. Validate successful, disabled, unnamed and stale-document cases before adding autonomous search or replay.
