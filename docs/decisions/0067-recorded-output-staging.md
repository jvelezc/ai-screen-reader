# Retain complete original recording output before native cleanup

Status: implemented source; release acceptance is reported separately.

The isolated worker used to keep a completed recording bundle only in memory.
After native cleanup, the observation store retained its baseline while archive
publication proceeded. A worker stop or storage failure could therefore lose the
complete WebM, traversal and source frames despite retained command checkpoints.

For actual leased recording jobs, retain the exact validated output in a private
host `recorded-output` directory beside the existing command-progress journal.
Keep it outside that journal's immutable file inventory. Bind the stage header
to the original organization, scan, job, generation, actual worker and native
ownership intent. Derive every path from the operator's private runtime root and
validated IDs. Unrecorded jobs and executor calls without a leased producer keep
their existing behavior.

Write the full bundle first, then its seal. Each file uses a new pending file,
write-through, explicit flush, non-overwriting rename and exact readback. The
seal binds header SHA-256, bundle SHA-256 and byte length. Payloads retain the
existing 25 MiB maximum; metadata is bounded at 8 KiB and inventory at eight
known files. Readers reject unknown names, linked directories/files, scope
changes, malformed metadata, missing seals, changed bytes and oversized pending
writes. They revalidate the complete keyboard/passive bundle rather than trusting
the declared digest alone.

The lease authorizer runs before the bundle write, before sealing and after
readback. Authority loss prevents a successful executor return. Bytes already
retained at that boundary stay private and confer no publication authority.
There are no automatic retries or input dispatches in this journal.

Native cleanup remains independently awaited even after cancellation or failed
staging. A separate acknowledgement copies the matching actual native ownership
journal removal only after its confirmed removal and intent retirement. Caller
success flags cannot create this marker. An output seal, a native removal record,
private archive availability and fenced database completion remain separate facts.
Safe `WORKER_OUTPUT_*` failures remain visible in coordinator evidence diagnostics.

This first slice stages **complete output received from the child**. It does not
recover an unfinished video from a child killed before output completion. It
does not publish an expired attempt, recover missing older media, prove power-loss
durability or custody, clear global retention quotas, or authorize replay. The
snapshot always reports no completion receipt, no replay and overall NotAssessed.
The core API remains 1.11 with sixteen private MCP tools; no staged-media consumer
read is introduced here. Protected reconstruction, publication reconciliation,
partial live-media retention and actual storage/disk failures remain next.

See [named validation and release gates](../validation/recorded-output-staging-v1.md).
