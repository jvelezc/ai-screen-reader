import json
import os
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
from deploy import Incoming, Receipt
import provision
from release_bundle import ReleaseError


class DeployIoTests(unittest.TestCase):
    def test_fragmented_pipe_input_fills_requested_header_without_changing_bytes(self):
        stream = Incoming(42)
        with patch("deploy.select.select", return_value=([42], [], [])), \
                patch("deploy.os.read", side_effect=[b"x", b"yz"]) as read:
            self.assertEqual(b"xyz", stream.read(3))
        self.assertEqual(3, stream.received)
        self.assertEqual([(42, 3), (42, 2)], [call.args for call in read.call_args_list])

    def test_pipe_eof_preserves_only_bytes_actually_received(self):
        stream = Incoming(42)
        with patch("deploy.select.select", return_value=([42], [], [])), \
                patch("deploy.os.read", side_effect=[b"x", b""]):
            self.assertEqual(b"x", stream.read(3))
        self.assertEqual(1, stream.received)

    def test_idle_pipe_cannot_hold_deployment_indefinitely(self):
        with patch("deploy.select.select", return_value=([], [], [])), patch("deploy.os.read") as read:
            with self.assertRaises(ReleaseError) as caught:
                Incoming(42).read(512)
        self.assertEqual("DEPLOY_INPUT_IDLE_TIMEOUT", str(caught.exception))
        read.assert_not_called()

    def test_input_budget_rejects_next_byte_after_actual_limit(self):
        stream = Incoming(42)
        stream.received = 16 * 1024**2
        with patch("deploy.MAX_TOTAL", 2), patch("deploy.select.select", return_value=([42], [], [])), \
                patch("deploy.os.read", side_effect=[b"ab", b"c"]):
            self.assertEqual(b"ab", stream.read(2))
            with self.assertRaises(ReleaseError) as caught:
                stream.read(1)
        self.assertEqual("DEPLOY_INPUT_TOO_LARGE", str(caught.exception))

    def test_events_are_distinct_immutable_files_without_environment_data(self):
        with tempfile.TemporaryDirectory(prefix="hsr-events-proof-") as name:
            root = Path(name)
            (root / "deployments").mkdir()
            receipt = Receipt(root, "a" * 40)
            with patch.dict(os.environ, {"HSR_WORKER_DATABASE": "private-database-canary"}):
                receipt.record("Verified")
                first = next((root / "deployments").iterdir())
                original = first.read_bytes()
                receipt.record("ImageLoaded")
            files = sorted((root / "deployments").iterdir())
            self.assertEqual(2, len(files))
            self.assertEqual(original, first.read_bytes())
            events = [json.loads(path.read_text()) for path in files]
            self.assertEqual([1, 2], [event["Sequence"] for event in events])
            self.assertEqual(["Verified", "ImageLoaded"], [event["State"] for event in events])
            self.assertEqual({receipt.run}, {event["Run"] for event in events})
            self.assertEqual({"a" * 40}, {event["Commit"] for event in events})
            self.assertNotIn("private-database-canary", b"".join(path.read_bytes() for path in files).decode())

    def test_atomic_install_preserves_original_when_replace_fails(self):
        with tempfile.TemporaryDirectory(prefix="hsr-install-proof-") as name:
            root = Path(name)
            target = root / "owned"
            target.write_bytes(b"original")
            with patch("provision.os.replace", side_effect=OSError("injected rename failure")):
                with self.assertRaises(OSError):
                    provision.install(target, b"replacement", 0o600)
            self.assertEqual(b"original", target.read_bytes())
            self.assertEqual(["owned"], [path.name for path in root.iterdir()])
            provision.install(target, b"replacement", 0o600)
            self.assertEqual(b"replacement", target.read_bytes())
            self.assertEqual(0o600, stat.S_IMODE(target.stat().st_mode))
            self.assertEqual(["owned"], [path.name for path in root.iterdir()])


if __name__ == "__main__":
    unittest.main()
