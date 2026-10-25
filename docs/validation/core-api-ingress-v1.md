# Core API HTTPS ingress validation

[Decision0053](../decisions/0053-authenticated-core-api-ingress.md) authorizes
API HTTPS on the existing VPS IP. The versioned site is
[`hsr-api-ip.caddy`](../../ops/ingress/hsr-api-ip.caddy), installed by
[`api_site.py`](../../ops/ingress/api_site.py). Public MCP remains deferred.

## Operator checks

The existing Linux deployment test runner discovers the new cases automatically:

```sh
python3 ops/worker/test_release.py
```

| Requirement | Evidence |
| --- | --- |
| Install only reviewed bytes, preserve unrelated configuration, separate reload from TLS | `test_install_preserves_unrelated_bytes_and_separates_reload_from_tls`, `test_unreviewed_site_bytes_fail_before_file_changes`, `test_private_umask_does_not_make_site_unreadable_to_caddy` |
| Reject changes before live write, including changes during validation | `test_changed_main_is_denied_before_validation`, `test_changed_unrelated_site_is_denied_without_overwrite`, `test_change_during_validation_is_fenced_before_live_write` |
| Failed validation leaves the live configuration intact | `test_validation_failure_never_creates_live_site_or_reloads` |
| Conflicting files, concurrent creation, symlinks and writable configuration cannot authorize replacement | `test_other_existing_owned_filename_is_never_overwritten`, `test_concurrent_creation_cannot_be_overwritten`, `test_dangling_owned_symlink_is_denied_without_following`, `test_unrelated_symlink_is_not_read_or_replaced`, `test_main_symlink_is_not_followed`, `test_group_writable_directory_is_denied`, `test_group_writable_configuration_is_not_trusted` |
| Reuse of identical files does not invent activation or TLS proof | `test_idempotent_file_receipt_does_not_invent_reload_or_tls`, `test_missing_exact_import_cannot_report_installation` |
| Failed reload restores only matching owned bytes; rollback failures/conflicts stay explicit | `test_reload_failure_removes_only_owned_site_and_reloads_previous`, `test_failed_rollback_reload_remains_unconfirmed`, `test_external_site_replacement_during_failure_is_not_deleted`, `test_explicit_rollback_removes_only_matching_owned_file`, `test_rollback_rejects_different_file_without_reload` |
| Concurrent unrelated changes after reload retain an unconfirmed outcome | `test_unrelated_change_during_reload_retains_unconfirmed_site` |

All144 deployment/operator cases passed on local Windows-launched Linux/Podman,
including22 new ingress cases, with zero failures/skips. Only `ops` and the
deployment test folder were mounted read-only; the container had no network.
These file/callback tests do not establish live Caddy, TLS or tenant acceptance.
The actual combined candidate also passed installed Caddy2.11.4 validation
before any live import was changed. CI and activation remain separate receipts.

## Reviewed root provisioning

Capture root-owned main/unrelated Caddy fingerprints immediately before review.
The baseline JSON contains `MainSha256` and `UnrelatedSites` (filename -> SHA256).
It must correspond to the exact candidate validated with the existing imports.
After CI accepts the source, copy the operator, site and reviewed baseline to a
root-owned staging directory outside `/etc/caddy/sites/*.caddy`.

```sh
python3 api_site.py install --site hsr-api-ip.caddy --baseline reviewed-baseline.json --site-sha256 <reviewed-sha256>
```

The operator uses fixed `/etc/caddy` paths and an exclusive operation lock.
The root-owned site is persistent host provisioning; ordinary restricted CI
backend promotions continue without rewriting Caddy. On activation failure,
retain the safe error and inspect rollback status before retrying. Explicit
rollback requires matching installed bytes:

```sh
python3 api_site.py rollback --site hsr-api-ip.caddy --baseline reviewed-baseline.json --site-sha256 <installed-sha256>
```

## External acceptance

Require a publicly trusted IP certificate without verification bypass, the
expected OpenAPI contract, anonymous API denial, private/legacy path denial,
foreign-Origin denial, retained core limits and unchanged unrelated site
configuration/behavior. Verify actual certificate expiry/renewal configuration;
a successful reload alone is insufficient. Original-user Edge-to-core and
visible UI workflows must be recorded separately from this transport check.
