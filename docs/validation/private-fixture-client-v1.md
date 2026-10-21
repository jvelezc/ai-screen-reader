# Private fixture client validation

The preceding shutdown gate passed all six jobs in [CI39548114638](https://github.com/jvelezc/ai-screen-reader/actions/runs/39548114638) at b336876. This increment adds a native scoped client and a separate display surface. No public listener, credential entry or human target-control authority is enabled.

| Requirement | Evidence |
| --- | --- |
| Bound presentation and preserve exposed text/focus facts | Presentation_BoundsNodesAndTextWithoutRenderingMarkupOrInventingFocus; Presentation_RejectsUnboundEpochAndOrdinals |
| Reject every address outside the exact loopback root | NativeClient_RejectsAnyAddressOutsideExactLoopbackRoot |
| Reject mismatched semantic/pixel bindings, media, cache policy and PNG envelope | NativeClient_RejectsUnboundOrMalformedResponsesBeforePresentation |
| Keep authorization denial, exclusion and response failure distinct | NativeClient_DistinguishesDeniedExcludedAndFailedRefreshWithoutInventingCause |
| Exact scope/headers and sequential semantic/pixel requests | NativeClient_SendsOnlyExactScopeHeadersAndJoinsSequentialReads |
| Bound actual streamed bytes with and without declared length | NativeClient_BoundsActualBytesWithAndWithoutDeclaredLength |
| Real HTTP redirect cannot forward credentials | ActualNativeHttp_RejectsRedirectWithoutSendingCredentialsToTheDestination |
| Real Chromium safely renders text; keyboard Pause clears and late renders stay rejected | ActualViewer_UsesTextNodesClearsOnKeyboardPauseAndRejectsLateRender |
| Fixed source/revision binding, CSP network denial and navigation end | ActualViewer_RejectsForgedBindingAndNetworkThenEndsOnNavigation |
| Cancelled render waiter cannot drain actual work; close joins actual callbacks | ActualViewer_CancelledWaitDoesNotDrainPendingRenderAndCloseJoinsItsActualCallback |
| Real scoped HTTP and Chromium composition, decoded pixels, unchanged target,1024/320px layout | ActualNativeClient_ComposesScopedHttpAndSeparateChromiumWithoutTargetInput |
| Real pause/resume/exclusion/revocation and both owned roots closed | ActualComposedProof_PauseResumeGapAndRevocationPreserveTargetAndCloseBothRoots |

The first focused run caught an audit DTO conversion error (5/7 passed); explicit JSON extraction repaired the adapter. A later test setup returned a function that Playwright invoked, hanging the test instead of installing the pending-render callback. The verified owned test process and descendants were terminated; its TRX is aborted and not acceptance (9 completed cases). Corrected setup passed40/40. The real redirect case and exact empty/singleton/interior/over-limit presentation partitions extend the final focused suite to44/44 in private-client-final-44.trx. The live state now updates only when text changes; the actual Chromium mutation assertion proves identical refreshes do not rewrite the live heading. A repeated scoped review returned ship for that delta.

Desktop1024px and narrow320px full-page captures were opened and checked. The fresh finish reviewer returned ship with five sections and no material fixes; the documenter recorded the inherited surface. One detector pass reported a missing image src (intentionally hidden until memory-only permitted pixels) and flat hierarchy (the scanner cannot resolve the hash-inlined resource; actual CSS gives32px/16px title/body). Both findings were supplied to the reviewer. This is a scoped UI review, not human/AT acceptance.

The first Linux dual-root launch exceeded the256-task budget. The measured run501634554114440db64503dc1e583a2f recorded ProcessLimitHits221, PeakTasks256 and OomKills0. Display/container/image cleanup succeeded. The scenario-specific512-task repair passed in e55f4b37da5c40dc971246e302c48653 (peak305) and again after the live-state change in a097e03089014e7092883e2baffbfc90 (peak326). Both recorded zero process-limit hits and OOM kills, decoded pixels, unchanged target counters, closed roots and display/container/image cleanup. Other scenario limits remain256, with exact observed-limit validation. CPU2, memory1GiB, no external network, non-root identity, no-new-privileges and restricted capabilities remain separately checked.

Final Windows shipping validation discovered/executed/passed1,234; failed/skipped0. All prior1,190 identities remain. Receipt: artifacts/recording-validation/private-client-shipping-full.trx. The final nonincremental Release build had zero warnings/errors; formatting verification passed. Windows outputs were restored after Linux publish. The44 new cases exercise real native/HTTP composition plus deterministic protocol/model boundaries. Replacement seven-job remote CI remains required before the next increment. Hosted identity, human/AT, credentials, public deployment and Claude acceptance are unperformed.

CI39655895713 passed Ubuntu1,234/1,234 and all five independent Linux jobs; Windows passed1,233/1,234, with one pre-existing diagnostic fixture failing actual browser-root shutdown timing. All new viewer cases passed, but overall CI was failed. The next repair adds a Windows-only retained-handle root-stop capability with strict independent exit confirmation and actual callback drain; see browser-shutdown-v1.md. The viewer has not passed its replacement seven-job gate yet.

The owned-root repair passed local Windows1,247/1,247 with all1,234 prior identities preserved, focused native/controller60/60, clean Release/format checks and separate Linux/Podman private-client acceptance. Replacement seven-job CI is pending; see browser-shutdown-v1.md for exact cases and safe forced-stop facts. No prior failed run was accepted.
