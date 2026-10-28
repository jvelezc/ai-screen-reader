# 0061 Preserve raw frame delivery and index presentation time

October 28, 2026. Actual original-user admission on deployed core `4b96372`
retained five verified private S3 originals and a playable 1.6-second video.
The reached route had two completed keys, three states and two edges. Timing
correctly remained `TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID`.

The retained original reveals a real CDP startup condition: source delivery 1
has timestamp 1793181156328.373 ms, followed by delivery 2 at 1793181156324.260 ms.
All 18 source JPEGs and 40 decoded video frames have valid markers. The 4.113 ms
reversal is in delivery versus presentation order. No timestamp or original is
changed; the earlier package remains uncalibrated.

The collector now exposes a stable **presentation-order index** alongside raw
source frames and precise deliveries. Alignment callers explicitly select that
index. The admitted original persists `SourcePresentationOrder`; parent validation
requires a complete bounded permutation and a chronological projection. Source
files remain one-to-one with original frame metadata, including delivery filenames.

Strict alignment validation is unchanged: a reversed chronological input,
invalid/missing markers, conflicting clocks or excessive uncertainty still
produce Unknown. The index creates no frame, timestamp, input or successful goal.
Every matching marker still contributes to the conservative uncertainty bound;
the 250 ms target is unchanged. The native controlled-fixture harness must now
prove calibrated timing within that existing bound, alongside actual originals,
closed capture and independent native absence before completion.

`PresentationIndexRetainsReversedRawDeliveriesAndCalibratesWithoutChangingTimestampsOrOutcome`
reproduces the reversal: raw alignment fails, the explicit index calibrates,
original delivery timestamps remain unchanged, and the receipt remains Unknown.
Additional bundle cases reject missing/duplicate/out-of-range indices and aliased
source frames. Actual Windows/Linux CI, exact promotion and fresh original-user
playback/seek remain separate acceptance gates.
