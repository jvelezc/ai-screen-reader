# 0083: Preserve closed evidence-publication diagnostics

Status: implemented in source; delivery/acceptance tracked in the
[validation record](../validation/publication-failure-diagnostics-v1.md).

## Problem

The actual scoped storage-denial job retains a complete original recording but
blocks with a generic observation-unconfirmed code. The worker captures a closed
archive cause privately; the durable caller loses it and sees NotCollected.
That prevents a clear distinction between failed capture and failed publication.

## Decision

Keep four platform-independent public codes in Domain: storage unavailable,
unconfirmed write/readback, invalid content and missing original. Map only defined
typed archive failures after the executor has returned and actual persistence has
begun. Persist the closed code through the existing worker block capability;
retain the separate diagnostic and never persist raw exceptions or provider text.
Native ownership/cleanup, caller stop and heartbeat loss retain their priorities.
Off-host bytes still cannot acknowledge database completion.

The migration extends only the existing exact block-code allowlist. Its active
lease, organization, worker, generation, token, expiry and cancellation fences,
empty search path and worker-only ACL remain unchanged.

HTTP1.16 returns the original blocked scan as Unavailable for storage/write/missing
causes, or Invalid for content failure. It carries no observation, published
producer or active worker permission. A contradictory provider state/receipt is
unconfirmed. Generic historical failures remain NotCollected and are not rewritten.
Published schema, private MCP and the separate consumer validate these combinations.

Private MCP adds a closed cause and InspectRetainedPublication next action with
CanReplay:false and overall NotAssessed. Advice refers only to the same scan's
retained recording if available, fresh-authority storage-only recovery, and no
input replay/overwrite. No failure code proves originals exist or are recoverable.
The UI consumes the published API through existing Supabase Edge11, with a plain
status notice. It does not perform reader, publication or recovery logic.

## Consequences

The original attempt and every acquired byte remain intact. Integrity failures
preserve conflicting bytes. A browser/network/storage error cannot become an
accessibility barrier, zero-input claim, verified task or sufficient evidence
package. Public MCP/plugins and broader provider/website/intervention acceptance
remain separate.
