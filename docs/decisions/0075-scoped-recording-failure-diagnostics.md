# 0075: Scoped recording-failure diagnostics

October31,2026. Status: terminal diagnostics accepted at1200190; collector-cause
preservation implemented, with its validation/delivery and hosted cause pending.

An admitted keyboard recording can retain command checkpoints and source images
but fail before producing its complete original bundle. The framed child formerly
caught every exception and returned exit3. The host consequently stored only
`FIXTURE_OBSERVATION_UNCONFIRMED`, hiding even an established recording error.
The retained disabled-control case is a concrete example; its actual cause is
not inferred merely from its250-image boundary.

The existing progress channel now admits one bounded terminal diagnostic schema,
`hsr-fixture-recording-failure-v1`. Its five fields contain the original execution
ID, exact checkpoint/source-batch prefix counters, schema and a closed reason.
It carries no page data, message, stack, file path, credentials, input count,
cleanup assertion, goal or completion reference. Known constant recording errors
map to specific reasons. Unknown exceptions map only to `Unconfirmed`. A start
that never passed admission validation emits no diagnostic.

A distinct normal hosted attempt still fails as Unconfirmed. Capture stop and
browser disposal intentionally sanitize collector failures; the recording probe
therefore retains its own closed cause during stop. Only frame budget, invalid
frame, write failure or source-export uncertainty can cross this path. A private
JSON-ignored CLI field carries that scalar into the terminal diagnostic mapper;
no exception object or message is serialized. Only the exact immediate capture-
stop/collector-stop wrappers are recognized. Unknown/deeper/other shutdown causes
stay Unconfirmed. Privacy stop, browser shutdown and native removal keep their
existing separate acknowledgement and halt rules. Capture limits are unchanged.

The host validates the exact execution and accepted prefix, the declared plan and
source limits, required fields, strict types, duplicate/unknown fields and the
1 KiB diagnostic limit. A valid failure receives no acknowledgement and admits no
following command/result. It cancels the actual runtime read, joins its process
and pipes, and still independently removes only its native owner. Uncertain
native ownership/cleanup keeps the existing halt behavior. A caller's committed
cancellation still wins through the original capability-bound confirmation.

Only thirteen closed diagnostic codes can become durable worker block reasons.
The existing worker-only `block_scan_job` function preserves its original
tenant/worker/generation/token fence. Its expanded exact allowlist rejects raw
details and arbitrary codes sharing the prefix. A recording failure remains an
engine/evidence failure, separate from a recorded accessibility barrier.

The HTTP1.14/private MCP contract and Edge consumer boundary are unchanged. Partial
originals remain available only through their existing scoped diagnostic reads;
they cannot become a final video, complete receipt, Ready package or replay.
See [validation](../validation/recording-failure-diagnostics-v1.md).
