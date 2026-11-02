# Terminal observed-source drain v1

Status: delivered at `6488605`; all nine CI jobs pass, with 3,112 tests per OS.
Exact private delivery and all four fresh original-account routes pass independent
storage/native checks. Actual checkout/feedback graph and native playback pass;
the complete282-frame feedback movie explicitly retains invalid calibration.
That separate [timing gap](long-video-timing-v1.md) is delivered at5053852 with
declared controlled long-video acceptance. Older failed attempts and the stored
uncalibrated recording remain intact.

## Exact live baseline

Core7fd3fc546b32723e5b39ecc0c2f2302322703cb7 passes all nine jobs in
[CI41353035479](https://github.com/jvelezc/ai-screen-reader/actions/runs/41353035479):
3,106 discovered/executed/passed per OS,0 failed/skipped, native71/database461/
operations172. [Deployment41354703343](https://github.com/jvelezc/ai-screen-reader/actions/runs/41354703343)
reports Ready and all151 CI/archive/installed files independently match, including
manifest10e7412a83b084c92a163dc06976cec02d313542026a908d98d06d40b26f70b5
and the actual dedicated rootless image. Trusted TLS, API1.14 guards, native
absence, unfrozen services and Restart=no pass. UIc614141/existing Edge11 are
unchanged. The original saved application session restores without another login.

Three distinct original-account recordings complete on the same producer
db736067-9c62-40dd-a1ae-347081bc5857, with one gen1 attempt each and0 remaining
leases. Every ZIP part and all five S3 originals independently match.

| Route / scan | Source / decoded frames | Retained outcome | Actual account consumption |
| --- | --- | --- | --- |
| Disabled2 / b2ca42eb-6b31-4bd2-a3ef-cbe6630ab12d | 151 /187 | NotReached, Observed barrier,0 keys | Five originals,4 states/3 edges, zero-call Blocked activation and7.48second native playback to Ended |
| Distinct disabled2 repeat / b7fbf120-3b72-4495-9069-d062940c25f9 | 94 /118 | NotReached, Observed barrier,0 keys | Independent private/S3 readback; separate run, same healthy worker |
| Validation3 / b035b7c5-4b26-45d5-aae1-6efc5dd0aa0e | 154 /183 | Unknown goal, NoneObserved barrier,3 actual keys | Five originals,4 states/3 edges, Enter completed1 call with TimedOut goal;7.32second native playback to Ended |

These hosted clips are shorter than10seconds. The actual >10second native control
passes separately on Windows and Ubuntu; it does not manufacture a longer hosted
clip or prove the precise earlier failed exception. The failed3e635654 attempt
still retains238 exact images/3,201,358 bytes. Original-account source238 download
matches13,460 bytes, hash, original timestamp/job/producer; no recording is inferred.

## New preserved failure

The original account then admits checkout1 as scan9c3d8e99-6dcf-4b76-bd5f-122d9c1fd899,
request41a79644-c164-4459-a262-e789f1454b25, jobe8b3cf56-4afb-4a56-aca4-e02d4267628b.
It blocks `WORKER_PROGRESS_RECORDING_CAPTURE_BUDGET_EXCEEDED`, one gen1 attempt,
same producer,0 leases, no result/reference/index/manifest/archive. All250 originals
match the private source manifest, totaling3,367,671 bytes over a13.238338second
presentation-timestamp span; native absence is independently verified.

The retained journal has5 records,2 intents and2 results, no uncommitted write,
CanReplayfalse and no completion receipt. The last Activate has1 actual key and
Observed/Satisfied checkout-preview facts. Those local facts cannot promote an
archive or overall verified goal from this failed recording. The attempt remains
unchanged; the new implementation must be tested with distinct future runs.

## Reproduction and local fix

The compiled real-Chromium regression fails on the old code with
“The final source transfer ran before the terminal observed checkpoint.”
The final Observed source copy now runs in the existing joined final drain;
the actual checkpoint is retained first. No limits or public contracts change.

The 23-case focused Windows run discovers/executes/passes 23, zero failed/skipped.
It covers actual four-route recording, original integrity and closure, terminal
transfer ordering, intermediate checkpoint ordering, failure/cancellation and
default count/byte boundaries. Full Windows Release independently discovers,
executes and passes 3,112 tests, zero failed/skipped, in 8m25s. Strict build has
zero warnings/errors, full format exits zero, and the TRX independently confirms
all six new cases, the 45 decoder cases and actual >10second native control.

Clean commands, using repository-pinned SDK 10.0.402:

```powershell
dotnet build HeadlessScreenReader.slnx -c Release -warnaserror
dotnet test HeadlessScreenReader.slnx -c Release
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
```

| Requirement | Evidence |
| --- | --- |
| Retain terminal observation before final transfer; stop acquisition before its storage delay | `TerminalObservedCheckpointPrecedesTheFinalOriginalTransfer` |
| Preserve earlier checkpoint source-transfer ordering | `IntermediateObservedCheckpointStillReceivesItsCompletedOriginalsBeforeAcknowledgement` |
| Failed/cancelled terminal checkpoint cannot claim media or begin final transfer | `TerminalCheckpointFailureCannotStartTheFinalTransferOrClaimARecording` |
| Preserve exact250-frame/16MiB default admission and deny the next byte/frame | `DefaultFrameAndByteBudgetsRemainClosedAtTheirDeclaredBoundaries` |
| Retain every acquired original and actual route classification through closed native capture | `FinalOriginalTransferCannotExtendActualSourceAcquisitionAfterTheRouteEnds`, `FourOriginalRecordings_LinkRealCaptureAndInputIntervalsWithoutChangingRouteClassification` |

## Verified delivery of the terminal drain

[CI 41360294468](https://github.com/jvelezc/ai-screen-reader/actions/runs/41360294468)
passes all nine jobs for `6488605b336077ba7fa3c24964878066b4a1c4e7`. Downloaded
Windows/Ubuntu TRX each independently confirm 3,112 discovered/executed/passed,
zero failed/skipped, including all six new cases, 45 PNG reader cases and the
actual >10second native control. Native 71, database 461 and operations 172
receipts independently pass; these remain separate from local Windows checks.

[Deployment 41362034751](https://github.com/jvelezc/ai-screen-reader/actions/runs/41362034751)
is Ready, run `c5c6992ca7a643e7b715f39b77b47355`, previous release `7fd3fc5`.
All 151 actual CI archive and installed files independently match manifest
`51e3f9a41bbe1efbe9f61decddd112c8e3d5069ecb7ec3501278cdd158e302b1` and the
dedicated rootless image
`sha256:e28cd71a8ac1396c376dd2e401ab555ba431ca6784bac2c62481f919258e3510`.
Default TLS, HTTP 1.14 contract/guards and zero native containers pass. Worker PID
3249346/invocation `f3be32812eb14ef5b48eb80a20d58a88` and control PID
3249380/invocation `6fba77df1aed42aeb6e13afc4e37a3c0` remain healthy, unfrozen and
Restart=no. Existing Edge 11/UI c614141 are unchanged.

## Fresh original-account checkout

The same saved original account admits scan `1899dd8b-53d9-454a-859c-60afdc4b2041`,
request `7dad6902-a070-407c-9bb4-1061a1bc08b6`, job
`4d2a821a-b95e-46d1-847b-59b3f9a43b66`. It completes in one generation-1 attempt,
producer `e20aed0b-8c00-4eee-9535-f24049713ebc`, zero remaining leases. Capture is
Closed with 116 exact source images, 154 complete decoded frames, three states
and two edges. Both actual keys complete; reachability is Reached and barrier
NoneObserved. Native absence and the same healthy service invocations independently
pass after collection.

Every ZIP part, all five S3 originals and the separate baseline independently
match. Archive size is 1,969,383 bytes, index
`8b014d9254b23999683a202662395c15a358b7453e015129995458c99d49088e`, manifest
`83d622aa3ebc3f240e9e5488718a7583db96dd2985aedc7dcfefd01ee03757dc`, baseline
`cb7cb59fe034ffb1f016c292e71cc5ceb082efa68d436dec1f2bfbfa18fca377`. The
77,311-byte original video hashes
`d4590ad93c8107ae309a703fdcc44653e71a2f083d3c15462da4145a4c5f3471`.
The original account verifies all five files through existing Edge/core, then
opens the actual three-state/two-edge graph. Selecting a2 shows one completed
Enter, Observed/Satisfied, complete input evidence and no uncertainty flag.
The native original loads at readyState4, seeks to zero, plays, pauses/resumes
and naturally ends at 6.16 seconds with no media error. This is agent-operated
consumer acceptance, not human usability or actual AT speech.

## Fresh unchanged-feedback recording

Scan `00bc1d24-2ffb-4d52-8e4a-4586a5de7eb5`, request
`ed1068ba-fc8b-4b74-993d-c67517fcbc0e`, job
`f43dacfa-5a13-4b0c-8aa1-2de3887cbdf4` completes in one gen1 attempt, the same
producer/service invocations and zero remaining leases. All 240 source files and
282 complete decoded video frames are retained. Four actual keys complete across
five states/four edges; the destination stays Unknown and barrier NoneObserved.
Capture is Closed, native count zero. No input is replayed and no resource limit
is increased.

All five S3 originals, every stored ZIP part and the separate baseline independently
match. Archive size is 3,750,097 bytes, index
`acb3cc6ce4e53f1040a4c38eb15df4f7e44d4ccf2dacb2d01abceddb573827fb`, manifest
`93826757ceebd93906db4eab3c30adf3ecad3ebed036b0efea3c256e00dd54a1`, baseline
`91c276767319b4e8a485f4f40ef11fa863d21f23ce5624b6683150ac9fc8eac7`.
The 89,522-byte original video hashes
`7106b5e29708ede58f0d45e716f3ff26c9dc8cb95dbfff804bb32c8d112067d3`.
The original account verifies all five files, reads the five-state/four-edge graph
and selects a4. Its one actual Enter remains Unknown/TimedOut with complete input
evidence and the conservative input-may-have-occurred flag. The complete original
loads at readyState4, plays from zero and naturally ends at11.28seconds/errornull.
Its stored timing remains FRAME_EVIDENCE_INVALID, null anchor/uncertainty and no
media cues. This is complete-media playback, not calibrated graph/video timing.

Two further distinct routes complete on the same producer/service invocations,
one attempt each, zero leases and independently confirmed native absence. Disabled2
scan7675959f-a4b8-4996-9217-53430b6ba842/job57c0a588-bcda-4394-be3d-d3e35fa4bfbf
retains140 source/174 decoded frames, NotReached/Observed and0 actual keys.
Validation3 scan35a8e8f5-1d16-45d2-98d5-a4327f2d7b5f/job46f36fb6-00ca-42bc-b0b8-f0f0e0503bfc
retains137/168, Unknown/NoneObserved and3 actual keys. All five originals, every
stored ZIP part and separate baseline independently match for both. This does not
claim separate original-account playback of these two new runs.

The old failed checkout is independently reread after these new runs: all 250
originals/3,367,671 bytes and their first/last hashes still match the original
private manifest, native absence is confirmed, and CompletedRecording/CanReplay
remain false. Its partial images do not become a new recording or successful goal.

## Next acceptance gate

The exact CI/release and four-route original-retention gates pass, including
actual checkout/feedback consumer playback. Subsequent [long-video timing](long-video-timing-v1.md)
passes at5053852 with fresh controlled graph/video acceptance beyond the old
boundary. Next is [scoped full-staging-disk failure and recovery](hosted-full-staging-disk-v1.md).
Keep genuine ambiguity, old failed attempts and raw originals
intact. No source
limit increase, sampling, replay, successful goal or Ready package follows from
partial media. Wider fault/provider/website/intervention/operations/human/AT gates
remain in the [ordered plan](../completion-plan.md).
