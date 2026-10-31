# Publication maintenance operator

October31,2026. Implements [decision0071](../decisions/0071-publication-maintenance-operator.md).

| Requirement | Named evidence |
| --- | --- |
| Stop, cleanup, publication, cleanup and existing readiness gates execute in order | `test_publication_waits_for_stop_cleanup_and_ordered_readiness` |
| Unknown actual publisher cannot resume polling | `test_unknown_actual_publisher_cannot_restore_polling`, `test_killed_waiter_cannot_substitute_for_an_active_unit`, `test_only_explicit_inactive_or_failed_zero_pid_metadata_confirms_settlement` |
| Stop/native/readiness errors cannot return Ready | `test_unconfirmed_stop_or_native_recovery_cannot_launch_publication`, `test_failed_ready_check_cannot_return_a_ready_operator_receipt` |
| Failed or completed publication is separate from service readiness | `test_exact_completed_retry_preserves_a_historical_nonreceipt`, `test_settled_failure_can_restore_services_without_claiming_publication`, `test_settled_unreadable_receipt_restores_only_service_readiness` |
| Original scope, no replay authority and private output remain protected | `test_actual_scope_is_canonical_before_any_service_operation`, `test_substituted_scope_and_unknown_private_fields_cannot_enter_a_receipt`, `test_secret_bearing_child_json_is_discarded_after_confirmed_exit` |
| Existing environment stays private and browser authority is absent | `test_transient_unit_uses_fixed_private_environment_and_no_browser_authority` |
| Explicit nonzero exit handling preserves the default runner and output bounds | `test_known_nonzero_exit_requires_explicit_opt_in_and_still_bounds_output`, `test_invalid_accepted_codes_cannot_launch_a_process` |
| Scoped maintenance events remain separate from deployment/goal receipts | `test_maintenance_events_are_separate_ordered_scope_receipts` |

The full operational suite passes171/171 on actual local Linux through the
existing Podman WSL machine, including16 new cases. Discovery/execution/pass counts
are read from the runner's JSON; failed/skipped0 and every discovered case executed.
Tests use real Linux
files/processes where stated, and simulated service calls for orchestration; they
do not substitute for hosted service acceptance. No C# or package version changed.
The accepted parent remains d144993: Windows/Linux2713 each, native67, database361,
exact151-file deployment and actual original-account publication/S3/graph/video
readback. The [parent receipt](recorded-publication-recovery-v1.md) records its
scoped failure and operator startup race separately.

Core `df5dc503f6cdb70358a1bfc8019b054e69f669b6` passes all nine jobs in
[CI41190148654](https://github.com/jvelezc/ai-screen-reader/actions/runs/41190148654).
Downloaded Windows/Ubuntu TRX each discover, execute and pass2,713/2,713,
failed/skipped0. Native Linux67/67 and database361/361 pass with zero failed/
unconfirmed/skipped and their test database/image/container removed. The CI
operational receipt independently confirms171/171, failed/skipped0.

[Private deployment41191353712](https://github.com/jvelezc/ai-screen-reader/actions/runs/41191353712)
is Ready. Independent checks match151 release files, trusted TLS, HTTP1.12
schema references and protected-read denials. The separate two-file operator
upgrade uses an exact Git-source archive and verifies the prior runtime hash
before replacement, preserving its private backup. Installed helper SHA-256 is
`9cee5ef141a92dc4c5c7d8ea7a7b01ae5bcf318d311d13bac983d41145ad47dd`;
runner SHA-256 is
`653f74877190453a378aec51837d3532b22f29f3847b5b1ea412f56f41d95bac`.
No deployment key, credential, service configuration or restart policy changed.

The actual root-only command runs against the already recovered original scan
`b2b291c2-52bd-48c8-a3db-cd30edf5f5c9`, job
`2421ec9a-198e-42f1-8826-70e81ce40426`, generation1. It returns
AlreadyCompleted/ServicesReady with the original producer and955,559-byte bundle
hash, input dispatched by recovery0 and overall goal NotAssessed. This verifies
completed-run maintenance; first publication after the real storage fault remains
the separately accepted parent command path. Both service lifecycles use the
existing ordered runtime, and post-maintenance HTTP/TLS/release guards pass.

Before/after canonical database reads match exactly: one execution attempt, two
publication events, zero queued/leased jobs and unchanged names/bytes/updated_at
for all seven S3 objects. The stage and every command-progress hash remain fixed.
Fresh independent native checks before and after this maintenance run Podman as
the actual rootless hsr-worker(uid1001), and confirm the owned container is absent.
The parent's earlier root-namespace check is explicitly insufficient and is not
promoted retroactively. The original signed-in account refreshes Observed/Available
through UI f7c1127 → existing Edge9 → core, without a new scan or sign-in.

Automatic restart remains disabled. Unfinished source media and the remaining
crash/network/storage/lease/cancellation/disk matrix remain later gates.
