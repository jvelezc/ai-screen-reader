# Recorded evidence claim verification

Source: [decision 0062](../decisions/0062-recorded-evidence-claims.md).
The original architecture proposal under `outputs/` is unchanged.

## Behavior evidence

| Requirement | Evidence |
| --- | --- |
| File integrity cannot verify uploaded barrier or success assertions | `VerifiedFileHashesCannotVerifyUploadedActionsBarrierOrSuccessfulOutcome` |
| Complete admitted originals preserve actual facts and controlled scope | `ActualAdmittedOriginalsVerifyRecordedFactsWithoutInventingWebsiteBarrierRepeatsOrMouseSuccess` |
| Altered target, keys, focus, AX, outcome, environment, attempts and files stay insufficient | `UploadedClaimsMustMatchOriginalTargetKeysStatesOutcomeContextAttemptsAndFiles` |
| Missing/corrupt originals, authority loss, changed attempt, foreign scope and cancellation cannot release verified facts | `MissingChangedForeignOrUnauthorizedOriginalsCannotReleaseVerifiedFacts` |
| Older originals do not acquire missing context or a full AX bundle | `OlderOriginalsRetainMissingCollectorContextAndBaselineOnlyAxWithoutFabricatingCompleteness` |
| Actual Chromium Unknown survives independent HTTP/stdio reconnect and assessment | `AdmittedKeyboardGraphOriginalSurvivesHttpAndStdioReconnectWithoutPromotingUnknown` |
| Whole-original reads are bounded, read each object once and reject corruption/cancellation | `WholeOriginalVerificationReadsEachObjectOnceAndRejectsCorruptionAndCancellation` |

These Chromium and protocol tests use a synthetic provider identity. They do not
replace original-user provider/RLS, private S3 and hosted acceptance.

## Local Windows

The final source run discovers, executes and passes **2,397/2,397**, with zero
failed or skipped cases. The strict Release build has zero warnings/errors;
full solution format verification passes. The actual Chromium/HTTP/stdio and
legacy-original cases above are included. Linux, CI, deployment and actual-user
acceptance are separate gates.

## Delivery gates

Local Windows full-suite results, Windows/Ubuntu CI, exact private promotion,
Edge source version and fresh original-account reached/Unknown acceptance are
recorded separately as they pass. The architecture Page remains the delivery
index. Do not infer acceptance from a source commit or a green build.

## Remaining original-plan gates

[The completion plan](../completion-plan.md) orders durable attempt recovery,
hosted authority loss, bounded website collection, hosted intervention and real
assistive-technology acceptance, then release operations. This implementation
does not complete those gates or turn controlled fixtures into website evidence.
Public MCP and plugins remain deferred.
