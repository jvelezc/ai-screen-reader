# Owned browser shutdown v1 validation

The failed Windows controller CLI in CI39521143289 is catalogued in [the decision](../decisions/0032-owned-browser-shutdown.md). Both operating systems passed 1,065 tests, but the overall run failed. Native shutdown watchdog/actual-task joining and failure receipts repair that gate; the private pixel increment remains on hold until remote CI passes.

The first repair run, [CI39525280615](https://github.com/jvelezc/ai-screen-reader/actions/runs/39525280615), also failed overall. Ubuntu passed 1,085/1,085 and all three independent Linux jobs passed. Windows executed all 1,085 tests: 1,080 passed and five failed (fixture load, recording calibration, and three unconfirmed native shutdowns). The suite previously ran two real Chromium collections concurrently. Collection execution is now sequential to remove competition between native browsers and media encoders on the same hosted runner. This is a test scheduling repair, with all assertions, explicit task-concurrency tests and production deadlines preserved. Contention is a hypothesis; the failures did not measure a common native cause. Replacement local and CI results must be recorded before this gate is accepted.

| Requirement | Evidence |
| --- | --- |
| Normal context/browser/transport order and one actual shutdown task | NormalShutdown_AcknowledgesContextThenBrowserDisposesTransportAndSharesActualTask |
| Reentrant close cannot dispatch another context close | ReentrantCloseCallback_ObservesTheSameActualShutdownWithoutStartingAnotherContextCall |
| Context escalation occurs at the exact one-second boundary and retains both actual tasks | StalledContext_EscalatesAtOneSecondAndSettlesEveryActualCallBeforeAcknowledgement |
| Synchronous/asynchronous per-context failure cannot prevent whole-browser cleanup | ContextFailure_CannotPreventWholeBrowserCloseOrTransportCleanup |
| Browser/transport failures remain explicit, sanitized and coalesced | UnconfirmedBrowserOrTransport_FaultsClosedWithSanitizedFacts |
| Browser's three-second wait timeout cannot drain actual work or recover after late acknowledgement | BrowserDeadline_ClosesTransportButCannotDrainOrRecoverActualLateClose |
| Acknowledged whole-browser close does not release a pending context call or cancelled waiter | AcknowledgedBrowserClose_DoesNotReleaseAnUnsettledContextCallOrCancelledWaiter |
| Collector failure remains failed despite acknowledged native cleanup | CollectorFailure_CannotBeErasedByAcknowledgedBrowserShutdown |
| Collector's existing five-second bound retains actual write drain and late acknowledgement remains faulted | CollectorTimeout_EscalatesNativeCleanupButRetainsActualWriteDrainAndFaultAfterLateAcknowledgement |
| Controller still faults at exactly five seconds, shows actual pending closure and never recovers on late completion | ControllerClose_KeepsFiveSecondDeadlineAndExposesActualPendingClosureWithoutLateRecovery |
| Existing production default is real Chromium/files; the internal seam preserves separate execution/cleanup outcomes and metadata without real test I/O | ControllerDemo_CoreRetainsExecutionAndCleanupFailuresSeparatelyWithoutRealIo; ControllerDemo.RunAsync and RunCoreAsync in src/HeadlessScreenReader.Cli/ControllerDemo.cs |
| Cancellation still closes the owner and remains separate from a cleanup failure | CancelledControllerDemo_StillClosesAndKeepsCancellationSeparateFromCleanupFailure |
| Actual Chromium repeated disposal joins one task and retains settled/acknowledged diagnostics | RepeatedChromiumDispose_JoinsOneActualShutdownAndRetainsSanitizedAcknowledgements |

Local Windows, October 20, 2026: **1,085 discovered/executed/passed, zero failed/skipped**. All 1,065 prior identities remain; discovery and TRX sets match. Nonincremental Release warnings-as-errors build and formatting verification passed. Receipt: artifacts/recording-validation/shutdown-acceptance-full.trx. The twenty added cases include one real Chromium case; the remaining cases use controlled native tasks, fake time and existing in-memory Domain contracts.

Final controller CLI passed at artifacts/controller/4e452340e17341689f0ed23670e7d5af/index.md, with all actual collector/context/browser calls settled and acknowledged, transport disposed, no pending calls/fault and the original controller.json schema preserved. The private-viewer CLI passed at artifacts/private-viewer/6366fe5eecf04964ad783b916c83b945/index.md; the separately opted-in synthetic input/media proof also passed. These do not replace remote CI, process-isolation acceptance or real human/AT acceptance. The repaired main push must pass every job before the next increment.

Local Linux/Podman passed both headed scenarios separately: controller continuity at artifacts/headed-linux/ad2f99dc5a2c49bf98977d911f5dcbd6/runtime.json and synthetic input/media handoff at artifacts/headed-linux/a1e7d894bbc34db48ffb7ad396a97e82/runtime.json. Each confirmed browser/display shutdown and matching container/image removal. No local Linux full-suite result is claimed. The Windows build was restored after these Linux publishes.

No deployment, real human/AT, Claude-client or remote identity-provider acceptance is claimed.

Sequential local Windows revalidation: 1,085 discovered/executed/passed, zero failed/skipped; all 1,085 prior identities preserved. Receipt: artifacts/recording-validation/shutdown-serialized-full.trx. Release solution tests built successfully under warnings-as-errors; formatting verification passed. Remote CI remains the acceptance gate.

The sequential replacement, [CI39526940276](https://github.com/jvelezc/ai-screen-reader/actions/runs/39526940276), passed all four Linux jobs, including Ubuntu 1,085/1,085, demos and packaging. Windows passed 1,084/1,085: the previous five failures passed, but `StdioSessionReplacement_RejectsOldOwnerAndStaleDocumentWithoutInput` received an unexpected error from its second fixture open. The helper had discarded the returned error code before asserting. It now parses the existing reply first and includes its tool name and error code in an assertion failure, preserving the expected result and all cases. This diagnostic change does not by itself establish the native cause or repair Windows acceptance.

The repaired gate passed at b528c98 in CI39528421432: all five jobs succeeded, Windows and Ubuntu each executed/passed1,085 with zero failures/skips, all demos/package checks passed, and both controller receipts confirmed every actual shutdown call acknowledged/settled with ActiveCalls0. Prior intermittent failures remain catalogued; this green run does not establish a common native cause. The subsequent private-pixel increment has separate validation.
