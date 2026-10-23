import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
import host_runtime
from release_bundle import ReleaseError

COMMIT = "a" * 40
IMAGE = "sha256:" + "b" * 64


class HostRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hsr-host-runtime-proof-")
        self.base = Path(self.temporary.name)
        folder = self.base / "releases" / COMMIT
        folder.mkdir(parents=True)
        (folder / "manifest.json").write_text(json.dumps({"schema": "hsr-worker-release-v1",
            "commit": COMMIT, "image": IMAGE, "files": {}}))
        account = SimpleNamespace(pw_uid=10042, pw_dir=str(self.base / "home"))
        with patch("host_runtime.pwd.getpwnam", return_value=account):
            self.runtime = host_runtime.HostRuntime(self.base)

    def tearDown(self):
        self.temporary.cleanup()

    def ready(self):
        return {"Schema": "hsr-runtime-readiness-v1", "State": "RuntimeReady", "Image": IMAGE,
                "ObservedNodes": 1, "Sha256": "c" * 64, "DatabaseAccessed": False,
                "ObservationPersisted": False, "DispatchedInputs": 0, "GoalVerified": False,
                "Recovered": 0}

    def test_operator_environment_is_closed_to_database_and_management_credentials(self):
        with patch.dict(os.environ, {"HSR_WORKER_DATABASE": "private-database-canary",
                                    "SUPABASE_ACCESS_TOKEN": "private-management-canary"}):
            env = self.runtime.environment()
        self.assertEqual({"PATH", "HOME", "USER", "LOGNAME", "XDG_RUNTIME_DIR",
                          "DBUS_SESSION_BUS_ADDRESS", "DOTNET_NOLOGO", "DOTNET_CLI_TELEMETRY_OPTOUT"}, set(env))
        self.assertEqual(str(self.base / "home"), env["HOME"])
        self.assertEqual("/run/user/10042", env["XDG_RUNTIME_DIR"])
        self.assertNotIn("private-", json.dumps(env))

    def test_recovery_uses_fixed_narrow_mode_and_never_passes_database_arguments(self):
        with patch.dict(os.environ, {"HSR_WORKER_DATABASE": "private-database-canary"}):
            with patch("host_runtime.run_checked", return_value='{"State":"Recovered","Recovered":0}') as run:
                self.runtime.recover(COMMIT, "evidence")
        args, kwargs = run.call_args
        self.assertEqual(["/usr/sbin/runuser", "-u", "hsr-worker", "--", "/usr/local/bin/dotnet",
                          str(self.base / "releases" / COMMIT / "worker" / "HeadlessScreenReader.Worker.dll"),
                          "--recover"], args[0])
        self.assertEqual(190, kwargs["timeout"])
        self.assertEqual(str(self.base / "state" / "evidence"), kwargs["env"]["HSR_WORKER_EVIDENCE"])
        self.assertEqual(IMAGE, kwargs["env"]["HSR_WORKER_IMAGE"])
        self.assertNotIn("HSR_WORKER_DATABASE", kwargs["env"])
        self.assertNotIn("private-database-canary", str(run.call_args))

    def test_unknown_scope_or_mode_cannot_invoke_a_host_command(self):
        for label, scope, mode in [("scope", "../config", "--recover"),
                                    ("mode", "readiness", "--poll")]:
            with self.subTest(case=label), patch("host_runtime.run_checked") as run:
                with self.assertRaises(ReleaseError) as caught:
                    self.runtime.worker(COMMIT, scope, mode)
                self.assertEqual("DEPLOY_OPERATION_INVALID", str(caught.exception))
                run.assert_not_called()

    def test_reader_recovery_runs_only_after_queue_recovery_and_requires_empty_inventory(self):
        pending = self.base / "state" / "evidence" / ".reader-runtime" / "intents"
        pending.mkdir(parents=True)
        intent = pending / "owned.json"
        intent.write_text("durable ownership sentinel")
        calls = []
        def recover(commit, scope, mode):
            calls.append((commit, scope, mode))
            if mode == "--recover":
                self.assertTrue(intent.exists())
                return {"State": "Recovered", "Recovered": 0}
            intent.unlink()
            return {"Schema": "hsr-reader-recovery-v1", "State": "ReaderRecovered", "Recovered": 1}
        with patch.object(self.runtime, "worker", side_effect=recover):
            self.runtime.recover(COMMIT, "evidence")
        self.assertEqual([(COMMIT, "evidence", "--recover"), (COMMIT, "evidence", "--recover-reader")], calls)
        self.assertEqual([], list(pending.iterdir()))

    def test_reader_recovery_cannot_claim_success_while_an_intent_remains(self):
        pending = self.base / "state" / "evidence" / ".reader-runtime" / "intents"
        pending.mkdir(parents=True)
        sentinel = pending / "owned.json"
        sentinel.write_text("retain this ownership intent")
        with patch.object(self.runtime, "worker", side_effect=[{"State": "Recovered", "Recovered": 0},
                {"Schema": "hsr-reader-recovery-v1", "State": "ReaderRecovered", "Recovered": 1}]) as worker:
            with self.assertRaises(ReleaseError) as caught:
                self.runtime.recover(COMMIT, "evidence")
        self.assertEqual("DEPLOY_READER_RECOVERY_UNCONFIRMED", str(caught.exception))
        self.assertEqual(2, worker.call_count)
        self.assertEqual("retain this ownership intent", sentinel.read_text())

    def test_reader_recovery_rejects_unconfirmed_receipt_without_deleting_ownership(self):
        pending = self.base / "state" / "evidence" / ".reader-runtime" / "intents"
        pending.mkdir(parents=True)
        sentinel = pending / "owned.json"
        sentinel.write_text("retain")
        valid = {"Schema": "hsr-reader-recovery-v1", "State": "ReaderRecovered", "Recovered": 1}
        for label, change in {"schema": {"Schema": "other"}, "state": {"State": "Ready"},
                "negative": {"Recovered": -1}, "boolean": {"Recovered": True}, "extra": {"Extra": True}}.items():
            with self.subTest(case=label), patch.object(self.runtime, "worker", side_effect=[
                    {"State": "Recovered", "Recovered": 0}, valid | change]):
                with self.assertRaises(ReleaseError) as caught:
                    self.runtime.recover(COMMIT, "evidence")
                self.assertEqual("DEPLOY_READER_RECOVERY_UNCONFIRMED", str(caught.exception))
                self.assertEqual("retain", sentinel.read_text())

    def test_empty_reader_inventory_preserves_legacy_release_recovery(self):
        pending = self.base / "state" / "evidence" / ".reader-runtime" / "intents"
        pending.mkdir(parents=True)
        with patch.object(self.runtime, "worker", return_value={"State": "Recovered", "Recovered": 0}) as worker:
            self.runtime.recover(COMMIT, "evidence")
        worker.assert_called_once_with(COMMIT, "evidence", "--recover")

    def test_readiness_accepts_exact_observed_bounds_without_database_or_goal_claim(self):
        for count in (1, 2048):
            with self.subTest(case="nodes_" + str(count)):
                with patch.object(self.runtime, "worker", return_value=self.ready() | {"ObservedNodes": count}) as worker:
                    self.runtime.probe(COMMIT)
                worker.assert_called_once_with(COMMIT, "readiness", "--check-runtime")

    def test_unconfirmed_readiness_facts_cannot_become_healthy(self):
        changes = {"schema": {"Schema": "other"}, "state": {"State": "Empty"},
                   "image": {"Image": "sha256:" + "d" * 64}, "empty": {"ObservedNodes": 0},
                   "too_many": {"ObservedNodes": 2049}, "boolean_nodes": {"ObservedNodes": True},
                   "digest": {"Sha256": "not a digest"}, "null_digest": {"Sha256": None},
                   "database": {"DatabaseAccessed": True}, "persisted": {"ObservationPersisted": True},
                   "input": {"DispatchedInputs": 1}, "boolean_input": {"DispatchedInputs": False},
                   "goal": {"GoalVerified": True}, "unknown": {"extra": "unexpected"},
                   "recovery": {"Recovered": -1}, "boolean_recovery": {"Recovered": True}}
        for label, change in changes.items():
            with self.subTest(case=label), patch.object(self.runtime, "worker", return_value=self.ready() | change):
                with self.assertRaises(ReleaseError) as caught:
                    self.runtime.probe(COMMIT)
                self.assertEqual("DEPLOY_RUNTIME_UNCONFIRMED", str(caught.exception))

    def test_recovery_requires_explicit_current_schema_and_nonnegative_count(self):
        for label, receipt in {"state": {"State": "Empty", "Recovered": 0},
                               "negative": {"State": "Recovered", "Recovered": -1},
                               "boolean": {"State": "Recovered", "Recovered": True},
                               "extra": {"State": "Recovered", "Recovered": 0, "extra": 1},
                               "missing": {"State": "Recovered"}, "root": []}.items():
            with self.subTest(case=label), patch.object(self.runtime, "worker", return_value=receipt):
                with self.assertRaises(ReleaseError) as caught:
                    self.runtime.recover(COMMIT, "evidence")
                self.assertEqual("DEPLOY_RECOVERY_UNCONFIRMED", str(caught.exception))

    def test_stop_requires_inactive_service_and_zero_actual_main_pid(self):
        with patch("host_runtime.run_checked", side_effect=["", "inactive", "0"]) as run:
            self.runtime.stop()
        self.assertEqual(["/usr/bin/systemctl", "stop", "hsr-observation.service"], run.call_args_list[0].args[0])
        for label, state, pid in [("still_active", "active", "42"), ("pending_pid", "inactive", "42")]:
            with self.subTest(case=label), patch("host_runtime.run_checked", side_effect=["", state, pid]):
                with self.assertRaises(ReleaseError) as caught:
                    self.runtime.stop()
                self.assertEqual("DEPLOY_STOP_UNCONFIRMED", str(caught.exception))

    def test_first_inactive_service_starts_without_resetting_an_unloaded_unit(self):
        invocation = "d" * 32
        status = "InvocationID=" + invocation + "\nActiveState=active\nMainPID=123"
        with patch("host_runtime.run_checked", side_effect=["inactive", "", invocation, "active", '{"State":"Empty"}', status]) as run:
            self.runtime.start_ready()
        commands = [call.args[0] for call in run.call_args_list]
        self.assertEqual(["/usr/bin/systemctl", "show", "hsr-observation.service", "--property=ActiveState", "--value"], commands[0])
        self.assertEqual(["/usr/bin/systemctl", "start", "hsr-observation.service"], commands[1])
        self.assertFalse(any("reset-failed" in command for command in commands))
        self.assertIn("_SYSTEMD_INVOCATION_ID=" + invocation, commands[4])
        self.assertEqual(6, len(commands))

    def test_failed_service_resets_failed_state_before_starting_a_fresh_invocation(self):
        invocation = "d" * 32
        status = "InvocationID=" + invocation + "\nActiveState=active\nMainPID=123"
        with patch("host_runtime.run_checked", side_effect=["failed", "", "", invocation, "active", '{"State":"Empty"}', status]) as run:
            self.runtime.start_ready()
        commands = [call.args[0] for call in run.call_args_list]
        self.assertEqual(["/usr/bin/systemctl", "reset-failed", "hsr-observation.service"], commands[1])
        self.assertEqual(["/usr/bin/systemctl", "start", "hsr-observation.service"], commands[2])
        self.assertIn("_SYSTEMD_INVOCATION_ID=" + invocation, commands[5])
        self.assertEqual(7, len(commands))

    def test_running_pending_or_unknown_service_never_starts_again(self):
        for state in ("active", "activating", "deactivating", "unknown", ""):
            with self.subTest(case=state or "empty"), patch("host_runtime.run_checked", return_value=state) as run:
                with self.assertRaises(ReleaseError) as caught:
                    self.runtime.start_ready()
                self.assertEqual("DEPLOY_START_STATE_UNCONFIRMED", str(caught.exception))
                self.assertEqual(1, run.call_count)
                self.assertEqual(["/usr/bin/systemctl", "show", "hsr-observation.service", "--property=ActiveState", "--value"], run.call_args.args[0])

    def test_failed_state_reset_failure_cannot_start_a_service(self):
        with patch("host_runtime.run_checked", side_effect=["failed", ReleaseError("DEPLOY_COMMAND_FAILED")]) as run:
            with self.assertRaises(ReleaseError) as caught:
                self.runtime.start_ready()
        self.assertEqual("DEPLOY_COMMAND_FAILED", str(caught.exception))
        self.assertEqual(2, run.call_count)
        self.assertEqual(["/usr/bin/systemctl", "reset-failed", "hsr-observation.service"], run.call_args.args[0])

    def test_database_readiness_uses_only_fresh_invocation_and_current_service(self):
        invocation = "d" * 32
        status = "InvocationID=" + invocation + "\nActiveState=active\nMainPID=123"
        with patch("host_runtime.run_checked", side_effect=["inactive", "", invocation, "active", '{"State":"Empty"}', status]) as run:
            self.runtime.start_ready()
        self.assertIn("_SYSTEMD_INVOCATION_ID=" + invocation, run.call_args_list[4].args[0])
        self.assertEqual(["/usr/bin/systemctl", "show", "hsr-observation.service", "--property=InvocationID",
                          "--property=ActiveState", "--property=MainPID"],
                         run.call_args_list[5].args[0])

    def test_changed_invocation_cannot_reuse_an_earlier_empty_receipt(self):
        status = "InvocationID=" + "e" * 32 + "\nActiveState=active\nMainPID=123"
        with patch("host_runtime.run_checked", side_effect=["inactive", "", "d" * 32, "active", '{"State":"Empty"}', status]):
            with self.assertRaises(ReleaseError) as caught:
                self.runtime.start_ready()
        self.assertEqual("DEPLOY_INVOCATION_CHANGED", str(caught.exception))

    def test_process_exit_after_empty_receipt_cannot_be_reported_ready(self):
        for label, state, pid in [("exited", "inactive", "0"), ("missing_pid", "active", "0")]:
            status = "InvocationID=" + "d" * 32 + "\nActiveState=" + state + "\nMainPID=" + pid
            with self.subTest(case=label), patch("host_runtime.run_checked",
                    side_effect=["inactive", "", "d" * 32, "active", '{"State":"Empty"}', status]):
                with self.assertRaises(ReleaseError) as caught:
                    self.runtime.start_ready()
                self.assertEqual("DEPLOY_SERVICE_UNCONFIRMED", str(caught.exception))

    def test_missing_database_receipt_times_out_without_a_stub_health_claim(self):
        with patch("host_runtime.run_checked", side_effect=["inactive", "", "d" * 32]), \
                patch("host_runtime.time.monotonic", side_effect=[0, 31]):
            with self.assertRaises(ReleaseError) as caught:
                self.runtime.start_ready()
        self.assertEqual("DEPLOY_DATABASE_READINESS_UNCONFIRMED", str(caught.exception))

    def test_failed_service_cannot_be_healthy_from_old_journal_output(self):
        with patch("host_runtime.run_checked", side_effect=["inactive", "", "d" * 32, "failed"]) as run:
            with self.assertRaises(ReleaseError) as caught:
                self.runtime.start_ready()
        self.assertEqual("DEPLOY_SERVICE_UNCONFIRMED", str(caught.exception))
        self.assertEqual(4, run.call_count)


if __name__ == "__main__":
    unittest.main()
