# Complete-video timing v1

Status: 110 focused and 3,129 full Windows checks pass, zero failed/skipped;
strict build and full format pass. New CI, exact private delivery and fresh
original-account long-video timing remain pending.

## Delivered baseline and discovered gap

Core `6488605b336077ba7fa3c24964878066b4a1c4e7` passes all nine jobs in
[CI41360294468](https://github.com/jvelezc/ai-screen-reader/actions/runs/41360294468),
3,112 tests per OS, native71/database461/operations172. Exact151-file private
[deployment41362034751](https://github.com/jvelezc/ai-screen-reader/actions/runs/41362034751)
and all four fresh original-account routes pass their original-retention scopes.
[Terminal-drain receipt](terminal-observed-source-drain-v1.md).

The same worker produces each recording in one gen1 attempt, with zero remaining
leases and independently confirmed native absence. All five originals, every
stored ZIP part and the separate baseline independently match for each run.

| Route / scan | Source / decoded frames | Actual retained outcome |
| --- | --- | --- |
| Checkout1 /1899dd8b-53d9-454a-859c-60afdc4b2041 |116/154 | Reached, NoneObserved,2 actual keys; actual original-account graph/native playback ends6.16s |
| Feedback4 /00bc1d24-2ffb-4d52-8e4a-4586a5de7eb5 |240/282 | Unknown, NoneObserved,4 actual keys; actual original-account graph/native playback ends11.28s, calibration remains invalid |
| Disabled2 /7675959f-a4b8-4996-9217-53430b6ba842 |140/174 | NotReached, Observed barrier,0 actual keys; independent original readback |
| Validation3 /35a8e8f5-1d16-45d2-98d5-a4327f2d7b5f |137/168 | Unknown, NoneObserved,3 actual keys; independent original readback |

The feedback's verified traversal SHA-256 is
`fab7408da6c24a45ea13cf388c9ad0c37bf97f06de1ee1780ac2e36805403af6`.
Independent metadata inspection finds no invalid source timestamp/marker or
video index/40ms-position/marker. The ordered presentation index has240 entries.
The stored timing reason remains `TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID`;
its anchor/uncertainty are null and cues empty. The actual player reaches
Ended11.28s, errornull. A complete playable movie does not prove synchronized cues.
The four completed keys and Unknown goal stay separate from this timing failure.

## Regression and implementation

Six compiled positive cases fail on the old timing gates:251,282 and10,000 frames
in each analyzer. The shared existing Domain/decoder limit is named without
changing its value;71 contract/parser checks and a zero-warning strict build
pass separately. The subsequent two gate fixes use that same bound.

Reviewed green independently discovers/executes/passes110, zero failed/skipped.
It includes17 new cases,45 PNG cases, four real Chromium route cases and the
actual >10second native decoder control. No fake browser substitutes for hosted
acceptance. Full Windows independently discovers/executes/passes3,129, zero
failed/skipped, in8m41s. The full TRX pins all17 new cases, the45 PNG cases, actual
long decoder control and all six prior terminal/default-bound regressions.
Explicit strict build reports zero warnings/errors; full format exits zero.

Clean commands using repository-pinned SDK10.0.402:

```powershell
dotnet build HeadlessScreenReader.slnx -c Release -warnaserror
dotnet test HeadlessScreenReader.slnx -c Release
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
```

| Requirement | Evidence |
| --- | --- |
| Retain valid long-video cues, including late/high frame indices, through the existing10,000-frame bound | `LongCompleteVideoRetainsLateCuesWithinTheExistingDecodedFrameContract` |
| Retain the late objective's index, lag and conservative error through the same bound | `LongCompleteVideoRetainsTheLateObjectiveWithinTheExistingDecodedFrameContract` |
| Inspect malformed suffix frames after the old250-frame boundary | `InvalidSuffixBeyondLegacyVideoBoundaryCannotProduceTraversalCues`, `InvalidSuffixBeyondLegacyVideoBoundaryCannotVerifySynchronization` |
| Reject10,001 frames without cues, anchors or timing success | `DecodedVideoImmediatelyBeyondTheExistingContractCannotProduceTraversalCues`, `DecodedVideoImmediatelyBeyondTheExistingContractCannotVerifySynchronization` |
| Keep conflicting later transitions Unknown under the unchanged uncertainty target | `ConflictingMarkerBeyondLegacyVideoBoundaryKeepsTimingUnknown`, `ConservativeBound_AtTargetPassesAndImmediatelyAboveRemainsUnknown` |
| Decode an entire actual long Chromium original without altering its bytes | `NativeDecoderRetainsWholeValidVideoBeyondLegacyTenSecondCeiling` |

## Next acceptance gate

Complete all nine CI jobs and independently check both platform TRX.
Promote only that exact successful artifact and independently verify every
installed file/image. Then admit a distinct original-account long route, match
every original and input/goal fact, select the actual graph action and verify its
calibrated interval and native playback past the old boundary. Keep any genuine
clock ambiguity explicit; do not rewrite the historical recording or manufacture
precision. All source/byte/uncertainty limits remain unchanged. Wider failure,
provider, website, intervention, operations and human/AT gates stay in the
[ordered completion plan](../completion-plan.md).
