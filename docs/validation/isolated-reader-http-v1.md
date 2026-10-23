# Isolated reader HTTP composition validation

See [decision0047](../decisions/0047-isolated-reader-http-composition.md). This record separates implementation, local proof, CI, private delivery and tenant acceptance.

| Requirement | Evidence |
| --- | --- |
| Reserve nonsecret ownership before initialization; startup consumes original lease | `ReservationSelectsNonsecretScopeAndStartsExpiryBeforeNativeInitialization`; `NativeStartupConsumesTheOriginalTwoMinuteLease` |
| Canceled/failed initialization cannot free unknown native capacity | `CanceledInitializationJoinsActualLaunchAndCleanupBeforeReleasingItsReservation`; `FailedInitializationWithUnknownCleanupRetainsSubjectUntilExplicitRetryConfirmsAbsence` |
| Physical absence and native acknowledgement remain separate | `ConfirmedAbsenceReleasesCapacityWhileMissingGracefulAcknowledgementStillReturnsFailure`; `NativeAcknowledgementAndPhysicalAbsenceRemainIndependentCloseFacts` |
| Worker authority and response identity cannot cross scope or restart | `WrongProcessCapabilityCannotReachDispatchAndDoesNotPoisonTheListener`; `ResponseCannotCrossScopeOrInventAFreshObservation`; `RestartChangesAuthorityAndOldRequestsCannotSilentlyJoinAnotherWorker` |
| Readiness requires the actual worker | `HttpReadinessBecomesUnavailableWhenTheActualReaderWorkerStops` |
| Drain joins clients and retires private authority | `BrokerDrainClosesAnActualClientWithAnIncompleteFrameAndJoinsItsWork`; `ActualLoopbackBrokerPublishesPrivateAuthorityAndRemovesItOnlyAfterDraining` |
| Targeted recovery preserves unrelated intent and rejects ambiguous ownership | `TargetedRecoveryWithoutAnIntentLeavesEveryUnrelatedIntentUntouched`; `TargetedRecoveryRejectsAmbiguousScopeBeforeAnyNativeRemoval` |
| Root promotion requires reader retirement before restart | `test_reader_recovery_runs_only_after_queue_recovery_and_requires_empty_inventory`; `test_reader_recovery_cannot_claim_success_while_an_intent_remains` |
| Actual packaged API, separate worker, fixed document and two fresh observations | `PublishedHttpReaderUsesIsolatedNativeContextAndRechecksSyntheticAuthority` in `scripts/Test-ObservationWorker.ps1` |
| Actual sibling survival, global subject/capacity limits, native close and container absence | HTTP/native receipt's `GlobalSubjectReservationConfirmed`, `GlobalCapacityConfirmed`, `WorkerScopeDenied`, `SiblingDocumentPreserved`, `NativeShutdownConfirmed`, `ContainerAbsenceConfirmed` |

The Windows-launched Linux/Podman packaged harness passes **28/28**, zero failures, skips or unconfirmed cases. Its expanded HTTP proof uses a separate worker process, two real isolated readers, 31 exposed Foundation nodes and no database access, saved observation, input or goal claim. All disposable resources are removed and the reader descriptor is absent after drain. Linux operator checks pass **122/122** on the actual Podman Linux host. These do not substitute for native Linux CI's additional SIGTERM cases or signed-in tenant acceptance.

The complete Windows suite passes **1,801/1,801**, zero failures/skips, including61 new cases. Strict nonincremental Release build passes with zero warnings/errors and full format verification passes. A deliberate self-comparison mutation of the worker capability check makes `WrongProcessCapabilityCannotReachDispatchAndDoesNotPoisonTheListener` fail its expected-exception assertion; restoring the exact source makes it pass. This proof uses a Ready request so a broken authorization check cannot hide behind a failing container launch.

Source3471fd3 passed all nine jobs in CI39924056549: Windows/Ubuntu each1801, native Linux30/30 including signals, operator122/122. Exact private deployment39925519783 and independent VPS readback verify release/image/operator hashes, healthy services, loopback worker broker, private0600 descriptor, read-only API, both ownership locks, anonymous reader denial and unchanged Caddy/public listeners. The actual VPS native canary passes same-document capture, scoped retirement/sibling survival, native shutdown/absence and recovery without leftovers or service restart. Automatic deployment remains enabled. The earlier native runtime is `2bc8405`, CI39909237769, private deployment39910679532.

Real application-user reader acceptance remains open; a masked local credential prompt runner is prepared but has not run with a confirmed app account. Management MCP/dashboard login is not an application identity. See [the next navigation increment](hosted-reader-navigation-v1.md); its synthetic proof cannot close this gate.
