# Private read-only viewer v1 validation

October 20, 2026, local Windows: **1,065 discovered/executed/passed, zero failed/skipped**. All 982 prior identities remain; discovery and TRX sets match. Final nonincremental Release warnings-as-errors build and formatting verification passed. Receipt: artifacts/recording-validation/private-viewer-full-fixed.trx. The focused 83/83 added cases comprise 44 broker cases, nine process-identity cases, seven owned-delivery cases, 21 actual HTTP cases and two real Chromium/HTTP cases.

The final standalone demo at artifacts/private-viewer/0342aac7600f42a383d313828ee674cd/index.md passed single exchange, wrong identity, actual capture gap, stale epoch, resumed baseline, revocation, same process/context/document, retained-view equivalence, no viewer input, credential absence and actual server/browser closure. Counts or local success do not establish remote identity-provider, deployment, browser UI, pixel/VNC, actual human or assistive-technology acceptance. Remote Windows/Ubuntu full-suite and private-viewer demos, both headed Linux/Docker scenarios and public-reader isolation are separate main-push CI gates.

| Requirement | Evidence |
| --- | --- |
| Trusted process credential establishes identity; malformed/unknown credentials cannot supply a subject | ProcessIdentity_RequiresTheExactProvisionedCredentialAndReturnsNoCallerSuppliedSubject |
| Exactly one concurrent bootstrap exchange succeeds; status and ordinary credential display omit secrets | Bootstrap_IsSingleExchangeEvenUnderConcurrentAttemptsAndStatusOmitsSecrets |
| Wrong subject/scope/token cannot consume the legitimate bootstrap or grant budget | DeniedExchange_CannotConsumeTheAuthorizedBootstrap; UnauthorizedIssuer_CannotConsumeTheTrustedGrantBudget |
| Bootstrap expiry is monotonic at both allowed endpoints and independent of UTC display time | BootstrapExpiry_UsesMonotonicExactBoundsAndIgnoresWallClockShift |
| Invalid bootstrap/connection lifetimes do not consume authority; invalid broker identity/scope/bounds cannot create it | InvalidLifetime_CannotConsumeGrantOrBootstrap; InvalidBrokerScopeOrBounds_CannotCreateAuthority |
| Every read/revoke binds identity, scan, session, connection ID and secret | EveryReadAndRevoke_RequiresExactAuthenticatedScopeAndConnection |
| Connection expiry rejects reads at the exact lower/upper lifetime boundaries | ConnectionExpiry_ClosesReadsAtExactMonotonicBoundary |
| Revoke, expiry and close deny late sources while actual work stays counted after cancelled waits | Revocation_RejectsLateSourceAndWaitsActualSourceAfterCancelledCaller |
| An unawaited delivery remains actual work; failed drain stays closed after completion | UnawaitedTransportDelivery_RemainsActualWorkUntilItSettlesAndDrainTimeoutStaysClosed |
| Missing, duplicate, null, mismatched or invalid source cannot invent a view; escaped callback cannot publish later | InvalidOrMissingSource_CannotDeliverAnInventedObservation; SettledSource_CannotUseAnEscapedPublicationCallbackLater |
| Grant/read budgets and invalid epochs reject before source work | GrantAndReadBudgets_RejectBeforeSourceAndDoNotConsumeReplacementAuthority; GlobalReadBound_AdmitsFourActualReadsAndRejectsFifthBeforeItsSource; InvalidReadEpoch_RejectsBeforeSource |
| Composed service cannot bind another owner/session | ViewerService_RejectsCompositionBoundToAnotherOwnerOrSession |
| Only acknowledged result/baseline is available; view sends no new capture, input or store write | ViewerUsesOnlyAcknowledgedRetainedObservationAndSendsNoInputOrNewCapture; UnacknowledgedCommand_CannotBecomeAViewerObservation |
| Capture acknowledgement drains actual delivery and rejects queued views even after caller cancellation | CaptureGap_WaitsActualViewerDeliveryAfterCallerStopsWaitingAndRejectsQueuedViews |
| Unconfirmed owned viewer drain remains closed after late transport completion | UnconfirmedViewerDrain_StaysClosedAfterLateTransportCompletion |
| Actual HTTP exchange, read, revoke/reconnect, no-cache headers, unsupported input paths and protocol document | ActualHttp_ExchangesOnceReadsBoundObservationAndRevokesEveryReconnectWithoutInputEndpoint |
| HTTP identity/Host/Origin/Cookie/scope/query/header denials precede source work and omit private payload | DeniedHttpContextOrScope_NeverInvokesObservationSource |
| Duplicate/unknown/malformed/oversize/unsupported-content JSON cannot consume bootstrap or echo payload | InvalidExchangeBody_CannotConsumeBootstrapOrEchoItsPayload |
| Actual concurrent HTTP revoke waits a noncooperative source and rejects its late private payload | ActualHttpRevocation_WaitsIgnoredSourceCancellationAndDropsLatePrivatePayload |
| Provider failures return sanitized Problem Details | ActualHttpProviderFailure_ReturnsSanitizedProblemWithoutPrivateExceptionText |
| Combined actual HTTP, Chromium, owned evidence, capture gap/fresh baseline, revocation and cleanup | ActualHttpAndChromium_RetainedReadExclusionFreshBaselineAndRevocationShareOneOwnedSession |
| Browser mutation after persistence is not recaptured by viewing; actual focus/keyboard stay unchanged and editable values remain absent | ActualViewerRead_ReturnsAcknowledgedEvidenceInsteadOfRecapturingChangedBrowserOrMovingFocus |

Local Linux/Podman headed compatibility passed separately at artifacts/headed-linux/4b7cf40acb6e4c13b328f3d8dc4e97fb/runtime.json. The existing controller scenario ran with the new CLI shared-framework dependency and confirmed actual continuity and owned browser/display/container/image cleanup. This is not a Linux private-viewer full-suite result. The regular Windows build was restored after the Linux publish.

Development caught a runtime packaging regression: ASP.NET framework pruning removed Hosting assemblies from the test output, where MCP tests had launched a copied application DLL with its standalone runtime configuration. The harness now launches the referenced MCP application's own output/dependency closure; all ten MCP cases passed in the focused repair. The MCP application itself keeps its existing runtime/package dependencies. FakeTimeProvider.AdjustTime also shifts its timestamp source, so the UTC-skew witness now uses a TimeProvider wrapper that independently changes UTC while retaining monotonic timers. A native TCP connection-refused check replaces HttpClient retries for actual server-close confirmation. Failed receipts are retained and are not acceptance evidence.

Run the [bounded proof](../decisions/0031-private-read-only-viewer.md). The next increment is [private pixel-viewer feasibility](../decisions/0031-private-read-only-viewer.md#next-private-pixel-viewer-feasibility); real remote identity, human input and credential entry remain separate gates. No local Linux full-suite, deployment, fresh NVDA or Claude acceptance is claimed.
