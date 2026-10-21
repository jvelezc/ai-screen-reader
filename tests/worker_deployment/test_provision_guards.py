import base64
import os
from pathlib import Path
import stat
import sys
import tempfile
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
import provision


class ProvisionGuardsTests(unittest.TestCase):
    def setUp(self):
        self.certificate = (Path(__file__).resolve().parents[2] / "ops" / "worker" / "supabase-root.crt").read_bytes()
        wire = b"\x00\x00\x00\x0bssh-ed25519\x00\x00\x00\x20" + bytes(32)
        self.config = {"Project": "gpnijaxrgtwzwtrqsbcu", "Commit": "a" * 40,
                       "PublicKey": "ssh-ed25519 " + base64.b64encode(wire).decode() + " unit-fixture",
                       "Password": "b" * 64, "Ca": base64.b64encode(self.certificate).decode()}
        self.sources = {name: "pass\n" for name in provision.MODULES}

    def test_exact_project_key_certificate_and_module_inventory_are_validated_before_mutation(self):
        self.assertEqual(self.certificate, provision.validate(self.config, self.sources))

    def test_wrong_project_password_or_commit_cannot_enter_bootstrap(self):
        cases = {"project": {"Project": "another-project"}, "password": {"Password": "quoted'password"},
                 "commit": {"Commit": "latest"}, "extra": {"RootPath": "/etc"}}
        for label, change in cases.items():
            with self.subTest(case=label), self.assertRaises(provision.ProvisionError) as caught:
                provision.validate(self.config | change, self.sources)
            self.assertEqual("PROVISION_INPUT_OR_OWNERSHIP_INVALID", str(caught.exception))

    def test_public_key_cannot_inject_forced_commands_or_another_authority(self):
        cases = {"newline": self.config["PublicKey"] + "\nother-key",
                 "options": 'command="arbitrary" ' + self.config["PublicKey"],
                 "wrong_wire": "ssh-ed25519 " + base64.b64encode(bytes(51)).decode(),
                 "other_type": self.config["PublicKey"].replace("ssh-ed25519 ", "ssh-rsa ", 1)}
        for label, key in cases.items():
            with self.subTest(case=label), self.assertRaises(provision.ProvisionError):
                provision.validate(self.config | {"PublicKey": key}, self.sources)

    def test_unpinned_certificate_cannot_weaken_database_trust(self):
        with self.assertRaises(provision.ProvisionError):
            provision.validate(self.config | {"Ca": base64.b64encode(b"another certificate").decode()}, self.sources)

    def test_unknown_module_name_cannot_overwrite_a_host_file(self):
        for label, sources in {"extra": self.sources | {"../outside": "pass"},
                               "missing": {name: value for name, value in self.sources.items() if name != "deploy.py"}}.items():
            with self.subTest(case=label), self.assertRaises(provision.ProvisionError):
                provision.validate(self.config, sources)

    def test_module_size_and_syntax_are_checked_before_host_installation(self):
        with self.assertRaises(provision.ProvisionError):
            provision.validate(self.config, self.sources | {"deploy.py": "x" * (128 * 1024 + 1)})
        with self.assertRaises(SyntaxError):
            provision.validate(self.config, self.sources | {"deploy.py": "def invalid("})

    def test_existing_account_requires_managed_nonroot_identity_and_exact_home(self):
        home = Path("/srv/headless-screen-reader/home")
        account = SimpleNamespace(pw_uid=10042, pw_gid=10042, pw_dir=str(home),
                                  pw_gecos="HeadlessScreenReader managed hsr-worker")
        provision.account_valid(account, "hsr-worker", home, True)
        for label, values, managed in [("unmanaged", {}, False), ("root", {"pw_uid": 0}, True),
                                        ("root_group", {"pw_gid": 0}, True),
                                        ("home", {"pw_dir": "/another-app"}, True),
                                        ("comment", {"pw_gecos": "another account"}, True)]:
            changed = SimpleNamespace(**(vars(account) | values))
            with self.subTest(case=label), self.assertRaises(provision.ProvisionError):
                provision.account_valid(changed, "hsr-worker", home, managed)

    def test_private_directory_ownership_uses_actual_open_directory_and_rejects_symlinks(self):
        with tempfile.TemporaryDirectory(prefix="hsr-directory-proof-") as name:
            root = Path(name)
            owned = root / "owned"
            provision.own_private_directory(owned, os.getuid(), os.getgid())
            self.assertEqual(0o700, stat.S_IMODE(owned.stat().st_mode))
            unrelated = root / "unrelated"
            unrelated.mkdir(mode=0o755)
            prior = unrelated.stat()
            link = root / "link"
            link.symlink_to(unrelated, target_is_directory=True)
            with self.assertRaises(OSError):
                provision.own_private_directory(link, os.getuid(), os.getgid())
            self.assertEqual(stat.S_IMODE(prior.st_mode), stat.S_IMODE(unrelated.stat().st_mode))
            self.assertEqual(prior.st_uid, unrelated.stat().st_uid)


if __name__ == "__main__":
    unittest.main()
