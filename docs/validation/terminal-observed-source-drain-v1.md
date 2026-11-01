# Terminal observed-source drain v1

Status: 23 focused and 3,112 full Windows checks pass, zero failed/skipped; strict
build and format pass. New CI, exact private delivery and fresh original-account
route acceptance remain pending.

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

## Next acceptance gate

Complete both CI platforms and the other required jobs. Promote
only that exact successful artifact, independently verify all installed files,
then admit distinct original-account checkout-preview and unchanged-feedback
recordings. Match all originals, graph/action links, input/goal facts and actual
native playback. Keep old failed attempts and raw originals intact. No source
limit increase, sampling, replay, successful goal or Ready package follows from
partial media. Wider fault/provider/website/intervention/operations/human/AT gates
remain in the [ordered plan](../completion-plan.md).
