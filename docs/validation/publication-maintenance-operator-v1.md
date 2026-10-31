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

New CI, installation of the exact helper/runner bytes and an actual completed-run
maintenance check remain pending. Automatic restart remains disabled.
