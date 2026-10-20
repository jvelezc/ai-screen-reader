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
