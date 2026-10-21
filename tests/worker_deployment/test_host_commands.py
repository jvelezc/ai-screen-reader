from pathlib import Path
import signal
import subprocess
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
import host_runtime
from release_bundle import ReleaseError


class HostCommandTests(unittest.TestCase):
    def command(self, source):
        return [sys.executable, "-I", "-c", source]

    def test_actual_child_returns_bounded_utf8_output(self):
        self.assertEqual("confirmed", host_runtime.run_checked(self.command("print('confirmed')")))

    def test_actual_child_failure_does_not_disclose_its_private_stderr(self):
        with self.assertRaises(ReleaseError) as caught:
            host_runtime.run_checked(self.command("import sys;print('private-database-error-canary',file=sys.stderr);sys.exit(7)"))
        self.assertEqual("DEPLOY_COMMAND_FAILED", str(caught.exception))
        self.assertNotIn("private-database-error-canary", str(caught.exception))

    def test_actual_large_output_fails_before_it_can_enter_a_receipt(self):
        for label, target in [("stdout", "stdout"), ("stderr", "stderr")]:
            with self.subTest(case=label), self.assertRaises(ReleaseError) as caught:
                host_runtime.run_checked(self.command("import sys;sys." + target + ".write('x'*(1024**2+1))"))
            self.assertEqual("DEPLOY_COMMAND_OUTPUT_INVALID", str(caught.exception))

    def test_timeout_stops_and_joins_only_the_new_owned_process_group(self):
        other = subprocess.Popen(self.command("import time;time.sleep(60)"), start_new_session=True)
        actual = []
        real_start = subprocess.Popen
        def observe_start(*args, **kwargs):
            process = real_start(*args, **kwargs)
            actual.append(process)
            return process
        try:
            with patch("host_runtime.subprocess.Popen", side_effect=observe_start), \
                    patch("host_runtime.os.killpg", wraps=host_runtime.os.killpg) as stop:
                with self.assertRaises(ReleaseError) as caught:
                    host_runtime.run_checked(self.command("import time;time.sleep(60)"), timeout=0.2)
            self.assertEqual("DEPLOY_COMMAND_TIMEOUT", str(caught.exception))
            self.assertEqual(1, len(actual))
            stop.assert_called_once_with(actual[0].pid, signal.SIGKILL)
            self.assertEqual(-signal.SIGKILL, actual[0].returncode)
            self.assertIsNone(other.poll())
        finally:
            if other.poll() is None:
                other.terminate()
            other.wait(timeout=5)


if __name__ == "__main__":
    unittest.main()
