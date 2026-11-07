# 0087: Source-bound isolated mock task collection

The four embedded v1 tasks now have an independent `MockTaskJob` contract. Its
workspace, scan, job, fixed profile and source SHA are required; generation is
exactly one. It accepts no URL, HTML, selector, credential or authored task. Public
page reviews remain input-free and cannot decode this descriptor.

The trusted parent starts the immutable image as a nonroot, read-only, no-network
container with bounded resources and no host mounts or forwarded credentials.
Inspect the actual image, fixed entrypoint/command and ownership labels before
starting. A four-field legacy DTO is used only by the existing native cleanup
journal; it is not a Foundation observation or mock source descriptor.

The child consumes a strict bounded start frame, then requires a matching parent
acknowledgement at thirteen fixed boundaries: opening each fresh browser, each
keyboard command, the separate pointer action and final package preparation.
Every acknowledgement binds the complete start identity, monotonic sequence and
SHA256 of its exact intent. Missing, old, foreign or changed acknowledgements
cancel collection; a permission to open does not authorize the following Tab.

The parent checks current authority before and after writing each grant intent.
It flushes that intent to private durable storage before sending an acknowledgement.
`InputMayHaveOccurred` is conservative possible delivery, never an observed key or
goal. Retain these records when collection fails. The original job fence is
created once and is never automatically removed: completed or uncertain input
cannot be replayed using the same identity, even after native cleanup. An explicit
new attempt needs a new job. Queue retry rules must preserve this fence.

The existing native recorder is reused through optional internal hooks. Existing
public local-recording method signatures are preserved. The parent bounds the
returned ZIP without extracting files; checks exact inventory, source, original
hashes, run/document independence, fixed plans, input events, graph edges, actual
goal observations and closed media/browser lifecycle; and recomputes the narrow
comparison description. Public-site, legal, AT and verified-timing claims remain
forbidden. Before collection the child saves `mock-task-execution.json` in the
original package; its hashed inventory binds the exact caller/job/worker/execution
identity. Changing only a transport envelope cannot relabel an earlier package.
Original files are transported unchanged. Maximum uncompressed
original bytes are25MiB, plus separately bounded ZIP/transport overhead; this
does not change the existing hosted bucket configuration.

Authority is checked again before returning originals and after confirmed owned
container removal. Caller cancellation does not cancel cleanup. Unknown removal
retains the native intent and prevents a completed result. Recovery removes only
the recorded native resource and never repeats the task.

`--check-isolated-mock-tasks` exercises the actual immutable image with four
source-bound collections, loss before Tab/pointer, denied replay and nine native
absence/removal receipts. That count includes replay-denied names that never
created a container; it is not a count of started containers. Its callback is a
local acceptance callback, not a Supabase user lease.
The existing CI worker/image pipeline now runs it before durable queue tests.

This gate supplies the execution contract, not tenant admission. Next implement
the fixed mock source registry, atomic caller-scoped admission/status, no-replay
claim/renew/complete/cancel transactions and original publication; then expose
HTTP/private MCP and connect Edge/UI. Do not accept queued tasks before that
complete lifecycle is validated. Real public tasks and real AT remain separate.

See [validation](../validation/isolated-mock-task-collection-v1.md).
