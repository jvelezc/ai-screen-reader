# 0074: Caller-owned admitted-run cancellation

October 31, 2026. Status: implemented; hosted delivery and original-account acceptance tracked separately.

An admitted worker run continues after its client disconnects. The original user
needs an explicit way to end that run without deleting its records, replaying
uncertain input or allowing a separate publication capability to complete it later.

`POST /api/workspaces/{organization}/scans/{scan}/cancellation` accepts only `{}`.
The caller is the existing confirmed application identity. The database requires
current workspace membership and the scan's original creator, holds membership
through commit and serializes against completion using the actual job lock.
The immutable scan makes repeat requests idempotent; no new request ID is needed.

A persisted cancellation marker leaves the job and scan blocked, archives its
queue message, revokes its execution lease and fences both new and already-issued
publication capabilities. The database constraint forbids resuming a marked job.
Generation, producer, attempt history and retained originals remain unchanged.
An already completed job returns an unchanged `AlreadyCompleted` result instead.

HTTP202 `Requested` is a stop request, not proof of physical stop or native cleanup.
An input authorized before revocation may remain in flight. A claimed attempt's
input assessment remains `NotAssessed`; only generation0 with no actual attempt
is `NoAttemptStarted`. The response is never a completion receipt, a goal result,
a barrier finding or permission to replay. Current authority is checked again
after the mutation and producer reads, before metadata is released.

Core HTTP1.14 and private MCP `scan_cancel` expose the same distinction. MCP
checks the status/body pair and strict attempt number. It does not retry writes,
poll automatically, cancel on disconnect or admit a replacement. The separate UI
uses the existing Supabase Edge Function as a closed adapter; cancellation and
evidence logic remain in the core.

See [validation and remaining delivery gates](../validation/caller-scan-cancellation-v1.md).
