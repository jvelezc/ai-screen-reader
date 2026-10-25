from pathlib import Path
import os
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "ingress"))
from api_site import IngressError, SITE_NAME, digest, install, rollback, snapshot

SITE = b"https://89.117.17.81 { respond 401 }\n"


class ApiIngressTests(unittest.TestCase):
    """Actual Linux file ownership; simulated Caddy callbacks do not attest TLS."""
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hsr-ingress-")
        self.root = Path(self.temporary.name)
        (self.root / "sites").mkdir()
        self.main = self.root / "Caddyfile"
        self.main.write_text(f"import {self.root}/sites/*.caddy\n", encoding="utf-8")
        self.other = self.root / "sites" / "other.caddy"
        self.other.write_bytes(b"other.example { respond 204 }\n")
        self.owned = self.root / "sites" / SITE_NAME
        self.expected = snapshot(self.root)
        self.validations = []
        self.reloads = []

    def tearDown(self):
        self.temporary.cleanup()

    def validate(self, path):
        self.validations.append(path.read_bytes())

    def reload(self, root):
        self.assertEqual(self.root, root)
        self.reloads.append(self.owned.read_bytes() if self.owned.exists() else None)

    def apply(self, validate=None, reload=None):
        return install(self.root, SITE, self.expected, digest(SITE), validate or self.validate, reload or self.reload)

    def test_install_preserves_unrelated_bytes_and_separates_reload_from_tls(self):
        original = self.main.read_bytes()
        result = self.apply()
        self.assertEqual("Installed", result["State"])
        self.assertTrue(result["CaddyReloaded"])
        self.assertFalse(result["TlsAccepted"])
        self.assertEqual(SITE, self.owned.read_bytes())
        self.assertEqual(0o644, stat.S_IMODE(self.owned.stat().st_mode))
        self.assertEqual(original, self.main.read_bytes())
        self.assertEqual(self.expected, snapshot(self.root))
        self.assertEqual([original + b"\n" + SITE], self.validations)
        self.assertEqual([SITE], self.reloads)
        self.assertEqual([], list(self.root.glob(".hsr-review-*")))

    def test_private_umask_does_not_make_site_unreadable_to_caddy(self):
        previous = os.umask(0o077)
        try:
            self.apply()
        finally:
            os.umask(previous)
        self.assertEqual(0o644, stat.S_IMODE(self.owned.stat().st_mode))

    def test_validation_failure_never_creates_live_site_or_reloads(self):
        def reject(path):
            self.assertIn(SITE, path.read_bytes())
            raise IngressError("SIMULATED_VALIDATION_FAILURE")
        with self.assertRaisesRegex(IngressError, "SIMULATED_VALIDATION_FAILURE"):
            self.apply(validate=reject)
        self.assertFalse(self.owned.exists())
        self.assertEqual([], self.reloads)
        self.assertEqual([], list(self.root.glob(".hsr-review-*")))
        self.assertEqual(self.expected, snapshot(self.root))

    def test_changed_main_is_denied_before_validation(self):
        self.main.write_text(self.main.read_text() + "# concurrent operator\n")
        with self.assertRaisesRegex(IngressError, "INGRESS_BASELINE_CHANGED"):
            self.apply()
        self.assertEqual([], self.validations)
        self.assertFalse(self.owned.exists())
        self.assertIn("concurrent operator", self.main.read_text())

    def test_changed_unrelated_site_is_denied_without_overwrite(self):
        self.other.write_bytes(b"external change")
        with self.assertRaisesRegex(IngressError, "INGRESS_BASELINE_CHANGED"):
            self.apply()
        self.assertEqual(b"external change", self.other.read_bytes())
        self.assertFalse(self.owned.exists())
        self.assertEqual([], self.reloads)

    def test_change_during_validation_is_fenced_before_live_write(self):
        def concurrent(path):
            self.validate(path)
            self.other.write_bytes(b"external during validation")
        with self.assertRaisesRegex(IngressError, "INGRESS_BASELINE_CHANGED"):
            self.apply(validate=concurrent)
        self.assertFalse(self.owned.exists())
        self.assertEqual(b"external during validation", self.other.read_bytes())
        self.assertEqual([], self.reloads)

    def test_other_existing_owned_filename_is_never_overwritten(self):
        self.owned.write_bytes(b"not this installation")
        with self.assertRaisesRegex(IngressError, "INGRESS_SITE_OWNERSHIP_DENIED"):
            self.apply()
        self.assertEqual(b"not this installation", self.owned.read_bytes())
        self.assertEqual([], self.reloads)

    def test_idempotent_file_receipt_does_not_invent_reload_or_tls(self):
        self.owned.write_bytes(SITE)
        result = self.apply()
        self.assertEqual("AlreadyInstalled", result["State"])
        self.assertFalse(result["CaddyReloaded"])
        self.assertFalse(result["TlsAccepted"])
        self.assertEqual([], self.reloads)

    def test_reload_failure_removes_only_owned_site_and_reloads_previous(self):
        def fail_first(root):
            self.reload(root)
            if len(self.reloads) == 1:
                raise IngressError("SIMULATED_RELOAD_FAILURE")
        with self.assertRaisesRegex(IngressError, "INGRESS_ACTIVATION_FAILED_ROLLED_BACK"):
            self.apply(reload=fail_first)
        self.assertEqual([SITE, None], self.reloads)
        self.assertFalse(self.owned.exists())
        self.assertEqual(self.expected, snapshot(self.root))

    def test_failed_rollback_reload_remains_unconfirmed(self):
        def fail(root):
            self.reload(root)
            raise IngressError("SIMULATED_RELOAD_FAILURE")
        with self.assertRaisesRegex(IngressError, "INGRESS_ACTIVATION_AND_ROLLBACK_UNCONFIRMED"):
            self.apply(reload=fail)
        self.assertEqual([SITE, None], self.reloads)
        self.assertFalse(self.owned.exists())

    def test_external_site_replacement_during_failure_is_not_deleted(self):
        def changed(root):
            self.owned.write_bytes(b"concurrent owned replacement")
            raise IngressError("SIMULATED_RELOAD_FAILURE")
        with self.assertRaisesRegex(IngressError, "INGRESS_ACTIVATION_AND_ROLLBACK_UNCONFIRMED"):
            self.apply(reload=changed)
        self.assertEqual(b"concurrent owned replacement", self.owned.read_bytes())
        self.assertEqual(self.expected, snapshot(self.root))

    def test_concurrent_creation_cannot_be_overwritten(self):
        original_open = os.open
        def racing_open(path, flags, *args, **kwargs):
            if Path(path) == self.owned and flags & os.O_CREAT:
                self.owned.write_bytes(b"external creation")
            return original_open(path, flags, *args, **kwargs)
        with patch("api_site.os.open", side_effect=racing_open):
            with self.assertRaisesRegex(IngressError, "INGRESS_SITE_OWNERSHIP_DENIED"):
                self.apply()
        self.assertEqual(b"external creation", self.owned.read_bytes())
        self.assertEqual([], self.reloads)

    def test_dangling_owned_symlink_is_denied_without_following(self):
        target = self.root / "must-not-create"
        self.owned.symlink_to(target)
        with self.assertRaisesRegex(IngressError, "INGRESS_SITE_OWNERSHIP_DENIED"):
            self.apply()
        self.assertTrue(self.owned.is_symlink())
        self.assertFalse(target.exists())

    def test_unrelated_symlink_is_not_read_or_replaced(self):
        self.other.unlink()
        target = self.root / "outside"
        target.write_bytes(b"retain private contents")
        self.other.symlink_to(target)
        with self.assertRaisesRegex(IngressError, "INGRESS_FILE_UNCONFIRMED"):
            self.apply()
        self.assertEqual(b"retain private contents", target.read_bytes())
        self.assertFalse(self.owned.exists())

    def test_main_symlink_is_not_followed(self):
        target = self.root / "outside"
        self.main.rename(target)
        self.main.symlink_to(target)
        with self.assertRaisesRegex(IngressError, "INGRESS_FILE_UNCONFIRMED"):
            self.apply()
        self.assertFalse(self.owned.exists())
        self.assertEqual([], self.reloads)

    def test_group_writable_directory_is_denied(self):
        (self.root / "sites").chmod(0o775)
        with self.assertRaisesRegex(IngressError, "INGRESS_DIRECTORY_DENIED"):
            self.apply()
        self.assertFalse(self.owned.exists())

    def test_group_writable_configuration_is_not_trusted(self):
        self.other.chmod(0o664)
        with self.assertRaisesRegex(IngressError, "INGRESS_FILE_OWNER_DENIED"):
            self.apply()
        self.assertFalse(self.owned.exists())
        self.assertEqual([], self.reloads)

    def test_unreviewed_site_bytes_fail_before_file_changes(self):
        with self.assertRaisesRegex(IngressError, "INGRESS_REVIEWED_SITE_REQUIRED"):
            install(self.root, SITE, self.expected, "a" * 64, self.validate, self.reload)
        self.assertEqual([], self.validations)
        self.assertFalse(self.owned.exists())

    def test_missing_exact_import_cannot_report_installation(self):
        self.main.write_text("import /elsewhere/*.caddy\n")
        with self.assertRaisesRegex(IngressError, "INGRESS_IMPORT_REQUIRED"):
            self.apply()
        self.assertFalse(self.owned.exists())

    def test_explicit_rollback_removes_only_matching_owned_file(self):
        self.apply()
        result = rollback(self.root, self.expected, digest(SITE), self.reload)
        self.assertEqual("RolledBack", result["State"])
        self.assertTrue(result["CaddyReloaded"])
        self.assertFalse(result["TlsAccepted"])
        self.assertEqual([SITE, None], self.reloads)
        self.assertEqual(self.expected, snapshot(self.root))

    def test_rollback_rejects_different_file_without_reload(self):
        self.owned.write_bytes(b"different owner")
        with self.assertRaisesRegex(IngressError, "INGRESS_ROLLBACK_OWNERSHIP_DENIED"):
            rollback(self.root, self.expected, digest(SITE), self.reload)
        self.assertEqual(b"different owner", self.owned.read_bytes())
        self.assertEqual([], self.reloads)

    def test_unrelated_change_during_reload_retains_unconfirmed_site(self):
        def changed(root):
            self.reload(root)
            self.other.write_bytes(b"external change during reload")
        with self.assertRaisesRegex(IngressError, "INGRESS_BASELINE_CHANGED"):
            self.apply(reload=changed)
        self.assertEqual(SITE, self.owned.read_bytes())
        self.assertEqual(b"external change during reload", self.other.read_bytes())
        self.assertEqual([SITE], self.reloads)
