# Finite recording finalization inside capture exclusion

Status: implemented; delivery and hosted acceptance are separate gates.

The final source upload previously took a snapshot while Chromium continued
acquiring images. An actual Chromium regression observed the source set grow
from 17 to 30 during a 500 ms final-transfer wait. The video also remained open
until that transfer returned. These are demonstrated local defects; they do not
establish the cause of earlier hosted recordings that failed Unconfirmed.

Finalize the route through the existing capture-exclusion drain. Deny input,
join precise-image acquisition and stop the video, then transfer the finite set
of remaining originals. The private final transfer requires Draining before
and after its actual acknowledgement. Public image copies still require
Permitted, including a check before waiting for the copy lock. There is no
Permitted window for final transfer and no private-gap acknowledgement until
the transfer and all collector work have joined.

Keep the existing no-argument finalization entry point. The overload accepting
the final source sink registers it once; duplicate or late finalization cannot
replace it. Cancellation or a failed transfer cannot acknowledge exclusion or
produce completed media. Native shutdown and sanitized capture errors retain
their independent contracts. Successful finalization retains every acquired
original without sampling or synthesis. A failed transfer preserves only the
independently confirmed host prefix as durable evidence; it does not prove that
every acquired file was exported. Existing count, byte and stop limits remain
unchanged.

The CLI uses this owned final flush after its final route observation and clock
calibration. Per-checkpoint original transfer stays unchanged. Domain, reader
semantics, the core HTTP/MCP contracts, Supabase and the consumer UI do not change.

[Validation and delivery gates](../validation/finite-recording-finalization-v1.md).
