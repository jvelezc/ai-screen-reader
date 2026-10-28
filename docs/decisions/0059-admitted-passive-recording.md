# 0059 Explicit passive recording under an admitted attempt

October 27, 2026. Core contract **1.7.0** adds `recordObservation: true` to the
existing fixed-fixture admission and private `scan_start_fixture` MCP tool.
Default admission remains an input-free, two-file observation. Recording is
explicit and uses the already sanitized sibling fixture: no editable fields or
credential canaries. The fixed fixture digest distinguishes these observations.

The original caller and workspace authorize admission. An idempotent request
must retain both request ID and recording mode; retries cannot upgrade or
downgrade an existing job. The queue descriptor carries the mode. Legacy workers
defer recording jobs without claiming a capability. New workers use the
recording-aware claim function. Recovery journals retain their original four-part
cleanup identity; recovery never reexecutes a descriptor.

The credential-free isolated Chromium child retains one actual AX capture,
holds briefly for frames without dispatching input, acknowledges capture
exclusion and browser shutdown, and decodes the original WebM in an independent
Chromium instance. Its exact-entry stdout bundle is bounded to 25 MiB; the host
never extracts container-provided paths. Full hashes, scope, sanitized snapshot,
capture closure, decoded-frame count and zero-action graph are checked before
storage. Independent owned-container absence remains required.

The four producer originals are `observation.json`, `original.webm`,
`capture-state.json` and `evidence-manifest.json`. The capture graph has exactly
one state and **zero input edges**. `Timing=NotAlignedToSnapshot` explicitly
denies synchronization. Auxiliary collector JPEGs are not retained. Neither
the clip nor graph establishes a keyboard path, successful objective, user
encounter or accessibility barrier.

Existing index-last archive finalization reads every original back before fenced
completion publishes the receipt. Receipt `recording=PassiveObservation` and
four-file inventory must agree with the admitted mode. Old completion cannot
complete a recording job. Failed/cancelled collection blocks the attempt; this
increment does not publish partial clips. Historical receipts never establish
current original availability. The additive hosted migration is
`20261028053837_passive_recorded_observations.sql`.

API, private MCP and the separate Lovable consumer use the same scoped archive
reads. The UI goes through the existing Supabase Edge function. It verifies the
complete capture metadata and video hashes before creating a local playback
URL, clears it on workspace/access loss and does not autoplay. No credentials,
public object URL or browser-to-core connection is introduced. Bucket capacity
remains 25 MiB with no MIME restriction.

**Next:** admitted keyboard traversal recordings with source/action/result
edges, measured timing, explicit partial outcomes and the same fencing and
capture exclusion guarantees. Arbitrary-site recording, human/AT comparison,
custody, legal eligibility and public MCP publication remain separate gates.
