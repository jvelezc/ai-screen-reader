# Headed Linux session v1 validation

October 20, 2026, local Windows: **918 discovered/executed/passed, 0 failed/skipped**, with all 873 prior identities retained and 45 cases added (including one real Chromium metadata case). Discovery and TRX identity sets match. Final nonincremental Release build had zero warnings/errors; formatting verification passed. Receipt: artifacts/recording-validation/headed-session-windows.trx.

Local Linux/Podman headed acceptance passed separately at artifacts/headed-linux/6fb2a3f468e54a158200ab1888e093ab/runtime.json. The named case reported identical actual browser process/context/target, same document, fresh goal verified, nonroot/no-new-privileges and all five capability masks restricted, followed by actual browser/display exit and container/image removal. No pending or authority files were copied. This is a real headed fixture experiment; no local Linux full-suite result is claimed. Linux/Docker CI is tracked separately on the corresponding main push.

| Requirement | Evidence |
| --- | --- |
| Only bounded local Unix display forms are accepted | LocalDisplayNumbers_ParseOnlySupportedUnixDisplays; UnsupportedDisplayForms_AreRejectedBeforeProcessOrFileLookup |
| Native headless/no-sandbox flags are exact; original arguments are absent from retained evidence | ActualArgumentFlags_AreExactAndOriginalPayloadIsNeverRetained; UnconfirmedNativeArgumentEvidence_CannotProduceAProof |
| CDP identity retains actual browser/context/target without URL payload | DiagnosticIdentity_RetainsOnlyActualBrowserProcessContextAndTarget |
| Missing, ambiguous or over-limit identity cannot produce a proof | AmbiguousOrInvalidCdpIdentity_FailsClosed |
| Every observed capability set accepts zero or only SYS_CHROOT | ZeroOrOnlySandboxCapability_IsAcceptedInEveryActualMask |
| Extra capability in any set, root, missing evidence or new privileges cannot pass | AdditionalCapabilityInAnySet_IsRejected; IncompleteOrPrivilegedKernelEvidence_CannotPass |
| Real Chromium metadata stays stable across exclusion/resume and is denied during the gap | RealChromiumIdentity_IsStableAcrossSameContextExclusionAndDeniedDuringGap |
| Actual headed Xvfb, two native-input controllers, unchanged process/context/document, fresh goal and owned cleanup | HeadedChromium_TwoControllerGenerationsPreserveProcessContextAndCleanOwnedDisplay in scripts/Test-HeadedSession.ps1; inspect runtime.json, run.json and cleanup.json |

Remote CI [39511138302](https://github.com/jvelezc/ai-screen-reader/actions/runs/39511138302) passed separately on main: Windows and Ubuntu each executed and passed all 918 cases, and the independent headed Linux/Docker and public-reader isolation jobs passed. Downloaded TRX and headed receipts confirmed these results. This does not establish deployment or assistive-technology acceptance.

Development found three real differences: Browser.getBrowserCommandLine is unavailable without enabling an additional automation flag, Xvfb shutdown completes asynchronously after xvfb-run returns, and Podman normalizes configured capability lists differently from Docker. The implementation now reads only required flags from its own Linux process, waits for actual display cleanup, and checks all five actual kernel capability masks. Failed batches remain in artifacts/headed-linux; they are not acceptance evidence. A C# 14 reserved lambda identifier caused one build failure and was renamed before validation.

This proof uses the trusted, network-disabled fixture with a disposable writable container layer. It does not certify arbitrary-site isolation, VNC authentication/revocation, accessible remote login, human takeover, sensitive input, deployment or fresh assistive-technology behavior.
