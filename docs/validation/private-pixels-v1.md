# Private pixel relay validation

The pixel increment adds 72 cases: 68 deterministic/domain/owned/actual-HTTP cases and four real Chromium cases. Nineteen test methods contain 114 assertion sites. Existing broker behavior was preserved by a private generic delivery extraction, with all 44 existing broker cases passing before the pixel entry point was added. No package versions or layer dependencies changed.

| Requirement | Evidence |
| --- | --- |
| Envelope dimensions, formats, length and exact limits, separate from decoding | PixelEnvelope_RejectsUnsupportedDimensionsFormatsAndBounds; PixelEnvelope_AcceptsRgbAndRgbaAtTheExactByteCeilingWithoutClaimingDecode |
| Source scan/epoch/ordinal/document/envelope binding before publication | PixelBroker_RejectsMisboundPayloadBeforeAnyDelivery |
| Identity, exact scope, connection, secret, monotonic expiry and revocation before source | PixelBroker_RequiresTheSameScopedMonotonicAuthorityBeforeSource |
| Semantic/pixel reads share busy admission and actual delivery drain after cancelled revoke waiter | PixelAndSemanticReads_ShareOneConnectionBusyFenceAndActualRevokeDrain |
| Late noncooperative source cannot publish after revocation and remains actual work | PixelRevocation_DropsLateSourceBytesAndJoinsNonCooperativeActualCapture |
| Unowned, missing baseline, excluded, stale and closed ownership deny before native capture | OwnerPixels_DenyUnavailableOwnershipOrBaselineBeforeNativeCapture |
| Acknowledged baseline binding, no pixel evidence write or input dispatch | OwnerPixels_BindAnAcknowledgedBaselineAndNeverWritePixelPayloadOrDispatchInput |
| Actual capture after cancellation joins exclusion drain and cannot publish in the gap | OwnerPixelCapture_JoinsActualWorkAfterCancellationAndBlocksPublicationAcrossExclusion |
| Adapter document/epoch/envelope mismatch cannot publish | OwnerPixelCapture_RejectsAdapterBindingMismatchWithoutPublishing |
| Actual HTTP bytes, binding/private headers, revoke/reconnect and optional OpenAPI | ActualPixelHttp_DeliversBoundPngWithPrivateHeadersThenRevokesReconnect |
| Client context, scope, credentials, malformed epoch/query/body denied before source | PixelHttp_DeniesClientScopeOrMalformedRequestBeforeSource |
| Actual routing denies mutation, keyboard, pointer, clipboard and browser-control paths | PixelRelay_RejectsMutationClipboardAndControlPathsWithoutDispatch |
| Default host has no pixel endpoint | PixelEndpoint_IsAbsentUnlessHostExplicitlyEnablesIt |
| Actual concurrent HTTP revoke waits noncooperative source and drops late bytes | ActualPixelHttpRevocation_WaitsActualSourceAndDropsItsLateBytes |
| Controller forwarding denies adapters lacking the optional capability | ControllerPixelForwarder_DeniesAnAdapterWithoutTheOptionalCapability |
| Ordinary Chromium launches, including synthetic fixture, have no pixel consent | DefaultChromiumFixture_DeniesPixelsWithoutSeparateHostOptIn |
| Actual native epoch/document and exclusion fences | OptedInNativePixels_RejectExcludedStaleEpochAndChangedDocumentBeforeDelivery |
| Delivered bytes independently decode; actual focus/value/counters, positive red control, gap/epoch/revoke and owned cleanup | ActualChromiumAndHttp_PixelsDecodeCorrectlyWithoutViewerInputAcrossExcludedSyntheticGap |

Focused Windows validation passed 72/72, zero failures/skips, before final full regression. Real CLI proof at artifacts/private-pixels/ee47a084bb8148969971cfcf0bdaed7d/index.md passed all pixel/input/gap/epoch/revocation/closure gates. It retained only hashes and color counts; actual bytes decoded to 67,874 green pixels before and 68,066 blue pixels after, with zero red in either delivered image. Per-run tick rendering can change byte hashes and counts.

Local Linux/Podman acceptance passed at artifacts/headed-linux/42eeea3b262d48108fa88c42c97c4ad7/runtime.json. Actual headed pixels, exact process/context/target/document continuity, private Xvfb/kernel restrictions, server/browser/display exit, evidence copy and matching container/image removal passed. The failed first harness receipt remains at artifacts/headed-linux/93432f0acb134168a5b10b3f1cb59710: the proof itself passed, but missing LF terminators in the new display-number/pid files caused the shell cleanup checker to exit. Adding the existing line-file convention repaired the actual harness gate. No local Linux full-suite claim is made.

Development also caught an omitted pixel-capability forwarder in the controller adapter; the owned-source tests now cover that composition. A pending deterministic test was aborted explicitly after a second build hit its file lock; neither aborted/partial result is acceptance evidence. All later focused cases executed. A two-second dual-stack connection probe also failed to confirm server closure; the proof now uses the existing IPv4 native-connection-refused witness with a separate five-second bound and preserves unsuccessful receipts. Controller/capture ownership deadlines remain unchanged.

Final Windows validation: **1,157 discovered/executed/passed, zero failed/skipped**. All 1,085 prior identities remain, and discovery/TRX sets match. Nonincremental Release warnings-as-errors build and formatting verification passed. Receipt: artifacts/recording-validation/private-pixels-final-full.trx. Remote six-job CI acceptance is pending. The Windows build is restored after Linux publish. No browser UI, remote identity-provider, actual human/AT, deployment or Claude acceptance is claimed.

The first pixel push, CI39532356790, passed Ubuntu 1,157/1,157 plus all three headed Linux/Docker scenarios and public isolation. Windows passed 1,152/1,157: five shutdown-related failures kept the overall run failed. The [shared shutdown-budget repair](browser-shutdown-v1.md) retains the four-second total native and five-second outer ownership bounds; its six additional deterministic cases and safe timeout/rejection facts have separate acceptance. Pixel proof receipts now include safe Shutdown metadata. Post-repair local Linux/Podman pixel acceptance passed again at artifacts/headed-linux/29dcb8f402014103899eda0562399b98/runtime.json with browser/display/container/image cleanup. Final Windows and replacement CI results must pass before the viewing-client increment.

Post-repair Windows validation passed **1,163/1,163**, all1,157 prior cases retained, discovery/TRX identical, Release build/format clean. The six added cases exercise shared shutdown time; the pixel set remains72 cases. Real controller and pixel CLI receipts at a0fdd896e4994172a1f2dd51058ed9ac and dba31c2f06894c5497cc90203446bd54 confirmed Closed/ActiveCalls0 with no timeout/rejection. Post-repair local Linux/Podman proof29dcb8f402014103899eda0562399b98 passed actual pixels and cleanup. Remote six-job replacement acceptance remains pending.
