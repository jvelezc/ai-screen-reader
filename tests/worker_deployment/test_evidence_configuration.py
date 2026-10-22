import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
import host_runtime
import control_start
import worker_start
from release_bundle import ReleaseError

ACCESS = "s3-operational-synthetic-access"
SECRET = "s3-operational-synthetic-secret-only"
PAIR = {"HSR_EVIDENCE_S3_ACCESS_KEY": ACCESS, "HSR_EVIDENCE_S3_SECRET": SECRET}


class EvidenceConfigurationTests(unittest.TestCase):
    def test_only_complete_s3_authority_is_copied(self):
        self.assertEqual({}, host_runtime.evidence_environment({"SUPABASE_ACCESS_TOKEN": "unrelated"}))
        self.assertEqual(PAIR, host_runtime.evidence_environment(PAIR | {"HSR_WORKER_DATABASE": "unrelated"}))
        for source in [{"HSR_EVIDENCE_S3_ACCESS_KEY": ACCESS}, {"HSR_EVIDENCE_S3_SECRET": SECRET},
                PAIR | {"HSR_EVIDENCE_S3_ACCESS_KEY": "sbp_" + "a" * 40},
                PAIR | {"HSR_EVIDENCE_S3_SECRET": "sb_secret_" + "a" * 40},
                PAIR | {"HSR_EVIDENCE_S3_SECRET": SECRET + "\n"},
                PAIR | {"HSR_EVIDENCE_S3_SECRET": "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.signature"}]:
            with self.subTest(source=list(source)), self.assertRaises(ReleaseError) as error:
                host_runtime.evidence_environment(source)
            self.assertEqual("EVIDENCE_S3_CONFIGURATION_INVALID", str(error.exception))
            self.assertNotIn(SECRET, str(error.exception))

    def test_root_file_is_optional_but_existing_file_requires_exact_private_shape(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder); (base / "config").mkdir()
            host_runtime.validate_evidence_configuration(base)
            path = base / "config" / "evidence.env"
            text = "HSR_EVIDENCE_S3_ACCESS_KEY=" + ACCESS + "\nHSR_EVIDENCE_S3_SECRET=" + SECRET + "\n"
            path.write_text(text); path.chmod(0o600)
            actual_stat = os.fstat
            def owned(fd):
                facts = actual_stat(fd)
                return SimpleNamespace(st_uid=0, st_mode=facts.st_mode, st_size=facts.st_size)
            with patch("host_runtime.os.fstat", side_effect=owned):
                host_runtime.validate_evidence_configuration(base)
                for value in [text + "HSR_WORKER_DATABASE=unrelated\n", text + text, text.rstrip("\n"), "x" * 513,
                        text.replace(ACCESS, "sbp_" + "a" * 40)]:
                    path.write_text(value)
                    with self.assertRaises(ReleaseError): host_runtime.validate_evidence_configuration(base)
                path.write_text(text); path.chmod(0o644)
                with self.assertRaises(ReleaseError): host_runtime.validate_evidence_configuration(base)
            path.chmod(0o600)
            with patch("host_runtime.os.fstat", return_value=SimpleNamespace(st_uid=10042, st_mode=0o100600, st_size=100)):
                with self.assertRaises(ReleaseError): host_runtime.validate_evidence_configuration(base)
            sentinel = base / "unrelated"; sentinel.write_bytes(b"retain operator bytes")
            path.unlink(); path.symlink_to(sentinel)
            with self.assertRaises(OSError): host_runtime.validate_evidence_configuration(base)
            self.assertEqual(b"retain operator bytes", sentinel.read_bytes())

    def test_worker_and_control_entrypoints_forward_only_the_valid_pair_to_server_processes(self):
        base = Path("/srv/headless-screen-reader"); commit = "b" * 40
        runtime = Mock(account=SimpleNamespace(pw_uid=10042))
        runtime.environment.return_value = {name: name for name in ("PATH", "HOME", "USER", "LOGNAME", "XDG_RUNTIME_DIR",
            "DBUS_SESSION_BUS_ADDRESS", "DOTNET_NOLOGO", "DOTNET_CLI_TELEMETRY_OPTOUT")}
        runtime.manifest.return_value = {"schema": "hsr-worker-control-release-v2", "image": "sha256:" + "c" * 64}
        for module, mode, database in [(worker_start, "--poll", True), (control_start, "--hosted-control", False)]:
            with self.subTest(mode=mode), patch.object(module, "BASE", base), \
                    patch.object(module, "HostRuntime", return_value=runtime), \
                    patch.object(module, "current_commit", return_value=commit), \
                    patch.object(module.os, "geteuid", return_value=10042), \
                    patch.dict(os.environ, PAIR | {"HSR_CONTROL_PUBLISHABLE_KEY": "sb_publishable_operational_fixture",
                        "HSR_WORKER_DATABASE": "database-canary", "SUPABASE_ACCESS_TOKEN": "management-canary"}, clear=True), \
                    patch.object(module.os, "execve", side_effect=RuntimeError("exec-boundary")) as execute:
                with self.assertRaisesRegex(RuntimeError, "exec-boundary"): module.main()
            args = execute.call_args.args
            self.assertEqual(mode, args[1][-1]); self.assertEqual(ACCESS, args[2]["HSR_EVIDENCE_S3_ACCESS_KEY"])
            self.assertEqual(SECRET, args[2]["HSR_EVIDENCE_S3_SECRET"])
            self.assertEqual(database, "HSR_WORKER_DATABASE" in args[2])
            self.assertNotIn("management-canary", str(args)); self.assertNotIn(SECRET, str(args[1]))

    def test_native_readiness_does_not_inherit_s3_or_database_credentials(self):
        base = Path("/srv/headless-screen-reader")
        with patch("host_runtime.pwd.getpwnam", return_value=SimpleNamespace(pw_uid=10042, pw_dir=str(base / "home"))):
            runtime = host_runtime.HostRuntime(base)
        with patch.dict(os.environ, PAIR | {"HSR_WORKER_DATABASE": "database-canary"}), \
                patch.object(runtime, "manifest", return_value={"image": "sha256:" + "c" * 64}), \
                patch("host_runtime.run_checked", return_value="{}") as run:
            runtime.worker("b" * 40, "readiness", "--check-runtime")
        env = run.call_args.kwargs["env"]
        self.assertNotIn("HSR_WORKER_DATABASE", env); self.assertNotIn("HSR_EVIDENCE_S3_ACCESS_KEY", env)
        self.assertNotIn("HSR_EVIDENCE_S3_SECRET", env)

    def test_invalid_s3_file_prevents_native_probe_before_promotion(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder); (base / "config").mkdir()
            path = base / "config" / "evidence.env"; path.write_text("invalid\n"); path.chmod(0o600)
            with patch("host_runtime.pwd.getpwnam", return_value=SimpleNamespace(pw_uid=10042, pw_dir=str(base / "home"))):
                runtime = host_runtime.HostRuntime(base)
            with patch.object(runtime, "worker") as native:
                with self.assertRaises(ReleaseError) as error: runtime.probe("b" * 40)
            self.assertEqual("EVIDENCE_S3_CONFIGURATION_INVALID", str(error.exception))
            native.assert_not_called()


if __name__ == "__main__":
    unittest.main()
