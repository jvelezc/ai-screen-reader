# 0081: Resume only authoritative cleanup acknowledgement prefixes

Status: delivered atc6e367b.161 focused/3,145 full Windows and both CI OSs pass,
all9 CI jobs succeed and all151 installed files match the exact artifact. The
actual empty acknowledgement recovers without changing header/media/native proof;
old-binary recovery, deployment and original-account graph/playback pass.

An actual64KiB staging-volume failure retained a61,440-byte partial bundle and
an empty native-removal.json.pending. Native absence had been independently
recorded on the normal durable filesystem. The job correctly stayed blocked and
retained its source images, and a distinct normal job recovered capacity without
restart. However startup recovery rejected the empty acknowledgement.

RecordedOutputJournal may resume a bounded pending acknowledgement only after
validating its immutable stage header, the actual independently durable native
removal and any existing final marker. Every pending byte must be an exact prefix
of the expected acknowledgement. Under exclusive file access it rechecks the
length/bytes, appends only the missing suffix, flushes to disk, verifies the exact
record and atomically renames without replacement. Existing complete pending
records keep their read-only rename path. The metadata limit remains8,192 bytes.

No bytes are truncated/deleted; corrupt, oversized, foreign or unconfirmed proof
cannot extend a prefix. Cancellation stays uncertainty, and a subsequent valid
retry can resume a remaining exact prefix. This repairs only historical native
cleanup acknowledgement. Partial media stays unsealed, recording/goal completion
and publication remain separate, and recovery executes no website input.

See [actual disk fault and validation](../validation/hosted-full-staging-disk-v1.md).
