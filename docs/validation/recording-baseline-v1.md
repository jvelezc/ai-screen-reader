# Independent baseline synchronization repair

November2,2026. CI41534998348 for7784c56 passes eight jobs, including Ubuntu3613
tests, native73, database631, operational180 and both native-output stream proofs.
Windows discovers3613, passes3612 and fails
`TwentyActualReaderActions_AlignOriginalVideoWithinTargetIncludingUncertainty`.
The failed result is retained as Unknown; it is never accepted by a retry or
relabelled as an accessibility barrier.

## Observed cause and correction

The first original-video frame contains baseline objective0/tick58. Independent
raw-source capture did not observe tick58. Video frames1 and2 contain baseline
tick59; raw source observes that exact marker at1793658109324.989 and
1793658109341.834 milliseconds. The previous analyzer examined only video frame0.
Consequently it returned `RECORDING_ANCHOR_NOT_OBSERVED` despite a later shared
baseline. Neither original nor raw-source frames are changed.

The analyzer now selects the earliest original-video baseline marker independently
present in raw source. Its actual CFR presentation offset is subtracted to derive
video time zero. Every source timestamp for the selected marker remains in the
ambiguity interval. A later narrower candidate cannot replace the earliest shared
baseline; action markers never choose or fit it. Missing shared baselines remain
Unknown. Clock validation,20 actual reader actions, goal verification,40ms frame
quantization and the conservative250ms limit are unchanged.

The embedded JSON regression retains the failed synthetic CI clock/action/source/
video metadata and its run identity58607f5e82944bc5948a96c83864c3dc. It contains
no original media or application account data. Three new deterministic regressions
fail against the previous analyzer. After the correction, all21 selected
synchronization cases pass, including actual Chromium. The full Windows workspace
discovers/executes/passes3656, failed/not-executed0, in8m48s. This includes39
uncommitted public-website API cases; the correction itself adds four cases to
the3613 main baseline. The nonincremental Release build has0 warnings/errors and
required format verification is clean. Public-website API work remains a
separate source increment; current correction delivery receipts follow below.

| Requirement | Evidence |
| --- | --- |
| A later independently shared baseline can establish video time zero | `LaterSharedBaselineAnchorsFrameZeroWithoutFittingActionMarkers` |
| Matching action data cannot invent a missing baseline | `ActionMarkerMatchesCannotReplaceAMissingSharedBaseline` |
| The earliest baseline retains all ambiguity, without selecting a convenient later match | `EarliestSharedBaselinePreservesAmbiguityInsteadOfChoosingASharperLaterMatch` |
| Exact failed Windows metadata remains within the existing250ms target | `FailedWindowsCiMetadataUsesItsIndependentLaterBaselineWithinTheUnchangedTarget` |
| Exact target passes and immediately greater error remains Unknown | `ConservativeBound_AtTargetPassesAndImmediatelyAboveRemainsUnknown` |
| Actual native reader/video behavior retains all20 objectives and original hashes | `TwentyActualReaderActions_AlignOriginalVideoWithinTargetIncludingUncertainty` |

Corrected source366e9c5 first runs in CI41539963824: Ubuntu passes3617/3617,
while Windows records two text-fixture startup timeouts; the recording cases
pass. Focused text cases pass4/4 locally. An unchanged-source full dispatch,
CI41543636420, then passes all9 jobs and3617/3617 on each OS. No deadline,
assertion or source is changed between those CI runs. The startup cause remains
unproven and the failed receipts remain retained.

Private promotion41545751253 is Ready for366e9c5. All165 files/4009378300 bytes
independently match manifest4c7f79c78f5eafffa1c772246b2fe6d38cbe6302a859a6610dea0ad9bacc6935.
The immutable rootless image, current release and both active services match;
owned containers/networks are0. Fixture/public/control readiness passes without
database persistence or original-account acceptance. Automatic promotion is held.
This repair does not establish a new original-account hosted acceptance,
general-site recording alignment or actual assistive-technology acceptance.
