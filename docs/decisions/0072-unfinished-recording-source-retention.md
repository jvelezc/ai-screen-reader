# Retain original images from unfinished keyboard recordings

Status: implemented; delivery gates are tracked in the
[validation record](../validation/unfinished-recording-sources-v1.md).

An interrupted keyboard child previously retained its command prefix but lost
its container-local media. A complete output seal cannot describe those missing
bytes. Preserve the source images that actually reached the parent, under the
same admitted attempt, without inventing a final video or completion receipt.

The internal fixture channel keeps its command-progress v1 schema. A start
explicitly opts into a separate `hsr-fixture-source-frames-v1` batch protocol.
Completed source JPEGs use their original sequence, raw Chromium double timestamp
and SHA-256. Delivery sequence is separate because source writes can finish out
of order. Legacy starts retain their existing command-only behavior. An opted-in
parent cannot acknowledge a baseline without retained source images.

Each batch carries at most 64 frames and 512 KiB of raw content. Individual
images are bounded, safe-named JPEG-signature bytes with matching hashes; this
validation alone does not establish decoding or image content. The existing
collector retains its 250-frame/16 MiB limit. The parent independently caps
retention at 250 images/25 MiB, snapshots mutable buffers, and verifies canonical
wire hashes. Source export joins the recording probe's exclusion drain. Cancelling
a waiter cannot make a still-pending transfer count as stopped.

The private `.runtime/recorded-sources` journal binds the original organization,
job, generation, producer and native owner. Non-overwriting original files and
header-bound batch manifests are flushed before acknowledgement. Current worker
authority is checked before and after actual storage. Reads independently check
paths, hashes, byte counts, packet reconstruction and scope. Orphan/pending files
are reported as uncommitted; they are not admitted frames. Completed output must
also match retained image bytes and original presentation timestamps.

`--inspect-recorded-sources <organization> <job> <generation> <producer>` is a
private operator diagnostic using `HSR_WORKER_EVIDENCE`. It returns checked
metadata, never image payloads or credentials. It grants no input or publication
authority. Hosted HTTP/private MCP delivery of partial source files is a later
gate and must resolve the actual producer from durable admission metadata.

Every snapshot remains `RetainedOriginalSourceSubset`, `IsCompletedRecording=false`,
`CanReplay=false`, with completeness and overall goal `NotAssessed`. Retention
does not change unknown input, complete-bundle validation, native cleanup,
archive publication, database completion or evidence readiness. It is neither
a reconstructed WebM nor an assistive-technology speech recording. Process-crash
retention does not establish power-loss durability, custody or legal sufficiency.
