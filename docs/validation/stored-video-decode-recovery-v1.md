# Stored video decode recovery v1

The canonical isolated-search Windows rerun retains4,299 passes and one failed working collection out of4,300 tests. Trial2 reached its goal with15 completed native keys, closed capture and browser, then reported `CAPTURE_DECODE_TIMEOUT` during final original-media analysis. The unchanged31,991-byte primary and staging videos both have SHA-256 `58df87524055fbcff0b910f9d67fc846303305ad05d5aaaf552fd37a45a5448f`. Ten independent offline decodes with the pinned decoder each return43 frames in189–201ms, without website input or original-file changes. The timeout mechanism remains unconfirmed; resource pressure is a hypothesis.

The repair permits one additional invocation only after the prior decoder timeout has completed its process/stream cleanup. It snapshots the saved bytes, checks their fingerprint after each invocation, retains the existing per-invocation deadline and frame/pixel limits, and never retries invalid media, cancellation or changed bytes. A second timeout remains a failure. This stored-media recovery does not repeat a page visit, key, click or task.

Failed/recovered recording diagnostics retain artifact path, source SHA/length, attempt number, UTC start, elapsed time, closed outcome/code, original correspondence and decoded-frame count on success. Process exit is confirmed only after a successful invocation or a timeout that returned through the joined cleanup path; other failures retain unknown exit. No raw decoder error text or console output is added to the child's framed progress stream. New optional diagnostic fields are absent from unchanged historical payloads. Original input/goal observations and unverified video alignment remain unchanged.

| Requirement | Evidence |
| --- | --- |
| One retry, same private source and both outcomes | `SettledVideoTimeoutRetriesOnlyTheSamePrivateOriginalAndRetainsBothOutcomes` |
| Stop after two timeouts | `ASecondSettledVideoTimeoutIsTerminalAndNeverBecomesARecording` |
| Invalid media and cancellation have no retry | `InvalidVideoDoesNotGainADecoderRetry`; `CancelledDecodeDoesNotStartAnotherInvocation` |
| Changed snapshot cannot qualify | `AChangedDecoderSnapshotCannotBeRetriedOrAccepted` |
| Historical payload remains identical | `HistoricalMediaWithoutRecoveryDiagnosticsKeepsItsExactPayload` |
| Actual decoder timeout, process exit, retry, frame count and first/last pixels | `VideoDecoder_InspectsActualFirstAndLastFramesWithIndependentSolidColorControl`: two real decoder processes, original SHA unchanged |
| Real five-context source collections and independent bundle checks | `ActualControlledCollectionRetainsFiveFreshContextsAndAllEightyFiveControlBoundaries` |

The policy regression was executed before enabling the retry:7 discovered,5 passed,2 failed. After the repair, the unchanged policy cases plus historical/native/collection checks pass15/15, zero failed/skipped. Whole-solution Windows, format and exact-source Windows/Linux/native CI are separate acceptance gates. No new hosted deployment or external assistive-technology validation is claimed by this repair.
