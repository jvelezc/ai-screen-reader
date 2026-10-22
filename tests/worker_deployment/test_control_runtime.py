import base64
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch, Mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
import control_runtime as control
import control_start
import host_runtime
import provision
from activation import activate, current_commit, replace_current
from release_bundle import ReleaseError

OLD, NEW = "a" * 40, "b" * 40
KEY = "sb_publishable_control_runtime_fixture"


class ControlRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hsr-control-proof-")
        self.base = Path(self.temporary.name)
        for commit, schema in [(OLD, "hsr-worker-release-v1"), (NEW, "hsr-worker-control-release-v2")]:
            folder = self.base / "releases" / commit
            folder.mkdir(parents=True)
            (folder / "manifest.json").write_text(json.dumps({"schema": schema, "commit": commit,
                "image": "sha256:" + "c" * 64, "files": {}}))
        account = SimpleNamespace(pw_uid=10042, pw_dir=str(self.base / "home"))
        with patch("host_runtime.pwd.getpwnam", return_value=account):
            self.runtime = control.DeploymentRuntime(self.base)
        self.service = self.runtime.control

    def tearDown(self):
        self.temporary.cleanup()

    def proof(self):
        return {"Schema": "hsr-control-runtime-proof-v1", "State": "ControlRuntimeReady", "ReleaseCommit": NEW,
            "PublicConfigurationRead": True, "UnauthenticatedSessionDenied": True, "ServerCloseConfirmed": True,
            "AppJwtValidated": False, "PublicMcpEnabled": False}

    def journal(self):
        return {"Schema": "hsr-control-readiness-v1", "State": "Listening", "ReleaseCommit": NEW,
                "AppJwtValidated": False, "PublicMcpEnabled": False}

    def http_body(self):
        return {"schema": "hsr-control-readiness-v1", "state": "Listening", "releaseCommit": NEW,
                "appJwtValidated": False, "publicMcpEnabled": False}

    def test_control_environment_contains_only_public_configuration_and_closed_runtime_fields(self):
        with patch.dict(os.environ, {"HSR_WORKER_DATABASE": "private-database-canary", "SUPABASE_ACCESS_TOKEN": "private-management-canary"}):
            env = control.control_environment(self.base, NEW, self.runtime.environment(), KEY)
        self.assertEqual(KEY, env["HSR_CONTROL_PUBLISHABLE_KEY"])
        self.assertEqual("8086", env["HSR_CONTROL_PORT"])
        self.assertEqual("http://localhost:6086", env["HSR_CONTROL_ORIGIN"])
        self.assertEqual(str(self.base / "state" / "evidence"), env["HSR_CONTROL_EVIDENCE"])
        self.assertNotIn("private-", json.dumps(env))
        with self.assertRaises(ReleaseError):
            control.control_environment(self.base, NEW, self.runtime.environment() | {"HSR_WORKER_DATABASE": "private"}, KEY)

    def test_public_key_guards_reject_privileged_other_project_duplicate_and_injected_values(self):
        def legacy(value):
            payload = base64.urlsafe_b64encode(value.encode()).decode().rstrip("=")
            return "eyJhbGciOiJIUzI1NiJ9." + payload + ".c2ln"
        good = legacy('{"role":"anon","ref":"gpnijaxrgtwzwtrqsbcu"}')
        for checker in (control.valid_public_key, provision.public_portal_key):
            self.assertTrue(checker(KEY)); self.assertTrue(checker(good))
            for label, key in {"secret": "sb_secret_control_fixture", "newline": KEY + "\n",
                "service": legacy('{"role":"service_role","ref":"gpnijaxrgtwzwtrqsbcu"}'),
                "other": legacy('{"role":"anon","ref":"other"}'),
                "duplicate": legacy('{"role":"service_role","role":"anon","ref":"gpnijaxrgtwzwtrqsbcu"}'),
                "array": legacy('[]'), "large": KEY * 200, "missing": None}.items():
                with self.subTest(case=label, checker=checker.__name__):
                    self.assertFalse(checker(key))

    def test_probe_executes_candidate_closure_without_database_or_tenant_claim(self):
        with patch.object(self.service, "public_key", return_value=KEY), \
                patch("control_runtime.run_checked", return_value=json.dumps(self.proof())) as run:
            self.service.probe(NEW)
        args, kwargs = run.call_args
        self.assertEqual(["/usr/sbin/runuser", "-u", "hsr-worker", "--", host_runtime.DOTNET,
            str(self.base / "releases" / NEW / "control" / "HeadlessScreenReader.Cli.dll"), "--check-control"], args[0])
        self.assertEqual(30, kwargs["timeout"])
        self.assertNotIn("HSR_WORKER_DATABASE", kwargs["env"])
        self.assertEqual(NEW, kwargs["env"]["HSR_RELEASE_COMMIT"])

    def test_root_public_configuration_rejects_symlinks_foreign_owners_and_open_permissions(self):
        folder = self.base / "config"; folder.mkdir()
        path = folder / "control.env"; path.write_text("HSR_CONTROL_PUBLISHABLE_KEY=" + KEY + "\n"); path.chmod(0o600)
        actual_stat = os.fstat
        def owned(fd):
            value = actual_stat(fd)
            return SimpleNamespace(st_uid=0, st_mode=value.st_mode, st_size=value.st_size)
        with patch("control_runtime.os.fstat", side_effect=owned):
            self.assertEqual(KEY, self.service.public_key())
            path.chmod(0o644)
            with self.assertRaises(ReleaseError): self.service.public_key()
            path.chmod(0o600); path.write_text("x" * 4201)
            with self.assertRaises(ReleaseError): self.service.public_key()
        path.write_text("HSR_CONTROL_PUBLISHABLE_KEY=" + KEY + "\n")
        with patch("control_runtime.os.fstat", return_value=SimpleNamespace(st_uid=10042, st_mode=0o100600, st_size=100)):
            with self.assertRaises(ReleaseError): self.service.public_key()
        sentinel = folder / "unrelated"; sentinel.write_bytes(b"retain operator bytes")
        path.unlink(); path.symlink_to(sentinel)
        with self.assertRaises(OSError): self.service.public_key()
        self.assertEqual(b"retain operator bytes", sentinel.read_bytes())

    def test_v1_probe_skips_control_while_v2_checks_worker_then_control(self):
        events = []
        with patch.object(host_runtime.HostRuntime, "probe", side_effect=lambda commit: events.append("worker:" + commit)), \
                patch.object(self.service, "probe", side_effect=lambda commit: events.append("control:" + commit)):
            self.runtime.probe(OLD); self.runtime.probe(NEW)
        self.assertEqual(["worker:" + OLD, "worker:" + NEW, "control:" + NEW], events)

    def test_v2_starts_control_after_database_readiness_and_enables_both_owned_units(self):
        replace_current(self.base, NEW); events = []
        with patch.object(host_runtime.HostRuntime, "start_ready", side_effect=lambda: events.append("database-ready")), \
                patch.object(self.service, "start_ready", side_effect=lambda commit: events.append("control-ready:" + commit)), \
                patch.object(host_runtime.HostRuntime, "enable", side_effect=lambda: events.append("worker-enable")), \
                patch.object(self.service, "enable", side_effect=lambda: events.append("control-enable")):
            self.runtime.start_ready(); self.runtime.enable()
        self.assertEqual(["database-ready", "control-ready:" + NEW, "worker-enable", "control-enable"], events)

    def test_v1_enable_keeps_portal_disabled_and_database_failure_never_starts_portal(self):
        replace_current(self.base, OLD)
        with patch.object(host_runtime.HostRuntime, "enable") as worker, patch.object(self.service, "disable") as disable, \
                patch.object(self.service, "enable") as enable:
            self.runtime.enable()
        worker.assert_called_once(); disable.assert_called_once(); enable.assert_not_called()
        with patch.object(host_runtime.HostRuntime, "start_ready", side_effect=ReleaseError("DEPLOY_DATABASE_READINESS_UNCONFIRMED")), \
                patch.object(self.service, "start_ready") as start, self.assertRaises(ReleaseError):
            self.runtime.start_ready()
        start.assert_not_called()

    def test_probe_refuses_unjoined_or_invented_acceptance_and_wrong_release(self):
        changes = {"open": {"ServerCloseConfirmed": False}, "identity": {"AppJwtValidated": True},
            "public": {"PublicMcpEnabled": True}, "wrong": {"ReleaseCommit": OLD}, "numeric": {"ServerCloseConfirmed": 1},
            "extra": {"tenantAccepted": True}}
        for label, change in changes.items():
            with self.subTest(case=label), patch.object(self.service, "public_key", return_value=KEY), \
                    patch("control_runtime.run_checked", return_value=json.dumps(self.proof() | change)):
                with self.assertRaises(ReleaseError) as error:
                    self.service.probe(NEW)
                self.assertEqual("CONTROL_RUNTIME_UNCONFIRMED", str(error.exception))

    def test_portal_stop_must_settle_before_worker_stop(self):
        events = []
        with patch.object(self.service, "stop", side_effect=lambda: events.append("portal")), \
                patch.object(host_runtime.HostRuntime, "stop", side_effect=lambda: events.append("worker")):
            self.runtime.stop()
        self.assertEqual(["portal", "worker"], events)
        with patch.object(self.service, "stop", side_effect=ReleaseError("CONTROL_STOP_UNCONFIRMED")), \
                patch.object(host_runtime.HostRuntime, "stop") as worker:
            with self.assertRaises(ReleaseError): self.runtime.stop()
        worker.assert_not_called()

    def test_control_stop_requires_zero_pid_and_inactive_or_failed_state(self):
        for state in ("inactive", "failed"):
            with patch("control_runtime.run_checked", side_effect=["", state, "0"]) as run:
                self.service.stop()
            self.assertEqual(["/usr/bin/systemctl", "stop", control.CONTROL_UNIT], run.call_args_list[0].args[0])
        for state, pid in [("active", "123"), ("inactive", "123"), ("deactivating", "0")]:
            with patch("control_runtime.run_checked", side_effect=["", state, pid]), self.assertRaises(ReleaseError) as error:
                self.service.stop()
            self.assertEqual("CONTROL_STOP_UNCONFIRMED", str(error.exception))

    def test_readiness_requires_fresh_journal_actual_http_and_same_active_invocation(self):
        invocation = "d" * 32
        status = "InvocationID=" + invocation + "\nActiveState=active\nMainPID=123"
        with patch("control_runtime.run_checked", side_effect=["inactive", "", invocation, "active", json.dumps(self.journal()), status]) as run, \
                patch.object(self.service, "http_ready") as http:
            self.service.start_ready(NEW)
        self.assertIn("_SYSTEMD_INVOCATION_ID=" + invocation, run.call_args_list[4].args[0])
        http.assert_called_once_with(NEW)
        self.assertEqual(6, run.call_count)

    def test_failed_unit_resets_before_start_but_active_or_unknown_never_start(self):
        status = "InvocationID=" + "d" * 32 + "\nActiveState=active\nMainPID=123"
        with patch("control_runtime.run_checked", side_effect=["failed", "", "", "d" * 32, "active", json.dumps(self.journal()), status]) as run, \
                patch.object(self.service, "http_ready"):
            self.service.start_ready(NEW)
        self.assertEqual(["/usr/bin/systemctl", "reset-failed", control.CONTROL_UNIT], run.call_args_list[1].args[0])
        for state in ("active", "activating", "unknown", ""):
            with patch("control_runtime.run_checked", return_value=state) as run, self.assertRaises(ReleaseError):
                self.service.start_ready(NEW)
            self.assertEqual(1, run.call_count)

    def test_http_failure_or_replaced_or_exited_process_cannot_be_ready(self):
        for label, status in {"changed": "InvocationID=" + "e" * 32 + "\nActiveState=active\nMainPID=123",
            "exited": "InvocationID=" + "d" * 32 + "\nActiveState=inactive\nMainPID=0",
            "zero": "InvocationID=" + "d" * 32 + "\nActiveState=active\nMainPID=0"}.items():
            with self.subTest(case=label), patch("control_runtime.run_checked", side_effect=["inactive", "", "d" * 32,
                    "active", json.dumps(self.journal()), status]), patch.object(self.service, "http_ready"), self.assertRaises(ReleaseError):
                self.service.start_ready(NEW)
        with patch("control_runtime.run_checked", side_effect=["inactive", "", "d" * 32, "active", json.dumps(self.journal())]), \
                patch.object(self.service, "http_ready", side_effect=ReleaseError("CONTROL_HTTP_UNCONFIRMED")), self.assertRaises(ReleaseError):
            self.service.start_ready(NEW)

    def test_missing_current_journal_receipt_times_out_without_http_shortcut(self):
        with patch("control_runtime.run_checked", side_effect=["inactive", "", "d" * 32]), \
                patch("control_runtime.time.monotonic", side_effect=[0, 31]), patch.object(self.service, "http_ready") as http:
            with self.assertRaises(ReleaseError) as error: self.service.start_ready(NEW)
        self.assertEqual("CONTROL_READINESS_UNCONFIRMED", str(error.exception)); http.assert_not_called()

    def test_wrong_commit_or_nonboolean_journal_cannot_reuse_readiness(self):
        for label, change in {"commit": {"ReleaseCommit": OLD}, "numeric": {"AppJwtValidated": 0}, "identity": {"AppJwtValidated": True}}.items():
            with self.subTest(case=label), patch("control_runtime.run_checked", side_effect=["inactive", "", "d" * 32,
                    "active", json.dumps(self.journal() | change)]), patch("control_runtime.time.monotonic", side_effect=[0, 1, 31]), \
                    patch("control_runtime.time.sleep"), patch.object(self.service, "http_ready") as http:
                with self.assertRaises(ReleaseError): self.service.start_ready(NEW)
            http.assert_not_called()

    def test_http_readiness_is_fixed_loopback_bounded_and_never_implies_a_user(self):
        connection = Mock(); response = connection.getresponse.return_value
        response.status = 200; response.read.return_value = json.dumps(self.http_body()).encode()
        with patch("control_runtime.http.client.HTTPConnection", return_value=connection) as start:
            self.service.http_ready(NEW)
        start.assert_called_once_with("127.0.0.1", 8086, timeout=3)
        connection.request.assert_called_once_with("GET", "/internal/ready")
        response.read.assert_called_once_with(4097); connection.close.assert_called_once()
        for label, status, body in [("wrong", 200, json.dumps(self.http_body() | {"releaseCommit": OLD}).encode()),
                ("numeric", 200, json.dumps(self.http_body() | {"appJwtValidated": 0}).encode()),
                ("identity", 200, json.dumps(self.http_body() | {"appJwtValidated": True}).encode()),
                ("large", 200, b"x" * 4097), ("status", 503, b"{}"), ("malformed", 200, b"private-canary")]:
            connection.reset_mock(); response.status = status; response.read.return_value = body
            with self.subTest(case=label), patch("control_runtime.http.client.HTTPConnection", return_value=connection), self.assertRaises(ReleaseError) as error:
                self.service.http_ready(NEW)
            self.assertEqual("CONTROL_HTTP_UNCONFIRMED", str(error.exception)); connection.close.assert_called_once()

    def test_v1_rollback_drains_failed_portal_then_recovers_and_restarts_only_previous_worker(self):
        replace_current(self.base, OLD)
        events = []
        def event(name): events.append((name, current_commit(self.base)))
        def control_start(commit):
            event("portal-start:" + commit)
            raise ReleaseError("CONTROL_START_FAILED")
        with patch.object(self.service, "stop", side_effect=lambda: event("portal-stop")), \
                patch.object(self.service, "probe", side_effect=lambda commit: event("portal-probe:" + commit)), \
                patch.object(self.service, "start_ready", side_effect=control_start), \
                patch.object(self.service, "disable", side_effect=lambda: event("portal-disable")), \
                patch.object(host_runtime.HostRuntime, "stop", side_effect=lambda: event("worker-stop")), \
                patch.object(host_runtime.HostRuntime, "probe", side_effect=lambda commit: event("worker-probe:" + commit)), \
                patch.object(host_runtime.HostRuntime, "recover", side_effect=lambda commit, scope: event("recover:" + commit + ":" + scope)), \
                patch.object(host_runtime.HostRuntime, "start_ready", side_effect=lambda: event("worker-start")):
            result = activate(self.base, NEW, self.runtime, lambda _: None)
        self.assertEqual("RolledBack", result["State"]); self.assertEqual(OLD, current_commit(self.base))
        self.assertEqual([("portal-stop", OLD), ("worker-stop", OLD), ("recover:" + OLD + ":evidence", OLD),
            ("worker-probe:" + NEW, OLD), ("portal-probe:" + NEW, OLD), ("worker-start", NEW), ("portal-start:" + NEW, NEW),
            ("portal-stop", NEW), ("worker-stop", NEW), ("recover:" + NEW + ":evidence", NEW),
            ("recover:" + NEW + ":readiness", NEW), ("worker-probe:" + OLD, NEW), ("worker-start", OLD), ("portal-disable", OLD)], events)

    def test_control_entrypoint_executes_fixed_mode_with_no_inherited_database_environment(self):
        replace_current(self.base, NEW)
        with patch.object(control_start, "BASE", self.base), patch.object(control_start, "HostRuntime", return_value=self.runtime), \
                patch("control_start.os.geteuid", return_value=10042), \
                patch.dict(os.environ, {"HSR_CONTROL_PUBLISHABLE_KEY": KEY, "HSR_WORKER_DATABASE": "private-database-canary"}), \
                patch("control_start.os.execve", side_effect=RuntimeError("exec-boundary")) as execute:
            with self.assertRaisesRegex(RuntimeError, "exec-boundary"): control_start.main()
        args = execute.call_args.args
        self.assertEqual([host_runtime.DOTNET, str(self.base / "releases" / NEW / "control" / "HeadlessScreenReader.Cli.dll"), "--hosted-control"], args[1])
        self.assertNotIn("HSR_WORKER_DATABASE", args[2]); self.assertNotIn("private-database-canary", str(args))

    def test_control_entrypoint_refuses_root_wrong_release_and_privileged_key(self):
        for label, commit, uid, key in [("root", NEW, 0, KEY), ("worker-only", OLD, 10042, KEY),
                ("privileged", NEW, 10042, "sb_secret_invalid_configuration")]:
            replace_current(self.base, commit)
            with self.subTest(case=label), patch.object(control_start, "BASE", self.base), \
                    patch.object(control_start, "HostRuntime", return_value=self.runtime), patch("control_start.os.geteuid", return_value=uid), \
                    patch.dict(os.environ, {"HSR_CONTROL_PUBLISHABLE_KEY": key}), patch("control_start.os.execve") as execute:
                with self.assertRaises(ReleaseError): control_start.main()
            execute.assert_not_called()


if __name__ == "__main__":
    unittest.main()
