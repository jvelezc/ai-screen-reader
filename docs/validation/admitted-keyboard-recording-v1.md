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

## First delivery and hosted timing finding

Core `4b96372` passed all nine jobs in
[CI40665140483](https://github.com/jvelezc/ai-screen-reader/actions/runs/40665140483):
Windows and Ubuntu each discovered/executed/passed **2,368**, zero failed or
not-executed cases. Native Linux passed **45/45** and the database **275/275**;
both owned test images/databases were removed. Exact private
[deployment40667311888](https://github.com/jvelezc/ai-screen-reader/actions/runs/40667311888)
and all 151 independent file hashes passed. Default-trust HTTPS publishes 1.8.0;
anonymous session 401 and private root/MCP/config paths 404 remain in force.

UI `055c060` passed 437/437 on Windows and Ubuntu in
[CI40664585722](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/40664585722).
Existing Edge v5 matches its three reviewed files. The original saved account
admitted route 1 through notebook → Edge → core. Scan
`b86dd5b3-f0e7-496b-9545-beb5dbb777ce`, job/archive
`e47c4fd3-ac04-43ee-86ec-239150007bf5`, generation 1 and actual worker
`5c4606f8-d948-48e5-9677-f401e8613242` agree with the fenced database receipt.
Index `c4d1232b5767a2d2efeb960aa8ed8321d5f4f82b9f9f7ab578936e7c8f5cdc60`
independently reconstructs **five files, 439,717 bytes** from private S3.

The notebook verified all five originals, displayed three states/two edges/two
completed keys/Reached independently of the zero-input baseline, and played the
actual 1.6-second original to its end. All five actual downloads verify; all 18
source JPEG hashes match their original ZIP. The freshly authorized report is
16,165 bytes, SHA256
`3427dffa4681abc28ba58c3d082b3b0bb5a688956ee67a8ed6919ce654b8e226`.
Independent rootless Podman inspection found no owned observation container.

Timing correctly stayed **FRAME_EVIDENCE_INVALID**, so synchronized selection
was not accepted. The original metadata retains a 4.113 ms startup reversal in
CDP delivery versus presentation order. [Decision0061](../decisions/0061-recording-presentation-order.md)
records the explicit presentation-index repair, unchanged raw originals and its
separate fresh deployment/account acceptance. The first package is never rewritten
or retrospectively labeled calibrated.

## Presentation-order repair validation

The final Windows run after decision0061 discovers/executes/passes **2,373**,
with zero failures or not-executed cases. The strict nonincremental Release build
has zero warnings/errors and full solution format verification passes.
`PresentationIndexRetainsReversedRawDeliveriesAndCalibratesWithoutChangingTimestampsOrOutcome`
also passes independently. The first focused attempt caught an incorrect test
expectation of Reached for an intentionally Unknown receipt; the assertion was
corrected to preserve Unknown before the clean full run.

The native harness now requires measured calibrated timing within the unchanged
250 ms target and retention of the explicit original-frame index. Fresh CI,
exact promotion and original-user calibrated seek/reconnect remain separate
gates. The [architecture plan](https://chatgpt.com/space/page_9a835b55f9808191a2accbc9900a3e25)
is the canonical latest delivery receipt; earlier named records above retain
their original source and measured limits.

## Remaining product gates

The website-evidence assessment still evaluates supplied scanner records. Bind
event assertions to actual admitted recording bytes before claiming that a scan
automatically supplies a sufficient website package. Broader hosted restart,
storage-outage and real revoked/expired authority acceptance remain open.
Arbitrary-site collection, remote human intervention and assistive-technology
acceptance remain separate. Disability, personal injury, standing, custody and
legal admissibility are outside the automated website-side decision. No human
review task is added, and public MCP/plugins remain deferred.
