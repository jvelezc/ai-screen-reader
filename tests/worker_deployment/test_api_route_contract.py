from pathlib import Path
import unittest


class ApiRouteContractTests(unittest.TestCase):
    """Static route coverage; live Caddy/TLS and core authorization are separate checks."""

    def forwarded(self, path):
        site = (Path(__file__).resolve().parents[2] / "ops" / "ingress" / "hsr-api-ip.caddy").read_text(encoding="utf-8")
        declarations = [line.split()[2:] for line in site.splitlines() if line.strip().startswith("@core path ")]
        self.assertEqual(1, len(declarations))
        for pattern in declarations[0]:
            self.assertTrue("*" not in pattern or (pattern.endswith("/*") and pattern.count("*") == 1))
            if path == pattern or (pattern.endswith("/*") and path.startswith(pattern[:-1])):
                return True
        return False

    def test_caller_workspace_catalog_paths_are_forwarded_to_core(self):
        for path in (
            "/api/workspaces",
            "/api/workspaces/page/a1000000-0000-4000-8000-000000000001",
            "/api/workspaces/a1000000-0000-4000-8000-000000000001",
            "/api/workspaces/a1000000-0000-4000-8000-000000000001/scans",
        ):
            with self.subTest(path=path):
                self.assertTrue(self.forwarded(path), path)

    def test_private_portal_and_mcp_paths_are_not_forwarded(self):
        for path in ("/", "/mcp", "/host.json", "/portal.js", "/api/config", "/api/workspaces-other"):
            with self.subTest(path=path):
                self.assertFalse(self.forwarded(path), path)
