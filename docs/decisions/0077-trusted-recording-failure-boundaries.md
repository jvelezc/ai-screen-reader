# Closed recording failure boundaries

Status: implemented; CI, exact delivery and hosted diagnosis are separate gates.

Finite acquisition is delivered at7fbf57a. One fresh original-account recording
completes and its five original files match; a separate normal repeat still
fails Unconfirmed. Successful local recording and one complete hosted archive
do not identify that intermittent fault.

Keep a closed operation boundary inside the trusted framed child: collection,
calibration, traversal, finalization, alignment, preparation, validation, cleanup
or output transfer. Returned fixed runner stages identify a failed operation.
Successful cleanup preserves that operation; only an actual cleanup failure
changes it to cleanup. Output writes have their own boundary, including failure
after some or all bytes arrive.

The existing terminal diagnostic retains its v1 schema, five fields, execution
and checkpoint/source scope and1,024-byte ceiling. Append11 closed Unconfirmed
reasons to its existing13 values. Existing specific reasons win. Exact known
capture/native stop codes may refine uncertainty; they do not confirm shutdown.
Unknown/default/invalid boundaries remain generic. Exception text, stack, paths,
Data, arbitrary nested causes and target content cannot supply a new reason.
Keep the original public method call shapes through forwarding overloads.

The stage is diagnostic only. It cannot acknowledge input, native cleanup,
original publication, successful transfer or goal completion, even if a complete
archive reached the output stream before an error. No telemetry is added to the
recorded-evidence schema. Capture exclusion, native teardown, resource limits,
reader semantics and HTTP/MCP/Edge/UI contracts remain unchanged.

Add the same closed codes to the worker-only block RPC without changing its
tenant/job/worker/generation/token/deadline fence or grants. Reauthorize every
read. Retained progress and partial originals remain evidence of only their
confirmed prefix, without replay or accessibility-barrier authority.

[Validation and delivery gates](../validation/recording-failure-boundaries-v1.md).
