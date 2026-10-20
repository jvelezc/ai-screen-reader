# Controlled collector validation v1

October 20, 2026, local Windows: **652 discovered/executed/passed, 0 failed/skipped**. All 641 prior test identities are retained; 11 cases were added. Release nonincremental build has zero warnings/errors; formatting verification passed. Local receipt: `artifacts/capture-validation/collector-windows-final.trx`. The CLI `--capture-probe` also passed and saved independently inspectable originals, staging media, hashes and a report.

| Requirement | Evidence |
| --- | --- |
| Stop awaits actual writes and late callbacks cannot reopen a segment | `Close_WaitsForActualWritesAndPermanentlyRejectsLateCallbacks` |
| A precise-source timestamp filter has an inclusive boundary | `PreviousEpochPresentation_IsDroppedAndBoundaryTimestampIsAdmitted` (the quantized SDK timestamp is deliberately not used as this filter) |
| Frame and byte budgets allow the exact limit then fail closed | `ExactFrameAndByteBounds_AreAllowedAndNextWriteFailsClosed` (both budgets) |
| Invalid frames are never written | `InvalidFrame_IsNeverWrittenAndFaultsSegment` (nonfinite timestamp, empty and negative size) |
| Writer failures are sanitized and still drain | `SinkFailure_IsSanitizedAndDrainStillSettles` |
| Actual original WebM/JPEG/staging pixels exclude the visible canary, with independent positive control, same-context resumption and hashes | `OriginalSegmentsAndStaging_ExcludeVisibleCanaryWithIndependentPositivePixelControl` |
| Cancellation before launch creates a failed receipt without claiming collector/inspection success | `AlreadyCancelledProbe_RetainsExplicitFailedReceiptWithoutStartingCollectors` |

The existing admission tests still cover stop/baseline failure and timeout, token ownership, late observations, denied normal/scoped keyboard input and reader epoch reset. No real credential entry or human takeover was performed. The positive-control screenshot is held only in memory by the trusted synthetic diagnostic probe.

An initial .NET probe failed because a precise-time admission cutoff was incorrectly applied to the SDK's single-precision Unix timestamp. That cutoff was removed. Reported timestamps now carry conservative quantization uncertainty (131,072 ms at the current epoch); **250 ms synchronization is not proven**. Raw presentation timing and at least 20 objective markers are next.

This is a controlled fixture proof, not arbitrary-site privacy certification. Default/public sessions do not start recording. Remote Windows/Linux and independent isolation acceptance are reported separately by the corresponding main CI run. No local Linux, deployment or fresh assistive-technology check was performed for this increment.

## Decoder boundary follow-up

Further synchronization research exposed ambiguity when drawing a video's initial frame before explicit presentation acknowledgement. The decoder now seeks every frame including frame zero, awaits `requestVideoFrameCallback`, verifies the reported media time within 1 ms of the expected 25 fps position, and rejects transparent/unpainted data. It retains first/last canary-pixel counts and sanitized decoder-specific error codes.

`VideoDecoder_InspectsActualFirstAndLastFramesWithIndependentSolidColorControl` records an intentionally red-first, green-last control outside the exclusion probe's artifact directory, then verifies both decoded boundaries. Local Windows full validation: **653 discovered/executed/passed, 0 failed/skipped**, retaining all 652 prior identities. Nonincremental Release build and format verification passed. TRX: `artifacts/capture-validation/decoder-windows-final.trx`. The preceding 652-case increment passed Windows, Ubuntu and independent isolation CI in run `39471215742`; this follow-up has its own main CI gate.

### Windows CI correction

Run `39472868577` passed Ubuntu and independent isolation, but Windows failed the boundary control with `CAPTURE_DECODE_TIMEOUT` while waiting for a compositor frame callback. That callback path is replaced by native decoding through the FFmpeg revision in the pinned Playwright manifest. Every decoded PNG is inspected, preserving exact first/last frame checks and the existing canary assertions. Originals are unmodified; no test is skipped or retried to hide failure. `NativeDecoder_InvalidMediaFailsWithSanitizedCode` also verifies malformed and empty media rejection. The subsequent main CI run is required before this correction is accepted.

Local Windows correction validation: **654 discovered/executed/passed, 0 failed/skipped**. All previous behavior remains in the suite. Nonincremental Release build and format verification passed; the CLI native probe passed. TRX: `artifacts/capture-validation/native-decoder-windows-final.trx`. The decoder uses the package manifest's FFmpeg revision and honors Playwright's configured browser cache; it does not require a system FFmpeg installation. Frame/byte/decode-time bounds remain explicit. Temporary decoder files are removed without recursively deleting any directory.

Correction acceptance: CI run `39474964008` passed **654/654** on Windows and Ubuntu, both CLI probes, and the independent Linux isolation/public-reader regression. Its downloaded test identities and counts were checked separately from the local run.
