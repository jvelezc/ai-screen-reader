# Recording synchronization v1 validation

October 20, 2026, local Windows: **665 discovered/executed/passed, 0 failed/skipped**. All 654 prior identities are retained; 11 cases were added. Nonincremental Release build: zero warnings/errors. Formatting verification passed. TRX: `artifacts/recording-validation/recording-sync-windows-final.trx`.

The CLI also passed independently. Run `1ed1afdba95946b18f68d0ec8416f873` measured twenty objective markers with a maximum **109.54 ms conservative absolute error**, including the declared uncertainty, against the unchanged 250 ms target. Its original media and report are under `artifacts/recording-sync/1ed1afdba95946b18f68d0ec8416f873/` locally. These generated artifacts are not source files.

| Requirement | Evidence |
| --- | --- |
| Real reader keys, observed heading goals, original video and conservative bounds for all twenty actions | `TwentyActualReaderActions_AlignOriginalVideoWithinTargetIncludingUncertainty` |
| Cancellation preserves a failed receipt without invented media or input | `CancelledBeforeLaunch_RetainsFailedReceiptWithoutInventingMediaOrInput` |
| Precise raw timestamps survive; actual acknowledgements drain; a closed source cannot restart | `RawDoubleTimestampsSurviveAndStopWaitsForActualFrameAcknowledgements` |
| Invalid protocol data is not persisted or exposed | `InvalidProtocolPayload_FailsClosedWithoutPersistingOrExposingRawData` |
| Tight clock calibration rejects contradictory or invalid evidence | `Calibration_UsesTightestRoundTripAndRejectsContradictoryClockEvidence` |
| Exact 250 ms conservative boundary passes; immediately above remains Unknown | `ConservativeBound_AtTargetPassesAndImmediatelyAboveRemainsUnknown` |
| Missing frames and unverified goals preserve uncertainty | `MissingFrameOrUnverifiedGoal_DoesNotBecomeAccessibilityBarrierOrInventedFrame` |
| Independent baseline and ambiguous source matches retain uncertainty | `BaselineIsIndependent_AndWideDuplicateAnchorRetainsItsUncertainty` |
| Invalid clock/actions/frames and out-of-interval keydown do not verify | `InvalidClockActionsFramesOrEventInterval_CannotProduceVerifiedTiming` |
| Reversed clock, overlapping actions and invalid visual codes remain Unknown | `ReversedSourceClockOverlappingActionsOrInvalidMarkerCode_RemainsUnknown` |

The initial .NET attempt correctly rejected raw keys in browse mode. The probe now uses actual Tab focus and explicit focus mode; it does not inject focus or weaken reader policy. A stale frame from resizing the original 1280×720 viewport was also exposed. Recording fixtures now launch directly at 320×240, and image dimensions/checksums remain strict.

Windows and Ubuntu CI run both the tests and the CLI synchronization probe on the corresponding main push. Independent Linux public-page isolation remains a separate regression. No local Linux run, deployment or fresh assistive-technology validation is claimed for this increment. The prior native-decoder repair passed all 654 tests on both CI platforms and independent isolation in run `39474964008`.

CI39542303437 passed all five Linux jobs, with Ubuntu1,186/1,186 and all demos/packaging, while Windows passed1,185/1,186. Every native-shutdown test passed, including the injected stalled-callback proof with real driver stop and kernel exit confirmation. The sole failed test was TwentyActualReaderActions_AlignOriginalVideoWithinTargetIncludingUncertainty: analysis returned Unknown instead of Verified. The assertion had lost ReasonCode and numeric timing/frame evidence, and the detailed fixed-fixture receipt was written below the test output directory outside the foundation artifact upload. No native or timing cause is inferred from that code-only failure.

The failure assertion now retains only its typed synchronization result, clock samples, reader-origin uncertainty, numeric action markers, frame counts and objective counts. CI separately uploads only the fixed synthetic recording-sync-tests receipt.json files. Images, videos and network headers are not added to this diagnostic artifact. The250ms conservative target, all twenty actual actions, original media decoding and Unknown preservation remain unchanged; there is no retry, skip or threshold relaxation. Replacement six-job CI remains required before the private viewing client.

Diagnostic follow-up local Windows validation passed1,186/1,186, zero failures/skips, all prior identities retained. Receipt: artifacts/recording-validation/timing-diagnostics-full.trx. Nonincremental Release build and formatting verification passed. One separately owned single-CPU recording run also verified all twenty actions, with maximum conservative error82.418ms and uncertainty42.625ms; receipt: artifacts/recording-sync/49867e8a96a94da2abe9bb56bd9627dd/receipt.json. This bounded scheduling probe did not reproduce the CI failure and does not establish its cause or a general performance guarantee. Replacement CI must retain the actual ReasonCode before any timing-rule change.

CI39544123526 retained the new JSON-only timing artifacts on both operating systems. Windows verified twenty actions with120.658ms maximum conservative error and42.551ms uncertainty; Ubuntu verified91.879ms maximum error. The overall CI run was still failed by two independent Windows shutdown checks, not by this timing probe. No cause is inferred for the earlier unrecorded Unknown result, and the250ms target remains unchanged.
