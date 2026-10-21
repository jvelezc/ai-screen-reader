from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
from activation import activate, current_commit, replace_current
from release_bundle import ReleaseError

OLD = "a" * 40
NEW = "b" * 40


class SimulatedRuntime:
    """Faults in orchestration only; these tests make no systemd/browser claim."""
    def __init__(self, base, failures=()):
        self.base = base
        self.calls = []
        self.failures = set(failures)

    def invoke(self, call):
        self.calls.append((call, current_commit(self.base)))
        if call in self.failures:
            raise ReleaseError("SIMULATED_OPERATION_UNCONFIRMED")

    def stop(self):
        self.invoke("stop")

    def recover(self, commit, scope):
        self.invoke("recover:" + commit + ":" + scope)

    def probe(self, commit):
        self.invoke("probe:" + commit)

    def start_ready(self):
        self.invoke("start:" + current_commit(self.base))


class ActivationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hsr-activation-proof-")
        self.base = Path(self.temporary.name)
        (self.base / "releases" / OLD).mkdir(parents=True)
        (self.base / "releases" / NEW).mkdir()
        self.sentinel = self.base / "unrelated"
        self.sentinel.write_bytes(b"retain")
        self.states = []

    def tearDown(self):
        self.assertEqual(b"retain", self.sentinel.read_bytes())
        self.temporary.cleanup()

    def test_activation_requires_stop_recovery_and_runtime_probe_before_switch(self):
        replace_current(self.base, OLD)
        runtime = SimulatedRuntime(self.base)
        result = activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual({"State": "Ready", "Commit": NEW, "Previous": OLD}, result)
        self.assertEqual([("stop", OLD), ("recover:" + OLD + ":evidence", OLD),
                          ("probe:" + NEW, OLD), ("start:" + NEW, NEW)], runtime.calls)
        self.assertEqual(NEW, current_commit(self.base))
        self.assertEqual(["Stopping", "RecoveringPrevious", "CheckingRuntime", "Starting", "Ready"], self.states)
        self.assertEqual([], list(self.base.glob(".current-*")))

    def test_failed_new_start_drains_both_scopes_before_rollback(self):
        replace_current(self.base, OLD)
        runtime = SimulatedRuntime(self.base, ["start:" + NEW])
        result = activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual("RolledBack", result["State"])
        self.assertEqual(OLD, current_commit(self.base))
        self.assertEqual([("stop", OLD), ("recover:" + OLD + ":evidence", OLD),
                          ("probe:" + NEW, OLD), ("start:" + NEW, NEW), ("stop", NEW),
                          ("recover:" + NEW + ":evidence", NEW),
                          ("recover:" + NEW + ":readiness", NEW),
                          ("probe:" + OLD, NEW), ("start:" + OLD, OLD)], runtime.calls)
        self.assertEqual("RolledBack", self.states[-1])

    def test_failed_probe_does_not_switch_and_recovers_before_previous_restart(self):
        replace_current(self.base, OLD)
        runtime = SimulatedRuntime(self.base, ["probe:" + NEW])
        result = activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual("RolledBack", result["State"])
        self.assertEqual([("stop", OLD), ("recover:" + OLD + ":evidence", OLD),
                          ("probe:" + NEW, OLD), ("stop", OLD),
                          ("recover:" + NEW + ":readiness", OLD),
                          ("probe:" + OLD, OLD), ("start:" + OLD, OLD)], runtime.calls)

    def test_unknown_stop_does_not_probe_switch_or_restart(self):
        replace_current(self.base, OLD)
        runtime = SimulatedRuntime(self.base, ["stop"])
        with self.assertRaises(ReleaseError):
            activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual([("stop", OLD)], runtime.calls)
        self.assertEqual(OLD, current_commit(self.base))

    def test_unknown_previous_recovery_holds_closed_before_probe(self):
        replace_current(self.base, OLD)
        runtime = SimulatedRuntime(self.base, ["recover:" + OLD + ":evidence"])
        with self.assertRaises(ReleaseError):
            activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual([("stop", OLD), ("recover:" + OLD + ":evidence", OLD)], runtime.calls)
        self.assertEqual(OLD, current_commit(self.base))

    def test_unknown_failed_native_cleanup_cannot_restart_previous_release(self):
        replace_current(self.base, OLD)
        runtime = SimulatedRuntime(self.base, ["start:" + NEW, "recover:" + NEW + ":evidence"])
        with self.assertRaises(ReleaseError):
            activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual(NEW, current_commit(self.base))
        self.assertEqual(("recover:" + NEW + ":evidence", NEW), runtime.calls[-1])
        self.assertNotIn("RolledBack", self.states)
        self.assertNotIn(("start:" + OLD, OLD), runtime.calls)

    def test_unknown_failed_probe_cleanup_cannot_restore_previous_release(self):
        replace_current(self.base, OLD)
        runtime = SimulatedRuntime(self.base, ["probe:" + NEW, "recover:" + NEW + ":readiness"])
        with self.assertRaises(ReleaseError):
            activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual(("recover:" + NEW + ":readiness", OLD), runtime.calls[-1])
        self.assertNotIn(("start:" + OLD, OLD), runtime.calls)

    def test_first_failed_release_is_stopped_and_has_no_current_pointer(self):
        runtime = SimulatedRuntime(self.base, ["start:" + NEW])
        with self.assertRaises(ReleaseError) as caught:
            activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual("DEPLOY_FIRST_RELEASE_FAILED", str(caught.exception))
        self.assertIsNone(current_commit(self.base))
        self.assertEqual(("recover:" + NEW + ":readiness", NEW), runtime.calls[-1])
        self.assertEqual("Stopped", self.states[-1])

    def test_ready_receipt_write_failure_still_stops_and_recovers_before_rollback(self):
        replace_current(self.base, OLD)
        runtime = SimulatedRuntime(self.base)
        def record(state):
            if state == "Ready":
                raise OSError("receipt unavailable")
            self.states.append(state)
        result = activate(self.base, NEW, runtime, record)
        self.assertEqual("RolledBack", result["State"])
        self.assertEqual(OLD, current_commit(self.base))
        self.assertIn(("recover:" + NEW + ":evidence", NEW), runtime.calls)

    def test_foreign_pointer_is_rejected_before_any_runtime_action(self):
        (self.base / "current").symlink_to("../unrelated", target_is_directory=True)
        runtime = SimulatedRuntime(self.base)
        with self.assertRaises(ReleaseError) as caught:
            activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual("DEPLOY_CURRENT_INVALID", str(caught.exception))
        self.assertEqual([], runtime.calls)

    def test_missing_release_cannot_become_current(self):
        with self.assertRaises(ReleaseError) as caught:
            replace_current(self.base, "c" * 40)
        self.assertEqual("DEPLOY_RELEASE_INVALID", str(caught.exception))
        self.assertIsNone(current_commit(self.base))

    def test_pointer_flush_failure_after_rename_still_recovers_and_removes_first_pointer(self):
        runtime = SimulatedRuntime(self.base)
        def change_then_fail(base, commit):
            replace_current(base, commit)
            raise OSError("directory flush failed after rename")
        with patch("activation.replace_current", side_effect=change_then_fail):
            with self.assertRaises(ReleaseError) as caught:
                activate(self.base, NEW, runtime, self.states.append)
        self.assertEqual("DEPLOY_FIRST_RELEASE_FAILED", str(caught.exception))
        self.assertIsNone(current_commit(self.base))
        self.assertEqual([("stop", None), ("probe:" + NEW, None), ("stop", NEW),
                          ("recover:" + NEW + ":evidence", NEW),
                          ("recover:" + NEW + ":readiness", NEW)], runtime.calls)
        self.assertEqual("Stopped", self.states[-1])


if __name__ == "__main__":
    unittest.main()
