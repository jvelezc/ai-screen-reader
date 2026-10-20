# Controlled capture collector feasibility

The admission protocol has a separately opted-in, synthetic fixture probe. Run:

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --capture-probe
```

It creates a unique folder under `artifacts/capture-probe/` with a readable index, JSON receipt, two original WebM segments, callback JPEGs and a dedicated `staging/` directory. Every saved media file, including staging originals, is decoded after the producer closes. Hashes identify the retained bytes. Originals are not annotated or replaced.

The probe starts collectors on the permitted green fixture, closes input/observation admission, drains work and pending frame writes, and awaits screencast stop and AX disable acknowledgements. Only then does its trusted synthetic diagnostic controller display a red viewport with visible and form-value canaries. Reader capture and keyboard dispatch must be denied. The controller clears private content before resumption; a fresh AX baseline and a new collector segment precede reopening admission. The same document and context marker must survive. A final exclusion stops the second segment.

An independent positive-control screenshot is decoded in memory to prove the canary really was visible; it is never written. This bypass is confined to the synthetic diagnostic probe, not offered to a reader or human controller. The detector checks every decoded original video frame at the pinned recorder's 25 fps and every callback JPEG for red canary pixels. Every video frame, including the first, is explicitly sought and acknowledged by `requestVideoFrameCallback`; wrong media positions and unpainted data fail validation. An independent red-first/green-last recording tests both boundaries. It is a controlled visual-canary test, not a general secret detector. Structured receipts must contain neither canary string. Failure/cancellation retains an explicit failed stage and partial evidence, without claiming inspection passed.

Trace, HAR, clipboard, network-body and typed-value logging stay disabled. Default fixture sessions do not start these recording collectors. Public-page recording, credential entry and human input leases remain unavailable. A future host must also drain complete reader commands and its payload persistence pipeline before granting sensitive human input; browser admission alone is not that host protocol.

## SDK limitations found during implementation

The [pinned .NET callback implementation](https://github.com/microsoft/playwright-dotnet/blob/v1.63.0/src/Playwright/Core/Screencast.cs) does not await frame-handler tasks. Each segment therefore has its own permanent callback admission gate and tracks actual pending writes. Stop closes callback admission before calling the SDK and waits for admitted writes. Bounded frame/byte limits and write failures prevent a successful stop acknowledgement.

The [pinned timestamp model](https://github.com/microsoft/playwright-dotnet/blob/v1.63.0/src/Playwright/API/Generated/Types/ScreencastFrame.cs) stores Unix milliseconds in `float`. Around the current epoch its spacing is 131,072 ms. These values are labeled as reported timestamps with conservative quantization uncertainty; they are not used as precise admission cutoffs or synchronization evidence. The earlier cutoff experiment discarded valid frames and was removed. No package upgrade or timing target relaxation was made.

## Next

Collect precise browser presentation timestamps independently of that `float` model. Calibrate their relation to monotonic reader events, inspect at least 20 objective action markers in original media, and report measured error and uncertainty against the original 250 ms target. Then attach permitted original segments to traversal receipts. General recording, complete command/persistence exclusion, human takeover and hosted durability remain separate acceptance gates.
