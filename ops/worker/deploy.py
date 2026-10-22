"""Root-owned forced-command target. Installs only the verified private worker."""
import datetime
import fcntl
import json
import os
from pathlib import Path
import re
import select
import shutil
import sys
import tempfile
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parent))
from activation import activate
from control_runtime import DeploymentRuntime
from release_bundle import MAX_TOTAL, ReleaseError, extract_verified, require, verify_directory

BASE = Path("/srv/headless-screen-reader")
MARKER = "headless-screen-reader:gpnijaxrgtwzwtrqsbcu:v1"


class Incoming:
    def __init__(self, descriptor):
        self.descriptor = descriptor
        self.received = 0

    def read(self, count):
        wanted = min(count, 1024**2)
        chunks = []
        received = 0
        while received < wanted:
            ready, _, _ = select.select([self.descriptor], [], [], 30)
            require(bool(ready), "DEPLOY_INPUT_IDLE_TIMEOUT")
            data = os.read(self.descriptor, wanted - received)
            if not data:
                break
            chunks.append(data)
            received += len(data)
            self.received += len(data)
            require(self.received <= MAX_TOTAL + 16 * 1024**2, "DEPLOY_INPUT_TOO_LARGE")
        return b"".join(chunks)


class Receipt:
    def __init__(self, base, commit):
        self.commit = commit
        self.folder = base / "deployments"
        self.run = uuid.uuid4().hex
        self.sequence = 0

    def record(self, state):
        self.sequence += 1
        receipt = {"Schema": "hsr-private-deployment-event-v1", "Commit": self.commit,
                   "Run": self.run, "Sequence": self.sequence, "State": state,
                   "Utc": datetime.datetime.now(datetime.timezone.utc).isoformat()}
        path = self.folder / (self.run + "-" + str(self.sequence).zfill(3) + ".json")
        with path.open("x", encoding="utf-8") as output:
            json.dump(receipt, output, separators=(",", ":"))
            output.flush()
            os.fsync(output.fileno())


def main():
    require(os.geteuid() == 0 and len(sys.argv) == 1, "DEPLOY_IDENTITY_INVALID")
    command = os.environ.get("SSH_ORIGINAL_COMMAND", "")
    match = re.fullmatch(r"deploy ([a-f0-9]{40})", command)
    require(match is not None, "DEPLOY_COMMAND_INVALID")
    commit = match[1]
    os.umask(0o077)
    require(BASE.is_dir() and not BASE.is_symlink()
            and BASE.stat().st_uid == 0 and BASE.stat().st_mode & 0o022 == 0
            and (BASE / ".managed").read_text().strip() == MARKER, "DEPLOY_ROOT_INVALID")
    with (BASE / "deployment.lock").open("a") as lock:
        fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        receipt = Receipt(BASE, commit)
        receipt.record("Receiving")
        stage = Path(tempfile.mkdtemp(prefix="release-", dir=BASE / "incoming"))
        try:
            manifest = extract_verified(Incoming(sys.stdin.fileno()), stage, commit)
            receipt.record("Verified")
            release = BASE / "releases" / commit
            if release.exists() or release.is_symlink():
                require(not release.is_symlink() and verify_directory(release, commit) == manifest,
                        "DEPLOY_EXISTING_RELEASE_MISMATCH")
            else:
                for path in stage.rglob("*"):
                    path.chmod(0o755 if path.is_dir() else 0o644)
                stage.chmod(0o755)
                stage.rename(release)
                descriptor = os.open(BASE / "releases", os.O_RDONLY | os.O_DIRECTORY)
                try:
                    os.fsync(descriptor)
                finally:
                    os.close(descriptor)
            runtime = DeploymentRuntime(BASE)
            runtime.prepare(commit)
            receipt.record("ImageLoaded")
            result = activate(BASE, commit, runtime, receipt.record)
            if result["State"] == "Ready":
                runtime.enable()
                receipt.record("Enabled")
            result["Schema"] = "hsr-private-deployment-v1"
            result["Run"] = receipt.run
            print(json.dumps(result, separators=(",", ":")))
            return 0 if result["State"] == "Ready" else 3
        except Exception:
            receipt.record("Unconfirmed")
            raise
        finally:
            # Only our newly created private staging directory, never releases/state or an input path.
            if stage.exists():
                require(stage.resolve().parent == (BASE / "incoming").resolve()
                        and stage.name.startswith("release-"), "DEPLOY_STAGE_CLEANUP_INVALID")
                shutil.rmtree(stage)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        print('{"State":"Unconfirmed","Code":"PRIVATE_DEPLOYMENT_UNCONFIRMED"}', file=sys.stderr)
        sys.exit(3)
