# Retain command progress before acknowledging keyboard input

October 28, 2026. Additive worker protocol within core 1.9.0. HTTP, private
MCP, queue schema, Edge Functions and the consumer contract are unchanged.

An admitted keyboard recording now uses a separate framed child channel. The
child reports its actual baseline, then an intent before each authored command,
then the actual attempt and observed result. It cannot advance until the host
acknowledges the exact execution ID and sequence. The host renews the actual
lease and saves each checkpoint before acknowledging it. No lease token,
database credential or storage credential is sent into the browser container.
Stdout still carries only the complete original recording bundle.

The platform-independent `TraversalCheckpoint` belongs to Domain. The runner's
optional awaited callback does not change navigation semantics. Recording hooks
run only while capture is permitted, on the fixed sanitized recording fixture.
No general-site capture or credential entry is added.

Worker-local `.runtime/command-progress/<organization>-<job>-<generation>/`
retains the original job, actual producer and native ownership identity. Each
bounded record names its predecessor hash. A write-through temporary file is
flushed, closed and atomically renamed before acknowledgement. An interrupted
next temporary write is reported separately from the valid committed prefix.
Changed, truncated, gapped, renamed, foreign or unexpected committed inventory
fails closed. An existing attempt cannot be overwritten. Complete bundles must
agree with every retained checkpoint before publication.

`--inspect-command-progress <organization-guid> <job-guid> <generation>` is an
operator-local worker command using only the private evidence directory. It
requires no database authority and grants none. Its summary always says
`IsCompletionReceipt=false` and `CanReplay=false`. A saved intent means input
may have occurred; a retained result describes that command, not an overall
verified goal. This is not yet a tenant API or notebook projection.

After a real worker process kill, startup reconciliation removes only the
independently verified owned container. The existing expired-keyboard fence
blocks the original attempt without a replacement generation. Progress remains
unchanged; a deliberate separate observation can establish recovered capacity.
`Restart=no`, `KillMode=process` and `SendSIGKILL=no` remain in force.

Process-crash retention is the tested durability scope. These hashes do not
establish power-loss durability, custody or admissibility. Interrupted video,
offhost reconstruction, upload-before-commit and real storage/disk failure
recovery remain in the [completion plan](../completion-plan.md#next-recover-interrupted-recordings-safely).
See [validation](../validation/retained-command-progress-v1.md).
