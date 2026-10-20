# Original recordings attached to authored traversal

The four authored shop routes now have separately opted-in original recording evidence. Run:

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --traversal-recording
```

Open the printed index. Each route has JSON, a directed SVG trace, an equivalent Markdown report, and its own media directory. Selecting a recorded node opens its capture evidence; selecting an edge opens the action interval. Links in the report open the corresponding original WebM interval. These are passive file links and never issue live browser input.

## Evidence contract

The recorded shop is a separate trusted fixture without editable fields or private canary values. The original unrecorded traversal fixture remains unchanged. Neither the public browser adapter nor normal sessions gain recording capability.

The runner retains the monotonic origin and frequency, brackets construction of the reader clock, and brackets each successful accessibility capture. The retained snapshot time is separate from its acquisition interval. Attempt windows already enclose actual reader commands; event windows include the reader-origin bracket. A canceled or failed acquisition does not create a result observation.

Seven clock round trips before and seven after the route calibrate host and browser clocks. The first original video frame is independently matched to its raw-source diagnostic tick/checksum, hidden from the reader. Multiple matches widen the anchor interval. The mapper retains clock, anchor, browser-resolution and full 40 ms frame uncertainty. Timing uncertainty above 250 ms produces Unknown. The video and source frame identities are not used to verify checkout or infer a barrier.

Each cue is Covered, Partial, OutsideClip or Unknown. Missing capture windows never become atomic frames inferred from retention time. Out-of-clip cues have no invented frame numbers. All cue types preserve the route's independent destination and barrier assessment. Source and result observations remain separate even for repeated states.

The probe waits for an actual SDK video callback before a route may start; its single-precision timestamp is ignored. Baseline/trailing padding is measurement time outside the route duration and is not a human-effort metric. Collectors stop, pending source writes drain and the producer closes before native decoding and hashing. Every original/staging video and source JPEG is inspected. Reports bind the run, document and original-video hash, recheck every artifact before publication, and refuse missing, changed or escaped paths. Existing reports and pending files cannot be overwritten.

Failures preserve the stage and sanitized code alongside the actual route receipt, if execution reached that point. No original recording is claimed when finalization or inspection did not complete. Partial originals remain in the owned media directory. Timing failure does not turn completed input into success or a system error into an accessibility barrier.

This is a bounded synthetic recording path. Complete reader-command/persistence exclusion, credential handling, human takeover, private remote viewing, arbitrary-site recording and hosted durability remain separate gates.

## Next: passive evidence viewer

Build a local accessible HTML report with one original-video player and selectable graph nodes/actions/events. A selection should identify its source, actual result and verification, seek only the local recording, and highlight the corresponding timeline interval. Include keyboard operation, unavailable/partial media states, and a readable equivalent table. It must have no browser-control endpoint, replay action, external dependency or inferred diagnostic label. Validate it in real Chromium and preserve the original media hashes.
