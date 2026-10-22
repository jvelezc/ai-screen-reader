# Private portal deployment validation

The preceding portal repair passed all nine jobs in [CI39715558383](https://github.com/jvelezc/ai-screen-reader/actions/runs/39715558383): Windows and Ubuntu1,450 each, database196, real Linux worker21 and operational86. That acceptance preceded this increment.

| Requirement | Evidence |
| --- | --- |
| Actual HTTP configuration,401 guard and joined listener shutdown without a tenant claim | `ActualRuntimeProbeChecksHttpGuardAndJoinsStopWithoutTenantIdentityClaim`, `CancelledRuntimeProbeNeverReturnsReady`; `PublishedControlRuntimeChecksHttpGuardAndJoinsStopWithoutTenantClaim` executes the actual published CLI |
| Package only the complete executed control closure; retain v1 compatibility | `test_v2_binds_every_executed_control_byte_and_keeps_v1_worker_compatible`, `test_changed_control_dependency_cannot_enter_release`, `test_omitted_executed_control_dependency_cannot_create_an_incomplete_release`, `test_unexecuted_control_dependency_cannot_be_added`, `test_control_cannot_enter_a_bundle_without_an_executed_closure` |
| Separate public control configuration from privileged worker configuration | `test_control_environment_contains_only_public_configuration_and_closed_runtime_fields`, `test_public_key_guards_reject_privileged_other_project_duplicate_and_injected_values`, `test_control_entrypoint_executes_fixed_mode_with_no_inherited_database_environment` |
| Stop control before worker recovery; require fresh journal, HTTP and same active process | `test_portal_stop_must_settle_before_worker_stop`, `test_readiness_requires_fresh_journal_actual_http_and_same_active_invocation`, `test_http_failure_or_replaced_or_exited_process_cannot_be_ready`, `test_missing_current_journal_receipt_times_out_without_http_shortcut` |
| Drain a failed portal before restoring a worker-only release | `test_v1_rollback_drains_failed_portal_then_recovers_and_restarts_only_previous_worker` |
| Clear previously displayed private results when tenant RLS hides the workspace | `MembershipHiddenByTenantRlsClearsPreviouslyDisplayedPrivateEvidence` uses controlled provider/project state and actual Kestrel/Chromium; it is not real hosted revocation acceptance |

Actual Linux operational discovery:113 executed/passed, failed/skipped0, retaining all86 previous identities. Files, symlinks and release pointer operations are real; systemd/HTTP lifecycle boundaries in those Python tests are simulated. Receipt: `artifacts/deployment-validation/unit.json`.

The local Windows-launched Podman worker harness passed20 checks, including the actual published control entrypoint. Its owned database and image were removed. This run did not produce a release bundle or perform the two Linux-only signal/concurrency checks; those remain CI gates. Receipt: `artifacts/worker-linux/3e28a135ecc34118b3d9c6abe5696ce9/run.json`.

The membership-loss screenshot was opened and shows the cleared sign-in state and safe access error. The scoped finish reviewer accepted the privacy repair, and the documenter rechecked the scoped surface brief. No additional detector or visual redesign was performed.

Full local Windows regression:1,453 discovered/executed/passed, failed/skipped0, retaining all1,450 preceding identities and multiplicities. Receipt: `artifacts/portal-validation/private-control-deployment.trx`. Strict nonincremental Release build with warnings as errors passes0warnings/errors; full formatting verification passes after whitespace corrections. The listener test asserts actual OS listener absence after disposal rather than accepting a client connection timeout as shutdown proof.

The broad quality review maps public outcomes to exact receipts, side-effect ordering, negative/no-call assertions, actual file preservation and real HTTP/DOM/listener state. Removing hash/inventory checks, permitting privileged configuration, skipping control stop, reusing old invocation readiness or retaining404 private data conflicts with those assertions. This is source-to-assertion review; no empirical mutation run is claimed. Actual systemd and identity-provider integration remain independent gates.

Exact-commit remote CI, accepted v2 export, hosted installation and real app-user acceptance must still be recorded separately. No live portal, hosted migration, actual user JWT, private S3 or assistive-technology acceptance is implied by these local checks.

[CI39719786201](https://github.com/jvelezc/ai-screen-reader/actions/runs/39719786201) passed SQL196, Linux operational113 and the actual Linux worker22, including v2 release export and cleanup. Its Ubuntu full suite passed1,452/1,453; route0 of `FourOriginalRecordings_LinkRealCaptureAndInputIntervalsWithoutChangingRouteClassification` returned an uncalibrated alignment. The original assertion did not print its reason. The assertion now retains the same calibration requirement and includes the alignment, clock brackets and bounded synthetic frame-marker diagnostics. No timing target, shutdown limit or expected route result changed.

A separate network-disabled, sandboxed Linux run on the native container filesystem passed all four recorded routes with calibration uncertainty approximately42.4–42.6ms; the owned container was removed. This did not reproduce the CI failure and does not establish its cause. An earlier Windows-mounted output attempt failed collector shutdown and is not counted as a passing reproduction. Replacement full CI remains required; no hosted mutation or deployment is authorized by the partial run.

The completed run passed its other eight jobs, with remote Windows1,453/1,453. After adding diagnostics, local Windows again passes1,453/1,453, strict nonincremental build0warnings/errors and full formatting verification. Receipt: `artifacts/portal-validation/portal-deployment-diagnostics.trx`. This change improves failure evidence; it does not claim a causal recording repair.
