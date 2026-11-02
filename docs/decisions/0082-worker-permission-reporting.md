# 0082: Report worker permission separately from execution

The actual [hosted lease-expiry experiment](../validation/hosted-lease-expiry-v1.md)
stopped the worker without replay, but exposed a consumer gap: the durable job
remains `leased` until explicit fenced recovery. A `Pending` report alone made
MCP clients wait for a worker that had lost permission.

HTTP1.15 adds an optional `workerPermission` to reports. Every current `leased`
report requires the snapshot; queued and terminal reports carry null/omitted
permission. The shared Domain contract contains only organization, scan, job,
generation, schema, status and database `checkedAt`/`expiresAt` timestamps. It
cannot dispatch, renew, replay or complete a job. `Active` means expiry is after
the snapshot's database clock; `Expired` means at or before; `Missing` has no
matching lease/expiry. The exact equality boundary is expired.

The new `hsr.read_scan_job_permission` RPC checks and locks the original caller's
current membership through its read, joins the actual job/generation/attempt,
and projects seven safe metadata fields. It does not expose a token, token hash
or worker credential. Its explicit security-definer scope is necessary because
callers cannot read the private lease table. Search path is empty; PUBLIC/anon
cannot execute; only authenticated callers receive this narrowly scoped read.
Membership is rechecked on every call. It never changes durable execution state.

The core adapter validates exact response fields, timestamps, actual scope and
generation. Missing, malformed, foreign or contradictory responses fail closed;
provider/auth denial cannot fall back to cached `Active`. The report service
rejects mismatched snapshots before reading evidence. A completion race can
therefore remain unavailable until an explicit fresh read; it cannot invent a
terminal result or claim that the worker remains permitted.

Private MCP validates the same contract and reports `AwaitFencedRecovery` for
Expired/Missing, with `canReplay:false` and an explicit instruction to preserve
retained input and not resend commands. Active remains an as-of snapshot for
explicit polling, never present-time input authority. The raw `leased`/`Pending`
facts and unassessed objective are unchanged.

The separate Lovable consumer validates the new optional metadata and shows
permission and its checked time. It consumes the core's status, without deriving
leases from browser time or querying worker tables. Earlier reports remain
compatible during upgrade. Existing Supabase Edge remains a bounded transport.
See [delivery and acceptance](../validation/worker-permission-reporting-v1.md).
