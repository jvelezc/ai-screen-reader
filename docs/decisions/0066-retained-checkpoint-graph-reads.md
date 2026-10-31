# Read retained checkpoint paths without replaying an attempt

October 30, 2026. Additive HTTP contract **1.11.0** and two private MCP tools.
The core owns graph reconstruction and checkpoint export; the separate Lovable
consumer continues through the existing Supabase Edge Function.

`GET /api/workspaces/{organization}/scans/{scan}/progress/graph` resolves the
current admitted job, generation and actual producer through the original
caller's existing RLS access. It verifies the entire available local journal
prefix, then returns its compact status, checkpoint manifest, observation nodes
and command edges. Repeated references to the same observation must retain the
same complete semantics; different observation IDs remain different nodes.

Each input intent is an edge. An observed result adds its actual destination,
command outcome, verification and completed-input count when retained. An
unresolved intent has no confirmed destination, input count or verification.
An observed command with no retained step also has unknown input, rather than
zero input. A satisfied command does not establish the overall goal.

The manifest contains sequence, phase, command index, source journal-entry seal,
canonical checkpoint-export hash and export byte length. These are distinct:

- `sourceSha256` hashes the sealed journal entry, including its private wrapper.
- `checkpointSha256` hashes the canonical serialized `TraversalCheckpoint`,
  using the worker wire format: PascalCase properties and string enums.
- The graph uses camelCase properties and string enums. Complete AX snapshots,
  actual reader events, source, instruction and optional result remain in the
  checkpoint export. They are retained page semantics, not recorded AT speech.

`POST .../progress/checkpoint` accepts only `prefixSha256`, `sequence` and
optional `offset` (default 0). Each page contains at most **16 KiB** of base64
checkpoint bytes, its page hash, full export hash and next offset. Offsets must
be 16 KiB aligned or exactly EOF. Reconstruct every byte and verify the full
export hash before claiming whole-export integrity. The raw journal wrapper,
execution/native ownership identity, header, image and capabilities are excluded.
This is a canonical full-checkpoint export, not an original journal-file download.

Every graph/page read rechecks workspace, scan, job, generation and actual
producer before and after storage, including storage failure paths. A changed
attempt conflicts; denied access releases no prior data. A changed prefix
returns `CONTROL_PROGRESS_PREFIX_CHANGED`: reread the same scan's graph and
restart the download, never execute the route again. No cache, automatic retry,
polling, replacement worker or database privilege is introduced.

NotApplicable, NotStarted and NotRetained remain explicit graph states. A byte
request without retained content returns 404. Invalid page arguments return 400;
corrupt storage or changed identity returns 409; unavailable storage returns 503.
Known incomplete `.pending` writes are flagged and excluded from the acknowledged
prefix. The parser still validates later committed records before releasing an
earlier checkpoint. The graph response is bounded at 64 KiB; the existing journal
limits remain nine checkpoints, 1 MiB per frame and 8 MiB total. Oversized graphs
fail explicitly without truncation or cursor advancement.

Private stdio MCP adds `scan_progress_graph` and `scan_checkpoint`, for sixteen
tools total. The latter requires the graph's prefix and expected export hash;
the client validates current scope, flags, topology, page bytes, hashes and
offsets. Existing 256 KiB upstream and 128 KiB tool-reply limits remain. These
reads do not recover video, produce a completed recording, assess a barrier,
authorize replay or establish custody/admissibility.

See [validation](../validation/retained-checkpoint-graph-v1.md) and the
[remaining recovery gates](../completion-plan.md#next-recover-interrupted-recordings-safely).
