"""Root-owned forced-command target. Installs only the verified private worker."""
import datetime
import errno
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
FAILURE_CODES = frozenset({
    "PRIVATE_DEPLOYMENT_UNCONFIRMED", "DEPLOY_IDENTITY_INVALID", "DEPLOY_COMMAND_INVALID",
    "DEPLOY_ROOT_INVALID", "DEPLOY_LOCK_BUSY", "DEPLOY_INPUT_IDLE_TIMEOUT", "DEPLOY_INPUT_TOO_LARGE",
    "DEPLOY_EXISTING_RELEASE_MISMATCH", "DEPLOY_STAGE_CLEANUP_INVALID", "DEPLOY_FIRST_RELEASE_FAILED",
    "DEPLOY_CURRENT_INVALID", "DEPLOY_CURRENT_CHANGED", "DEPLOY_RELEASE_INVALID", "DEPLOY_COMMAND_TIMEOUT",
    "DEPLOY_STORAGE_CAPACITY", "DEPLOY_FILESYSTEM_DENIED", "DEPLOY_PATH_UNAVAILABLE",
    "RELEASE_DIGEST_MISMATCH", "RELEASE_FILES_MISMATCH", "RELEASE_IDENTITY_MISMATCH",
    "RELEASE_MANIFEST_INVALID", "RELEASE_PATH_INVALID", "RELEASE_FILE_UNEXPECTED",
    "CONTROL_CONFIGURATION_INVALID", "CONTROL_RUNTIME_UNCONFIRMED", "CONTROL_STOP_UNCONFIRMED",
})
PHASES = frozenset({
    "Identity", "Command", "Root", "Lock", "Receipt", "Staging", "Receiving", "Verified",
    "InstallingRelease", "PreparingRuntime", "ImageLoaded", "Stopping", "RecoveringPrevious",
    "CheckingRuntime", "Starting", "Ready", "FailedDeploymentDrained", "Stopped", "RolledBack",
    "Enabling", "Enabled", "Cleanup",
})


def failure_code(error):
    # Never use a provider/OS exception message, even when it resembles a code.
    if (type(error) is ReleaseError and len(error.args) == 1
            and isinstance(error.args[0], str) and error.args[0] in FAILURE_CODES):
        return error.args[0]
    if isinstance(error, OSError):
        if error.errno in (errno.ENOSPC, errno.EDQUOT):
            return "DEPLOY_STORAGE_CAPACITY"
        if error.errno in (errno.EACCES, errno.EPERM):
            return "DEPLOY_FILESYSTEM_DENIED"
        if error.errno == errno.ENOENT:
            return "DEPLOY_PATH_UNAVAILABLE"
    return "PRIVATE_DEPLOYMENT_UNCONFIRMED"


def remember_failure(progress, error):
    if "Code" not in progress:
        progress["Code"] = failure_code(error)
        progress["FailurePhase"] = progress["Phase"]


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

    def record(self, state, *, code=None, phase=None):
        require(code is None and phase is None or isinstance(code, str) and isinstance(phase, str)
                and code in FAILURE_CODES and phase in PHASES,
                "PRIVATE_DEPLOYMENT_UNCONFIRMED")
        self.sequence += 1
        receipt = {"Schema": "hsr-private-deployment-event-v1", "Commit": self.commit,
                   "Run": self.run, "Sequence": self.sequence, "State": state,
                   "Utc": datetime.datetime.now(datetime.timezone.utc).isoformat()}
        if code is not None:
            receipt.update(Code=code, Phase=phase)
        path = self.folder / (self.run + "-" + str(self.sequence).zfill(3) + ".json")
        with path.open("x", encoding="utf-8") as output:
            json.dump(receipt, output, separators=(",", ":"))
            output.flush()
            os.fsync(output.fileno())


def main(progress):
    require(os.geteuid() == 0 and len(sys.argv) == 1, "DEPLOY_IDENTITY_INVALID")
    progress["Phase"] = "Command"
    command = os.environ.get("SSH_ORIGINAL_COMMAND", "")
    match = re.fullmatch(r"deploy ([a-f0-9]{40})", command)
    require(match is not None, "DEPLOY_COMMAND_INVALID")
    commit = match[1]
    progress["Commit"] = commit
    os.umask(0o077)
    progress["Phase"] = "Root"
    require(BASE.is_dir() and not BASE.is_symlink()
            and BASE.stat().st_uid == 0 and BASE.stat().st_mode & 0o022 == 0
            and (BASE / ".managed").read_text(encoding="utf-8").strip() == MARKER, "DEPLOY_ROOT_INVALID")
    progress["Phase"] = "Lock"
    with (BASE / "deployment.lock").open("a") as lock:
        try:
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise ReleaseError("DEPLOY_LOCK_BUSY") from None
        progress["Phase"] = "Receipt"
        receipt = Receipt(BASE, commit)
        progress["Run"] = receipt.run
        def record(state):
            progress["Phase"] = state
            receipt.record(state)
        record("Receiving")
        progress["Phase"] = "Staging"
        stage = Path(tempfile.mkdtemp(prefix="release-", dir=BASE / "incoming"))
        try:
            progress["Phase"] = "Receiving"
            manifest = extract_verified(Incoming(sys.stdin.fileno()), stage, commit)
            record("Verified")
            progress["Phase"] = "InstallingRelease"
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
            progress["Phase"] = "PreparingRuntime"
            runtime = DeploymentRuntime(BASE)
            runtime.prepare(commit)
            record("ImageLoaded")
            result = activate(BASE, commit, runtime, record)
            if result["State"] == "Ready":
                progress["Phase"] = "Enabling"
                runtime.enable()
                record("Enabled")
            result["Schema"] = "hsr-private-deployment-v1"
            result["Run"] = receipt.run
        except Exception as error:
            remember_failure(progress, error)
            try:
                receipt.record("Unconfirmed", code=progress["Code"], phase=progress["FailurePhase"])
                progress["FailureEvent"] = "Persisted"
            except Exception:
                progress["FailureEvent"] = "Unavailable"
            raise
        finally:
            # Only our newly created private staging directory, never releases/state or an input path.
            progress["Phase"] = "Cleanup"
            try:
                if stage.exists():
                    require(stage.resolve().parent == (BASE / "incoming").resolve()
                            and stage.name.startswith("release-"), "DEPLOY_STAGE_CLEANUP_INVALID")
                    shutil.rmtree(stage)
                progress["Cleanup"] = "Completed"
            except Exception as error:
                progress["Cleanup"] = "Unconfirmed"
                progress["CleanupCode"] = failure_code(error)
                remember_failure(progress, error)
                raise
        print(json.dumps(result, separators=(",", ":")))
        return 0 if result["State"] == "Ready" else 3


def entrypoint():
    progress = {"Phase": "Identity", "Commit": None, "Run": None,
                "FailureEvent": "NotCreated", "Cleanup": "NotStarted", "CleanupCode": None}
    try:
        return main(progress)
    except Exception as error:
        remember_failure(progress, error)
        receipt = {"Schema": "hsr-private-deployment-failure-v1", "State": "Unconfirmed",
                   "Code": progress["Code"], "Phase": progress["FailurePhase"],
                   "Commit": progress["Commit"], "Run": progress["Run"],
                   "FailureEvent": progress["FailureEvent"], "Cleanup": progress["Cleanup"],
                   "CleanupCode": progress["CleanupCode"], "AutomaticRetry": False,
                   "Utc": datetime.datetime.now(datetime.timezone.utc).isoformat()}
        print(json.dumps(receipt, separators=(",", ":")), file=sys.stderr)
        return 3


if __name__ == "__main__":
    sys.exit(entrypoint())
