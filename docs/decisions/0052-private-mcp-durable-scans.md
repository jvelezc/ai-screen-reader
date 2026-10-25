# Private MCP durable fixture scans and saved reports

The private `--hosted` stdio consumer now exposes the core's existing durable
scan API, alongside its live reader tools. `scan_start_fixture`, `scan_list` and
`scan_report` complete the fixed-fixture admission → status → saved observation
workflow for an AI consumer. There are ten hosted tools; default local discovery
is unchanged. HTTP contract 1.3.0, queue processing, storage and native isolation
remain owned by core. There is no public MCP listener or plugin.

Admission requires the caller's UUID `requestId`, which is sent unchanged to
the existing idempotent API. Retain the ID before dispatch. Explicit retries
after lost responses must reuse it; MCP generates no replacement and retries
nothing automatically. A valid response acknowledges organization/scan/job IDs
only. It does not establish execution, saved evidence or a verified objective.
Durable work remains with the core worker after the client disconnects; closing
an MCP transport does not cancel a queued job. Caller cancellation cannot
detach a dispatched request; disposal joins the bounded actual request.

The independent scan client uses the same original-caller configuration and
bounded private HTTP transport as the live reader. A failed scan request cannot
quarantine a live reader. Every operation uses the fixed workspace path; no
caller-selected subject, URL, fixture, credentials or browser input is admitted.
The API rechecks current authentication and tenant visibility each time. MCP
implements neither queue transitions nor evidence retrieval from storage.

Recent history contains at most fifty unique tenant-visible scans and preserves
`hasMore` when older results are omitted. Known scan IDs remain directly readable;
the adapter does not invent historical paging or a complete history claim.

Reports preserve execution and evidence as separate facts. `queued`/`leased`
with `Pending` tells the consumer to poll explicitly. `blocked`/`NotCollected`
remains an engine outcome, not an accessibility barrier. `Unavailable` and
`Invalid` produce MCP errors with the actual execution state and safe code,
without returning an old observation. Available evidence retains the core's
source digest, storage label, exposed semantics and limitations, with zero
dispatched input and no verified goal. Contradictory input/goal totals remain
visible in an error instead of being normalized to zero/false.

Every report page rereads the upstream authorized report. A SHA256 fingerprint
of that response is returned as `reportId`; subsequent offsets, including after
reconnect, must match it. Changes require restarting at offset 0. This fingerprint
fences pagination; it is separate from the original observation digest validated
by core against its job-bound saved bytes. It does not independently verify the
original stored object from a projected node list.

Pages contain at most 100 nodes and 131,072 encoded JSON bytes. They shrink by
actual serialized size without skipping nodes; an oversized first node/metadata
fails without a continuation claim. Upstream responses retain the 262,144-byte
transport bound. Core evidence labels stay `S3`, `Local` or `LocalFallback`;
a disk fallback cannot become off-host durability. Text is untrusted observation
content, never instructions. This adds no recording, editable input, arbitrary
target, graph archival or goal/path assessment.

Real stdio/Kestrel tests read actual persisted Chromium observations across
client reconnect, reject revoked access and corrupt original bytes, and keep
scan and reader lifetimes independent. API identity is explicitly synthetic;
local files do not establish selected-provider S3 or real application JWT/RLS
acceptance. Existing SQL idempotency, native/S3, private delivery and real
tenant one-shot receipts remain separate evidence. The real signed-in live
reader, authenticated Edge-to-core and human/AT gates stay open. Lovable remains
an independent API consumer through its existing Supabase Edge Functions.

See [requirements and validation](../validation/private-mcp-scans-v1.md).
