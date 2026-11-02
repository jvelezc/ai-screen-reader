# 0079: Copy terminal originals inside the final acquisition drain

Status: delivered at6488605, with all nine CI jobs, exact private delivery and
fresh four-route retention acceptance. Subsequent long-video timing is delivered
separately at5053852.

## Problem

The last observed traversal checkpoint copied its pending source images while
acquisition still accepted new frames. Waiting for that transfer could consume
the source-frame budget even after the actual command had finished. Finalization
already had a separate input-denied, joined acquisition drain, but the last
checkpoint transfer ran before it.

On live7fd3fc5, a distinct original-account checkout-preview run retained two
intents/two observed results and a locally Satisfied activation, then stopped with
`WORKER_PROGRESS_RECORDING_CAPTURE_BUDGET_EXCEEDED`. Its250 exact originals used
3,367,671 bytes; no result/archive was committed. This engine failure cannot
establish a website barrier or a completed recording.

## Decision

For the last instruction's Observed checkpoint only, retain the actual command
result before transferring its remaining source images. The existing final flush
copies them after input is denied and both acquisition collectors are joined.
Earlier checkpoints continue transferring their completed originals before the
checkpoint acknowledgement.

Keep this composition in the CLI. Domain, reader semantics, browser contracts,
API/MCP, Edge and consumer schemas remain unchanged. Keep the250-image/16MiB
acquisition bound,250-image/25MiB parent retention bound and all native deadlines.
Do not sample frames, drop acquired originals, enlarge limits or replay failed
attempts to make the acceptance pass.

## Consequences

The terminal journal acknowledgement confirms the retained observation. Full
recording completion still requires the joined final transfer, original-media
validation, exact native cleanup and fenced publication. Partial-image reads can
remain a subset while the terminal result is already retained; their existing
NotAssessed/incomplete labels remain authoritative.

Calibration and the trailing measurement hold stay outside the traversal runner;
decoding is not moved into the command loop. A failed/cancelled checkpoint cannot
start the final source transfer or claim a recording. Unknown goals remain Unknown.

[Validation and the next gate](../validation/terminal-observed-source-drain-v1.md).
