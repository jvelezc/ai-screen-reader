# 0069: Repair recording cleanup acknowledgements from durable native facts

Date: October31,2026. Status: implemented; delivery gates are tracked in the
[validation record](../validation/recording-removal-recovery-v1.md).

Native cleanup records actual owned-container absence before retiring its
intent. The parent can stop after that retirement but before copying the record
beside the saved recording. Recovery of pending intents alone misses this gap,
leaving a valid complete bundle permanently CleanupUnconfirmed.

After existing native recovery, the worker streams the private recorded-output
inventory under its existing exclusive host lock. A30-second startup budget
bounds this additional pass. Each header must match its derived tenant/job/
generation directory. The matching owner record must bind the exact native
intent and explicitly confirm absence. Header identity/hash, regular paths,
bounded metadata and allowed inventory are checked before copying; recording
payloads are not loaded during startup. Missing native facts stay AwaitingRemoval.

Only this bookkeeping copy becomes idempotent. An existing acknowledgement
must equal the canonical durable record byte for byte. A completed exact
`.pending` copy may finish its non-overwriting rename. Foreign, truncated,
oversized, linked or altered final/pending copies remain errors and are
preserved for diagnosis. Unknown inventory and incomplete headers fail closed.
Normal begin/seal writes retain their immutable non-overwriting behavior.

An unsealed stage may receive a real cleanup acknowledgement; it still has no
complete recording. No seal, bundle, published archive, database completion or
replacement attempt is created. A historical cleanup record is not a fresh
native absence observation. Existing HTTP1.12/private MCP exports still require
their full bundle validation and original caller/actual producer checks before
and after storage. Automatic worker restart remains disabled.

Publication reconciliation under the original attempt and recovery of media
from an unfinished child remain separate work. Damaged bookkeeping is not
silently discarded or reconstructed from a caller success flag.
