"""Source-bound static mock-site export checks; no live or fabricated account."""

import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path


spec = importlib.util.spec_from_file_location("mock_export", Path(__file__).with_name("export.py"))
exporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exporter)
REPO = Path(__file__).resolve().parents[2]


class MockSiteExportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "repo"
        for relative in ["ops/test-sites/v1", "src/HeadlessScreenReader.Browser.Playwright/Fixtures"]:
            shutil.copytree(REPO / relative, self.root / relative)
        (self.root / "ops/ingress").mkdir()
        shutil.copyfile(REPO / "ops/ingress/hsr-canary.caddy", self.root / "ops/ingress/hsr-canary.caddy")

    def change_manifest(self, change):
        path = self.root / "ops/test-sites/v1/manifest.json"
        manifest = json.loads(path.read_bytes())
        change(manifest)
        path.write_text(json.dumps(manifest), encoding="utf-8")

    def test_exports_exact_original_bytes_and_only_six_fixed_files(self):
        destination = Path(self.temp.name) / "release"
        digests = exporter.export(self.root, destination)
        self.assertEqual({"index.html", "manifest.json", *(f"{s}.html" for s in exporter.SCENARIOS)}, set(digests))
        for name, data in exporter.collect(self.root).items():
            self.assertEqual(data, (destination / name).read_bytes())

    def test_refuses_existing_destination_without_mutating_its_files(self):
        destination = Path(self.temp.name) / "release"
        destination.mkdir()
        (destination / "keep").write_bytes(b"retained")
        with self.assertRaises(FileExistsError):
            exporter.export(self.root, destination)
        self.assertEqual(["keep"], [p.name for p in destination.iterdir()])
        self.assertEqual(b"retained", (destination / "keep").read_bytes())

    def test_rejects_changed_fixture_before_creating_output(self):
        source = self.root / "src/HeadlessScreenReader.Browser.Playwright/Fixtures/mockcheckoutworkingv1.html"
        source.write_bytes(source.read_bytes() + b"changed")
        destination = Path(self.temp.name) / "release"
        with self.assertRaisesRegex(ValueError, "Fixture bytes changed"):
            exporter.export(self.root, destination)
        self.assertFalse(destination.exists())

    def test_rejects_wrong_version(self):
        self.change_manifest(lambda m: m.update(version=2))
        with self.assertRaisesRegex(ValueError, "Unsupported catalogue version"):
            exporter.collect(self.root)

    def test_rejects_missing_or_duplicate_scenarios(self):
        self.change_manifest(lambda m: m["scenarios"].__setitem__(1, m["scenarios"][0]))
        with self.assertRaisesRegex(ValueError, "scenario set"):
            exporter.collect(self.root)

    def test_rejects_changed_fixture_identity_and_path(self):
        for key, value in [("fixture", "../../escape"), ("path", "/api/private")]:
            with self.subTest(field=key):
                self.change_manifest(lambda m: m["scenarios"][0].update({key: value}))
                with self.assertRaisesRegex(ValueError, "identity or public path"):
                    exporter.collect(self.root)
                shutil.copyfile(REPO / "ops/test-sites/v1/manifest.json", self.root / "ops/test-sites/v1/manifest.json")

    def test_rejects_missing_script_hash_policy(self):
        path = self.root / "ops/ingress/hsr-canary.caddy"
        path.write_text(path.read_text().replace("'sha256-", "'removed-"))
        with self.assertRaisesRegex(ValueError, "exact fixture script hash"):
            exporter.collect(self.root)

    def test_rejects_missing_explicit_public_path(self):
        path = self.root / "ops/ingress/hsr-canary.caddy"
        path.write_text(path.read_text().replace(" /lab/v1/control-unnamed.html", ""))
        with self.assertRaisesRegex(ValueError, "explicitly allow each fixture path"):
            exporter.collect(self.root)

    def test_rejects_an_unexpected_script_even_with_an_updated_source_hash(self):
        import hashlib

        path = self.root / "src/HeadlessScreenReader.Browser.Playwright/Fixtures/mockcheckoutworkingv1.html"
        path.write_bytes(path.read_bytes() + b"<script>unexpected()</script>")
        self.change_manifest(lambda m: m["scenarios"][0].update(contentSha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        with self.assertRaisesRegex(ValueError, "one fixed inline script"):
            exporter.collect(self.root)

    def test_fixed_lab_is_ordered_before_consumer_fallback_and_unknown_paths_deny(self):
        caddy = (self.root / "ops/ingress/hsr-canary.caddy").read_text()
        self.assertLess(caddy.index("@lab path"), caddy.index("import /etc/caddy/hsr-ui/*.caddy"))
        self.assertIn("handle /lab/* {\n\t\t\trespond 404", caddy)
        self.assertNotIn("unsafe-inline", caddy.split("script-src", 1)[1].split(";", 1)[0])


if __name__ == "__main__":
    unittest.main()
