# Preserve uncertain keyboard input after lease loss

October 28, 2026. Additive safety behavior within HTTP 1.9.0/private MCP. No new
browser input, credential authority, Edge operation or public endpoint.

An expired keyboard worker lease cannot tell the next coordinator whether the
old worker dispatched its input. Automatically creating another generation
could repeat actions. The queue claim now blocks that job with
`WORKER_INPUT_OUTCOME_UNCONFIRMED`, retains its original attempt identity and
does not issue another capability. The existing transaction, queue/job lock
ordering, deadline, completed-receipt deduplication and worker-only grants stay
in force. Observation-only and passive-recording jobs retain bounded retries.

The coordinator carries the stopped job ID and safe code even when no lease was
issued. Source assessment reports Unavailable/RecordingRequired with an explicit
next action. It cannot turn an interrupted worker into an accessibility barrier,
confirmed successful input, or successful goal. The consumer accepts bounded
uppercase report outcome codes while keeping transport faults under CONTROL_*.

Preserve the existing record. A deliberate new test is a separate admission,
appropriate only when repeating its actions is intended. No automatic resume is
offered. Per-command durable intent/progress, interrupted-original recovery and
actual crash/storage/disk/restart acceptance remain in the recovery gate.

See [validation](../validation/interrupted-recording-recovery-v1.md).
