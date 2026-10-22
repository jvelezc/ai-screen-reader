# Private workspace control validation

Local protocol tests use controlled Auth/REST responses; they do not establish a real Supabase app identity. Real Kestrel and Chromium tests exercise the actual HTTP/UI paths, and the result path uses an actual native Chromium fixture observation, private file bytes and hash validation.

| Requirement | Evidence |
| --- | --- |
| Fixed-project identity; original user bearer; denied/outage/malformed handling | `IdentityComesFromFixedProjectUserLookupAndNeverFromCallerClaims`, `ProviderDeniedIdentityNeverBecomesWorkspaceAuthority`, `AuthOutageStaysUnavailableAndDoesNotExposeBearerOrRawException`, `CallerCancellationSettlesActualProviderRequestWithoutOutageClassification` |
| Only the subject can bootstrap one workspace; concurrency and revocation | `tests/database/personal-workspace.sql` (29 cases), `tests/database/concurrent-personal-workspace.sql` (6 actual concurrent cases) |
| Explicit HTTP admission/Location; strict body, host, origin and authentication | `WorkspaceCreateReturnsRetrievableLocationAndDuplicateReturnsSameResource`, `AdmittedFixtureReturnsReportLocationAndPendingEvidenceDoesNotVerifyGoal`, `InvalidAdmissionBodyHasNoDataSideEffect`, `HostAndCrossOriginRequestsAreDeniedBeforeIdentityOrTenantWork` |
| Exact metadata scope, stored identity, hash and native deadlines | `JobMetadataCannotChangeScopeOrSmuggleUnverifiedEvidence`, `ExactReferenceHashIdentityAndNativeShutdownAreRequiredBeforeRendering`, `RealChromiumObservationSurvivesPrivateStorageHashAndAuthenticatedHttpReport` |
| Pending, blocked, missing, corrupt and concurrent state remain explicit | `IncompleteExecutionCannotReadEvidenceOrInventAccessibilityJudgment`, `MissingAndInvalidStoredEvidenceRemainDifferentFromCompletedExecution`, `ReadsAcrossAtomicCompletionRefreshMetadataOnceBeforeOpeningEvidence`, `MissingJobAndAccessRemovedDuringRefreshCannotOpenEvidence` |
| Native keyboard use, mobile layout, report delivery and private-data clearing | `NativeKeyboardSignInRunVerifiedResultAndSignOutWorkWithoutCredentialPersistence`, `SignupUsesPkceAndSingleFlightWhileAccessDenialClearsPrivateResults` |
| No readiness shortcut to tenant acceptance | `PublicSettingsAndActualListenerReadinessMakeNoTenantAcceptanceClaim` |

Real local PostgreSQL/pgmq acceptance:196 discovered/executed/passed, failed/skipped/unconfirmed0, owned container removed and `HostedProjectChanged=false`. Receipt `artifacts/database/d6cfeebfe0274c2cbfb4315b8c41c8c8/run.json`; all prior161 case identities retained. The harness uses `supabase_admin` only to arrange disposable test roles; the tested calls run with their actual restricted roles.

Focused portal protocol/report validation initially passed60 cases, boundary validation31 and real Chromium UI3. The malformed-observation test caught an unhandled `InvalidDataException`; the report reader now maps it to invalid evidence. The native fixture preserves strict exit confirmation before4,000ms and the original outer5,000ms cleanup contract.

UI finish review: disposition **ship**, scoped to the scored unnamed-node correction. Desktop1120 and mobile320 sign-in/result captures were opened and verified. The single detector run returned no findings. No new visual world or shipping raster assets were introduced.

Full local Windows Release regression:1,449 discovered/executed/passed, failed/skipped0. All1,352 preceding test identities are retained with their original multiplicities. `artifacts/portal-validation/portal-regression.trx` includes the97 new portal cases. Strict nonincremental Release build with warnings as errors passes with0 warnings/errors; full solution formatting verification passes. The final browser group includes the PKCE callback and literal-markup test as a fourth case.

Remote CI must pass before the next deployment increment. Hosted portal deployment, actual app signup/email confirmation/JWT, tenant job execution, S3 and human/AT acceptance remain open. The separate worker release/rollback acceptance is not a substitute for those gates.
