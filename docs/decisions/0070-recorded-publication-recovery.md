# 0070: Recover publication with storage-only authority for the original attempt

Date: October31,2026. Status: implemented; delivery evidence is tracked in the
[validation record](../validation/recorded-publication-recovery-v1.md).

A complete recording can survive native cleanup and archive finalization while
its database completion fails. Renewing the expired execution capability would
break the keyboard replay fence. Blindly repeating persistence also fails at the
immutable local observation file, even when its bytes match.

Keep execution leases and original `job_attempts` unchanged. A separate private
`recorded_publications` capability binds the original organization, scan, job,
generation, producer, recorded route and complete bundle digest. Four narrowly granted functions
claim, renew, fail and complete storage publication. They issue no queue claim,
browser authority, input capability or replacement execution generation.

The claim verifies the actual durable attempt, recording flags and current
membership of the original creator. Active execution or another active storage
claim returns Busy. Expired execution is durably blocked before storage authority
is issued. An exact existing completed job is left unchanged; legacy receipts
cannot gain retrospective provenance. Changed source, producer or generation is
rejected, including a changed route or attempted reopening of a completed publication.
Job-first locking and a fresh server clock after lock acquisition fence
renewal and completion. Each capability has a5–60-second TTL and a separate
ten-minute renewal budget. Explicit storage retries preserve the same bundle.

Both new tables have RLS. The capability/token hash table has no direct caller
or worker table grants. Private helpers are not worker entrypoints. Only
`hsr_worker` receives the four function grants; `public`, `anon` and
`authenticated` cannot invoke them. Safe Claim/Failed/Completed events remain
durable; current workspace members may read their own events through RLS.
Successful completion clears the current error while preserving prior failure
events and original execution provenance.

The worker validates a complete retained bundle and its matching historical
native-removal acknowledgement before claiming publication. The database binds
the actual admitted producer; the private header alone supplies no admission
authority. Recovery-only persistence can reuse the exact original local
observation after regular-path and byte/readback checks. Normal persistence
remains non-overwriting. Producer manifests and archive indexes are deterministic,
with original parts independently read back before the index and database receipt.

The publication coordinator has no executor/reader dependency. It checks storage
authority before and after finalization, renews independently, and joins actual
storage and renewal callbacks before returning or committing. Cancellation cannot
release a still-running operation. A lost/rejected completion remains Unconfirmed;
an exact completed retry returns AlreadyCompleted without repeating storage or
input. A Published receipt describes database/archive finalization; it does not
verify an overall goal or legal sufficiency.

The explicit operator command is
`HeadlessScreenReader.Worker --recover-recording <organization> <job> <generation>`.
It uses the existing private worker environment and exclusive evidence lock;
the polling owner must have settled first. It needs no browser image/runtime
configuration, cannot repair unconfirmed native cleanup, and does not add public
API/MCP mutation or automatic worker restart. HTTP1.12/eighteen private tools and
the existing Edge consumer remain unchanged; reports can read a recovered
admitted receipt through their existing authority checks.

This slice does not recover unfinished child media or claim universal storage,
disk-full, provider-authority-loss or hosted-human/AT acceptance. Separate receipts
must distinguish local Podman, Linux CI, hosted schema, exact deployment and the
original application account.
