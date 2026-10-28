# 0058 Admitted observation archive provenance

Accepted October 27, 2026. Core HTTP contract 1.6.0 extends the existing
input-free synthetic-shop worker. It does not enable hosted recording.

The durable attempt identity is the existing database primary key
`(organization_id, job_id, generation)`, together with its actual `worker_id`.
The nonsecret descriptor already binds organization, scan, job and generation
before Chromium starts. Do not invent an attempt UUID or relabel operator imports.
The lease bearer and token hash never enter the browser descriptor or archive.

After actual collector/browser shutdown and independently confirmed owned
container removal, the coordinator flushes and reads back the exact observation.
Its producer finalizer creates an `hsr-worker-observation-package-v1` source
manifest with the actual descriptor, worker and original observation SHA256.
Scope is `ControlledSyntheticFixtureObservation`, origin is
`QueueWorkerObservation`, recording is `NotCollected`, dispatched input is zero
and goal verification is false. The two original files are `observation.json`
and `evidence-manifest.json`.

Existing archive finalization performs both integrity passes, writes and reads
back every object, then publishes and reads back the content-addressed index
last. Archive ID is the actual job ID; separate generations have distinct
manifests and index digests. Production uses configured private S3; a local-only
worker uses its owned `evidence/archives` directory and does not claim off-host
readback. A finalizer error or cancellation prevents successful completion.
Actual callbacks settle before ownership is released.

`complete_scan_job_with_archive` is executable only by `hsr_worker`. It delegates
the actual capability, worker, generation, clock and queue-ack checks to the
existing completion function and publishes index/manifest digests, total bytes
and actual worker in the same transaction. A failure after queue acknowledgement
rolls back queue, result, receipt and attempt event together. Lost fences leave
orphan bytes, never a discoverable completed receipt. Exact retries require the
original capability and same receipt. Historical completed jobs cannot acquire
a receipt retrospectively. Existing job RLS and read-only member grants apply
to the new nullable columns; there is no user-writable evidence catalog.

The authorized scan report and private `scan_report` MCP return optional
`workerArchive`, including the descriptor, worker and immutable archive receipt.
Its identity can be retained for existing `archive_manifest` and `archive_file`
operations after reconnect. Adapters reject mismatched tenant/scan/job/attempt,
unconfirmed readback, wrong origin or invalid inventory. A known receipt is
historical publication evidence: current originals may still be missing,
unavailable or invalid. In particular, `Unavailable` does not become `Available`
because an archive identity is known. The Lovable consumer accepts and validates
the new optional field through its existing Supabase Edge transport.

The additive migration is `20261028011148_worker_observation_archives.sql`,
matching the hosted Supabase migration version. Old observation completion stays
compatible and remains explicitly without a producer receipt. The bucket limit
and upload MIME policy are unchanged.

Next: extend the actual isolated collector to produce a recorded traversal
package under an admitted attempt, with capture exclusion, source/action/result
links, original media, timing and native cleanup proven end to end. The current
two-file contract deliberately cannot describe video or a completed objective.
Operator-imported graph/video playback, legal scope and human/AT comparison keep
their existing separate meanings.
