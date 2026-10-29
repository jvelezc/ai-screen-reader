# Read retained command diagnostics through the core API and private MCP

October 28, 2026. Additive HTTP contract **1.10.0** and private `scan_progress`
tool. The independently usable core owns this feature.

`GET /api/workspaces/{organization}/scans/{scan}/progress` resolves the current
job, generation and actual producer using the original caller's RLS access.
Existing `job_attempts` permissions already support the selected `worker_id`
lookup; no migration, privilege expansion or service credential is added.
The control process already reads the worker's private evidence directory.

The response separates scan execution from progress availability:

| Progress state | Meaning |
|---|---|
| NotApplicable | This run did not request an authored keyboard route. |
| NotStarted | No worker attempt has started. The same scan ID can be read explicitly later. |
| NotRetained | No local prefix exists for this attempt, including older runs. |
| Available | A valid, bound diagnostic prefix is retained. The run can still be blocked or incomplete. |

Missing or invalid scope fails before storage access. Authority is checked again
after storage, including failure paths. Changed job/generation/worker returns a
conflict; membership loss cannot release the earlier payload. Corrupt inventory
and unavailable storage retain distinct safe errors. Cancellation releases no
old result. Every read is fresh and uses no cache or automatic retry.

The compact Domain contract retains checkpoint counts, phase, command, source
and result observation IDs, the last command's actual input count/verification
when observed, the prefix hash and an explicit uncommitted-write flag. A saved
intent means input may have occurred. Unknown input counts stay null. Native
ownership, image, capabilities, filesystem paths, raw AX snapshots and complete
recording bytes are excluded. This is a summary, not retrieval of every retained
checkpoint or a recovered graph/media package.

`isCompletionReceipt=false`, `canReplay=false` and
`goalVerification=NotAssessed` are validated even when the last retained command
satisfied its predicate. Read `scan_report` and complete bound archive originals
for completed-run evidence. Progress never upgrades an engine failure into an
accessibility barrier or authorizes reexecution.

Private API-backed stdio MCP now has fourteen tools. `scan_progress` takes only
`scanId`, retains original-user scope, validates the returned attempt and flags,
and preserves the existing 128 KiB tool / 256 KiB HTTP bounds. It neither polls
automatically nor sends browser input. Public MCP/plugins remain deferred.

The Lovable consumer continues through Edge Functions in the existing Supabase
project. This core increment does not add an Edge operation or a UI panel.
Original-account consumer acceptance for this new read is a separate gate.
Interrupted-original recovery, offhost partial diagnostics and the remaining
storage/disk/cancellation injections remain in the
[completion plan](../completion-plan.md#next-recover-interrupted-recordings-safely).

See [validation and exact tests](../validation/tenant-retained-progress-v1.md).
