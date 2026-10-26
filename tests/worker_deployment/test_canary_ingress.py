from pathlib import Path
import stat
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "ingress"))
from api_site import CANARY_SITE_NAME, SITE_NAME, IngressError, digest, install, rollback, snapshot

CANARY = b"screenreader.privensic.com { respond 200 }\n"
API = b"https://89.117.17.81 { respond 401 }\n"


class CanaryIngressTests(unittest.TestCase):
    """Real Linux file checks; injected Caddy callbacks do not prove HTTPS."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hsr-canary-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "sites").mkdir()
        self.main = self.root / "Caddyfile"
        self.main.write_text(f"import {self.root}/sites/*.caddy\n", encoding="utf-8")
        self.api = self.root / "sites" / SITE_NAME
        self.api.write_bytes(API)
        self.canary = self.root / "sites" / CANARY_SITE_NAME
        self.expected = snapshot(self.root, site_name=CANARY_SITE_NAME)
        self.reloads = []

    def reload(self, root):
        self.assertEqual(self.root, root)
        self.assertEqual(API, self.api.read_bytes())
        self.reloads.append(self.canary.read_bytes() if self.canary.exists() else None)

    def apply(self, validate=lambda path: None, reload=None, expected=None):
        return install(self.root, CANARY, expected or self.expected, digest(CANARY),
                       validate, reload or self.reload, site_name=CANARY_SITE_NAME)

    def test_canary_install_and_rollback_preserve_existing_api_and_main(self):
        original = self.main.read_bytes()
        result = self.apply()
        self.assertEqual("Installed", result["State"])
        self.assertFalse(result["TlsAccepted"])
        self.assertEqual(CANARY, self.canary.read_bytes())
        self.assertEqual(0o644, stat.S_IMODE(self.canary.stat().st_mode))
        result = rollback(self.root, self.expected, digest(CANARY), self.reload,
                          site_name=CANARY_SITE_NAME)
        self.assertEqual("RolledBack", result["State"])
        self.assertEqual([CANARY, None], self.reloads)
        self.assertFalse(self.canary.exists())
        self.assertEqual(API, self.api.read_bytes())
        self.assertEqual(original, self.main.read_bytes())
        self.assertEqual(self.expected, snapshot(self.root, site_name=CANARY_SITE_NAME))

    def test_api_baseline_cannot_be_borrowed_for_canary_install(self):
        with self.assertRaisesRegex(IngressError, "INGRESS_BASELINE_CHANGED"):
            self.apply(expected=snapshot(self.root))
        self.assertFalse(self.canary.exists())
        self.assertEqual(API, self.api.read_bytes())
        self.assertEqual([], self.reloads)

    def test_api_change_during_validation_prevents_canary_creation(self):
        def concurrent(path):
            self.api.write_bytes(b"external API change")
        with self.assertRaisesRegex(IngressError, "INGRESS_BASELINE_CHANGED"):
            self.apply(validate=concurrent)
        self.assertEqual(b"external API change", self.api.read_bytes())
        self.assertFalse(self.canary.exists())
        self.assertEqual([], self.reloads)

    def test_failed_reload_rolls_back_selected_canary_only(self):
        def fail_first(root):
            self.reload(root)
            if len(self.reloads) == 1:
                raise IngressError("SIMULATED_RELOAD_FAILURE")
        with self.assertRaisesRegex(IngressError, "INGRESS_ACTIVATION_FAILED_ROLLED_BACK"):
            self.apply(reload=fail_first)
        self.assertEqual([CANARY, None], self.reloads)
        self.assertFalse(self.canary.exists())
        self.assertEqual(API, self.api.read_bytes())

    def test_api_change_during_failure_denies_rollback_and_preserves_change(self):
        def concurrent(root):
            self.api.write_bytes(b"external API change")
            raise IngressError("SIMULATED_RELOAD_FAILURE")
        with self.assertRaisesRegex(IngressError, "INGRESS_ACTIVATION_AND_ROLLBACK_UNCONFIRMED"):
            self.apply(reload=concurrent)
        self.assertEqual(b"external API change", self.api.read_bytes())
        self.assertEqual(CANARY, self.canary.read_bytes())

    def test_different_existing_canary_is_not_overwritten(self):
        self.canary.write_bytes(b"external canary")
        with self.assertRaisesRegex(IngressError, "INGRESS_SITE_OWNERSHIP_DENIED"):
            self.apply()
        self.assertEqual(b"external canary", self.canary.read_bytes())
        self.assertEqual(API, self.api.read_bytes())
        self.assertEqual([], self.reloads)

    def test_canary_symlink_cannot_replace_or_remove_api(self):
        self.canary.symlink_to(self.api)
        with self.assertRaisesRegex(IngressError, "INGRESS_SITE_OWNERSHIP_DENIED"):
            self.apply()
        with self.assertRaisesRegex(IngressError, "INGRESS_ROLLBACK_OWNERSHIP_DENIED"):
            rollback(self.root, self.expected, digest(CANARY), self.reload,
                     site_name=CANARY_SITE_NAME)
        self.assertTrue(self.canary.is_symlink())
        self.assertEqual(API, self.api.read_bytes())
        self.assertEqual([], self.reloads)

    def test_unknown_and_traversal_site_names_are_denied_without_mutation(self):
        for name in ("other.caddy", "../Caddyfile", "/etc/caddy/Caddyfile", "sites/hsr-canary.caddy"):
            with self.subTest(name=name):
                with self.assertRaisesRegex(IngressError, "INGRESS_SITE_NAME_DENIED"):
                    install(self.root, CANARY, self.expected, digest(CANARY),
                            site_name=name)
                with self.assertRaisesRegex(IngressError, "INGRESS_SITE_NAME_DENIED"):
                    rollback(self.root, self.expected, digest(CANARY), site_name=name)
        self.assertFalse(self.canary.exists())
        self.assertEqual(API, self.api.read_bytes())
        self.assertEqual(self.expected, snapshot(self.root, site_name=CANARY_SITE_NAME))
