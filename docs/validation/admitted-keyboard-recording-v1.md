# Admitted keyboard recording validation

October 28, 2026. See [contract 0060](../decisions/0060-admitted-keyboard-recordings.md).

| Requirement | Evidence |
| --- | --- |
| Real keys, graph, original media and independent route outcome | `ActualAuthoredKeyboardRouteRetainsOriginalMediaGraphAndIndependentOutcome` executes all four routes in real Chromium, decodes original video and reconstructs five-file archives. Reached/2 keys, disabled/0 keys, validation Unknown/3 keys and unchanged Unknown/4 keys remain distinct. |
| Reject cross-scope, invented or altered original evidence | `BoundKeyboardOriginalsRejectChangedScopeMediaGraphAndTiming` rejects tenant/generation/route/plan, media/source hashes, escaping source names, missing frames, duplicate/invented graph entries, key events, capture closure, decode, timing and collector mutations. |
| Exact authenticated admission and no caller-defined actions | `HttpAdmissionPreservesExactAuthoredRouteAndOriginalCaller`, `UnknownRouteOrCombinedPassiveRequestCannotAdmitInput` and `ProviderKeyboardAdmissionUsesOnlyFixedRpcRouteAndOriginalScope`. |
| Reconnect retains actual Unknown graph independently of baseline | `AdmittedKeyboardGraphOriginalSurvivesHttpAndStdioReconnectWithoutPromotingUnknown` reconstructs original bytes through actual HTTP/private stdio, verifies full SHA, three completed keys/edges and Unknown, then denies revoked authority. Its identity provider is a test implementation, separate from hosted JWT acceptance. |
| Actual native attempt and cleanup before fenced publication | Linux harness cases `RealAdmittedKeyboardWorkerRetainsRouteAndActualAttempt`, `KeyboardBaselineCannotReplaceActualInputAndOutcome`, `KeyboardArchiveRetainsFiveOriginalsAndCollectorIdentity`, `KeyboardGraphRetainsActualSourceActionResultAndCompletedKeys`, `KeyboardRecordingRetainsOriginalFramesMeasuredTimingAndClosedCapture` and `KeyboardContainerAbsentBeforeCompletion`. |
| Database roles, route-safe retry and legacy deferral | `tests/database/keyboard-recording.sql` contributes 27 named cases to the actual PostgreSQL/pgmq suite. All **275/275** aggregate cases passed in an owned disposable Linux container, which was removed. |
| Original consumer verification and reconnect lifecycle | UI tests `verifies all five originals and preserves the actual route graph input and outcome facts`, `clears a loaded admitted keyboard graph and owned video on same-workspace reconnect`, `aborts an in-flight admitted keyboard original read on reconnect`, and `freezes the selected authored keyboard route across an uncertain admission retry`. |

The hosted additive migration was applied through Supabase MCP and read back as
20261028092549. Membership is required for admission; anon cannot admit, members
cannot claim and the worker can claim. Private helpers remain revoked. The
private evidence bucket remains 25 MiB with no MIME restriction.

Local notebook validation passes **437/437**, frozen install, typecheck,
production build and lint with zero errors (six existing Fast Refresh warnings).
Final full Windows TRX: **2,368 discovered/executed/passed**, zero failures or
not-executed cases. Strict nonincremental Release build has zero warnings/errors;
full format verification passes. Native Linux, Windows/Linux CI, exact deployment and actual
original-user graph/video/reconnect acceptance are separate gates. Their final
receipts belong below after successful runs; a passing test is not a substitute
for the original-user hosted recording or a human/NVDA comparison.

## Remaining product gates

The website-evidence assessment still evaluates supplied scanner records. Bind
event assertions to actual admitted recording bytes before claiming that a scan
automatically supplies a sufficient website package. Broader hosted restart,
storage-outage and real revoked/expired authority acceptance remain open.
Arbitrary-site collection, remote human intervention and assistive-technology
acceptance remain separate. Disability, personal injury, standing, custody and
legal admissibility are outside the automated website-side decision. No human
review task is added, and public MCP/plugins remain deferred.
