# Controlled recording synchronization

The original milestone 2 gate requires at least 20 fixture action markers with no more than 250 ms absolute synchronization error. The new controlled .NET probe measures that gate with actual reader commands, original video, independent visual frame identities and an explicit uncertainty bound.

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --recording-sync
```

The command prints a report under `artifacts/recording-sync/<run>/`. It retains an unmodified original WebM, raw source JPEGs, capture staging originals, artifact hashes, reader events, command results, browser keydown observations, calibration samples and the analysis. It can launch only the trusted timing fixture. No arbitrary page recording, credential entry or human input lease is enabled.

## Measurement contract

- One real Tab establishes focus on the fixture button. Entering reader focus mode sends no input. Twenty actual reader `ArrowRight` commands each verify the exact exposed heading `Marker N`; all 21 input attempts/completions remain in the receipt.
- The trusted fixture displays a diagnostic binary objective/tick/checksum pattern, hidden from the accessibility tree. This is measurement instrumentation, not reader-visible semantics. An 80 ms hold preserves each marker long enough to sample; it is not an effort or difficulty metric.
- Raw `Page.screencastFrame` JSON preserves Chromium's double-precision timestamp. The SDK's single-precision timestamp model is not used. Source bytes and frame acknowledgements drain before capture stop is acknowledged.
- The first decoded video frame is anchored to its independently decoded raw-source objective/tick. Multiple matching source times widen the anchor interval. The anchor is not fitted to the twenty tested objectives.
- Seven browser-clock round trips before and seven after input calibrate the host's monotonic clock. Contradictory intervals produce Unknown. The reader's start time is bracketed separately.
- Original video frames are decoded by the FFmpeg installed with the pinned Playwright package. The recorder is pinned to its 25 fps contract. Every PNG is inspected, including first and last frames; objective/tick checksums reject ambiguous pixels.
- Each action reports measured lag and conservative absolute error. The bound includes anchor ambiguity, a full 40 ms frame interval, clock round-trip uncertainty, reader-origin bracketing and browser clock resolution. All twenty bounds must be at most 250 ms. Missing frames, invalid evidence, unverified reader goals, clock conflicts or excess error remain Unknown; none is an accessibility barrier.

The recording fixtures launch at their required 320×240 viewport, avoiding stale frames from a later resize. Originals remain unmodified. The default reader and public-page adapter still do not start recording.

## Next: recording evidence for traversal

Implemented in the subsequent [controlled traversal recording slice](0022-traversal-recording-evidence.md). Its next step is an integrated passive evidence viewer.

Attach permitted recording segments, immutable media hashes and calibrated event/frame links to the existing authored traversal receipts and node/edge reports. Start with a trusted shop fixture whose permitted visual content excludes prefilled private values. Preserve observation/input/goal uncertainty, and never infer arrival or a barrier from video alone. Clicking a report link must remain passive.

The broader command/persistence exclusion protocol, expiring input ownership, private viewer, diagnostic feature provenance, separate eligible replay results and hosted durability remain open gates. The 20-marker fixture proof does not make milestone 2 or arbitrary-site recording complete.
