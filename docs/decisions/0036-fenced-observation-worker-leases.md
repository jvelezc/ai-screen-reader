# Fenced observation worker leases

The admission schema is installed in the selected Supabase project. This increment adds database ownership and attempt protocols for its fixed `observe-fixture` stage. The native coordinator and browser execution remain a subsequent gate. Public MCP hosting/plugins remain deferred.

`hsr_worker` is a non-login capability role. It can execute only four worker entrypoints: claim, renew, complete and block. It cannot read the raw queue, active lease tokens or other workers' capabilities, change application tables directly, or call internal unfenced helpers. Authenticated clients retain tenant-bound reads and admission; they cannot invoke worker operations. Future runtime login roles must inherit only these narrow capabilities.

Active leases live in a private RLS-enabled table. Client-readable job rows contain safe state/generation/result metadata. Application roles cannot rewrite attempt facts or events; attempts store only a token hash. Database administration remains a separate trusted authority. Membership-based RLS also protects attempt and event reads.

Claim validates a non-null worker identity and a5–60-second duration. Actual pgmq delivery and a job row lock bind the exact canonical message before granting a random capability. Claims use `SKIP LOCKED` on a busy job, avoiding a queue-first/job-first lock cycle with renewal/completion. A prematurely visible message cannot replace an unexpired lease. Server time is checked after acquiring ownership locks. Queued backlog does not consume execution time: a ten-minute hard budget starts at first claim. A job allows at most three attempts.

Renewal requires the current organization, job, generation, worker and token. It requires an unexpired lease, clamps expiry to the original hard budget and updates actual queue visibility with an acknowledged result. Expiry or replacement prevents renewal, completion and blocking. Redelivery retains the expired attempt and issues a new generation/capability. A budget or attempt limit leaves an explicit blocked state.

Completion requires a canonical lowercase SHA256 digest and a relative reference bound to organization/scan/job/generation. Result state, queue deletion, capability removal and completion event are transactional. A post-delete failure rolls back even the actual queue deletion. A lost response can be retried: only the original worker/capability, generation, digest and reference return the prior durable success. Conflicting retries leave the original result intact.

The database validates the receipt's binding, not its file bytes. The future trusted coordinator must persist and verify original observation bytes before completion. `scans.state='observed'` means one observation receipt was supplied; it is not goal verification, complete exploration or private S3 finalization. The current SQL tests use synthetic digests and do not claim browser execution.

Unbound or malformed queue messages receive safe quarantine records containing message identity, reason and time, with no raw payload. Bound malformed jobs remain blocked. Explicit worker failure codes are allowlisted; arbitrary exception detail cannot enter the attempt log. Engine failures are not accessibility barriers.

Database lease expiry does not prove the old browser stopped. This stage is observation-only and grants no target-input authority. Native process cleanup, exclusive input ownership, capture exclusion and human assistance remain separately enforced requirements. Uncertain input must not inherit this read-only retry policy.

Validation uses actual PostgreSQL/pgmq, controlled acknowledgement faults, two tenant identities and concurrent database sessions. A completion that starts while valid but waits on a job lock until expiry is rejected. This increment must pass local and eight-job CI acceptance before hosted migration or native execution work advances.
