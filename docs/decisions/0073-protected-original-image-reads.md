# Protected reads of retained original-image subsets

Status: implemented; [delivery gates](../validation/retained-source-images-v1.md)
remain separate. This follows [source retention](0072-unfinished-recording-source-retention.md).

The independently usable core API 1.13 adds an original-image subset manifest and
64 KiB image byte pages. Private HTTP-backed stdio MCP adds `scan_source_manifest`
and `scan_source_image`, bringing its tool count to 20. Public MCP/plugin
publication remains deferred. Existing Edge/UI consumption is a subsequent gate.

The core resolves current workspace/scan/job/generation and the actual attempt
producer through the original caller. Before and after storage, including storage
failures, it checks that authority and producer scope remain valid. No request
selects a worker, generation, native owner, filesystem path or storage credential.
Unrecorded, unstarted, missing, empty and available subsets remain distinct.
Uncommitted files are reported separately from committed originals; no missing
input count, final video or completed recording is inferred.

The worker projects checked private source journals into platform-independent
Domain DTOs, omitting native/host wrappers. A source-prefix digest binds the
specific checked source version; this digest identifies content and is not
authorization or custody. Source sequence and raw Chromium presentation time
remain explicit and need not follow delivery order. The journal has at most 250
images/25 MiB; an individual image is at most 512 KiB. JPEG signature and matching
hashes alone do not establish successful decoding or image content.

Image reads require the exact prefix, source sequence and whole-image SHA-256.
The core revalidates the source version and complete selected image before
slicing a 64 KiB page. Offsets must align to 64 KiB or equal exact image EOF. Each
page includes compact original job/producer/image identity and a page digest,
rather than repeating the 250-file manifest. Both maximum manifest and page fit
the private MCP payload budget. A client must compare original identity on every
page, reconstruct and verify the whole-image hash, then make a fresh exact-EOF
read before delivery. Changed sources require rereading the same scan, never
repeating input. No automatic retry is introduced.

Every source response remains `RetainedOriginalSourceSubset`, completeness and
overall goal `NotAssessed`, `IsCompletedRecording=false` and `CanReplay=false`,
including when its job separately completed. Partial-image access is independent
of the existing complete retained ZIP and published archive protocols. It cannot
establish a final recording, actual AT speech, accessibility barrier, legal
standing, custody or a sufficient website-evidence package.
