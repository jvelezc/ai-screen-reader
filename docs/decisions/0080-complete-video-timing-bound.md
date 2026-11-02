# 0080: Align timing checks with the complete decoded-video contract

Status: implemented locally; full checks, delivery and fresh hosted timing remain
separate gates.

## Problem

The decoder and admitted original-recording contract allow up to 10,000 decoded
frames. Both timing analyzers still rejected videos above 250 frames. A valid
complete original could therefore play in full while its graph-to-video links
were explicitly unavailable.

On core6488605, a distinct original-account unchanged-feedback recording retains
240 exact source images and 282 decoded frames. All five originals independently
match storage; actual playback ends at11.28seconds. Its stored alignment remains
`TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID`, with no media cues. Independent
inspection confirms the index/timestamp/marker checks pass; the legacy count is
the remaining frame rejection. The original recording is not rewritten.

## Decision

Name the existing 10,000 decoded-frame contract in Domain and use it in admitted
record validation, native PNG framing and both timing analyzers. This keeps the
existing decoder/archive limit consistent across layers. It does not increase
source acquisition, parent retention, PNG byte limits or the250ms timing target.

Check every frame, including the suffix beyond the old boundary. Reject the next
frame past the existing contract. Preserve malformed index/time/marker rejection,
clock/identity checks and conservative ambiguity from every matching transition.
Long videos with contradictory timing stay Unknown and have no calibrated cues.

## Consequences

Valid late actions can link to their original media without truncation or
sampling. Goal verification remains independent of pixels or synchronization.
Historical originals, hashes and stored failed/uncalibrated results stay intact;
fresh runs establish acceptance of the new implementation. Domain remains free
of browser/hosting/storage dependencies; API/MCP/Edge schemas do not change.

[Validation and next acceptance](../validation/long-video-timing-v1.md).
