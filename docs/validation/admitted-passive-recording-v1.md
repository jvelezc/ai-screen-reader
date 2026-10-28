# Admitted passive recording validation

October 27, 2026. See [contract 0059](../decisions/0059-admitted-passive-recording.md).

| Requirement | Evidence |
| --- | --- |
| Actual original media, exclusion and zero-action state | `ActualSanitizedPassiveRecordingRetainsMediaAndOneStateWithoutInputOrGoal` uses real Chromium, decodes WebM, verifies collector exclusion/native browser close and reconstructs all four original files. |
| Cross-scope, corrupt or invented traversal cannot publish | `PassiveTransportRejectsWrongScopeIncompleteCaptureAndInventedActionEdges` rejects twelve independent mutations; exact-entry ZIP tests reject duplicate, changed-case and escaping names. |
| Original playback bytes survive authorized API/private MCP reconnect | `PassiveOriginalVideoSurvivesAuthorizedHttpAndStdioReconnectWithoutInventingTraversal` records real Chromium, reconstructs complete original bytes through HTTP and stdio after reconnect, then checks access revocation. Identity is a local test provider; this is not hosted JWT/S3 acceptance. |
| Explicit admission preserves original caller and mode | `PassiveRecordingAdmissionTests` covers boolean mode routing, malformed input and the exact fixed provider RPC with original bearer. |
| Native container admission and four-file publication | `RealWorkerProducesExplicitPassiveRecordingAfterAdmittedClaim`, `PassiveArchiveRetainsFourOriginalsWithIndexLastHashes`, `PassiveOriginalVideoMatchesDecodedCollectorReceipt` and `PassiveCaptureHasNoRemainingOwnedContainerBeforeCompletion` are wired into the Linux native worker harness. Actual run receipts are recorded below when completed. |
| Retry/fencing/legacy compatibility | `tests/database/passive-recording.sql` adds 25 named pgTAP cases, including mode-safe retries, legacy worker deferral, worker-only helpers and completion fencing. |
| Consumer verification and authority lifecycle | `passive-recording.test.ts`, `passive-recording-component.test.tsx` and `freezes explicit recording intent across an uncertain admission retry` check hashes/scope, invented edges, account loss, unmount abort and frozen retry mode. |

The isolated Linux database run passed **248/248** discovered/executed cases,
zero failures, and removed its owned database container. The additive migration
was then applied through Supabase MCP. Readback matches hosted version
20261028053837. The new authenticated security-definer admission is intentional
and checks membership; private helpers remain revoked. Existing advisor notices
for private policy-free tables, existing subject-checking functions,
`rls_auto_enable` and password-protection configuration are unchanged.

The final Windows TRX reports **2,338 discovered/executed/passed**, zero failed
or skipped. The pinned nonincremental Release build passes with zero warnings
or errors; full solution format verification passes. An earlier full run caught
an outdated MCP schema assertion (2,336 passed, one failed); it was corrected to
assert both properties and the default-false recording flag before the final run.

Local UI passes **397/397**, typecheck, production build and lint with no errors
(six existing Fast Refresh warnings). CI, native worker,
exact deployment and original-user playback are separate gates and will be
recorded here after their actual receipts pass. No new NVDA/human comparison
or legal/custody acceptance is claimed.
