# Bounded decoding of complete original video

Status: implemented and locally validated; CI, exact delivery and fresh hosted
recordings remain separate gates.

A new original-account route fails during media finalization while its238
retained source images remain within the250-image capture limit. A separate
actual Chromium reproduction demonstrates that the old native decoder rejects
a valid video longer than10seconds: it writes251 PNGs and rejects anything over
250. CFR video at25fps can exceed that count while the independently acquired
source-image count stays below250. This local reproduction does not recover the
precise old hosted exception or prove its cause from the source count alone.

Decode the unchanged original through the pinned Playwright ffmpeg's image2
output muxer and pipe protocol. Read complete PNG frames sequentially, retaining
their exact bytes and order; inspect every decoded frame in Chromium as before.
The pinned minimal build has no image2pipe output muxer. Never substitute a
system decoder, sample video, drop a prefix/tail or synthesize source frames.

Keep the16MiB encoded-input ceiling,4MiB individual-PNG ceiling and10second
native deadline. Bound retained decoded bytes to16MiB plus one bounded working
frame/fixed buffers. Match the existing archive contract's10000-frame bound,
using an extra native frame as an overflow sentinel. Budget failure returns no
partial success. The old decoder could expand250*4MiB=1GiB of PNG files and
resident frame arrays; the new path creates no PNG files. The source collector's
250 images/16MiB and parent retention's250 images/25MiB remain unchanged.

Validate PNG framing, dimensions and byte admission before reading/allocating a
declared chunk. Framing alone does not establish valid pixel or CRC evidence;
independent Chromium pixel inspection remains required. Truncation, cancellation,
malformed output, excess dimensions and resource limits stay explicit. Empty
native output remains subject to the real process exit check. Native failures
expose closed codes; stderr is drained through a fixed buffer and discarded.

Join the actual owned decoder process, stdout and stderr before returning.
On timeout or failure, kill/join only that process tree. Stage only the encoded
input in a fresh owned directory and delete known leaves without recursive
cleanup. Preserve the existing decoder method signature and production defaults.

This changes internal browser media inspection only. Domain, reader behavior,
HTTP/MCP/Edge/UI schemas, queue authority, source-original hashes, evidence
classification and capture exclusion do not change.

[Validation and delivery gates](../validation/bounded-complete-video-decoding-v1.md).
